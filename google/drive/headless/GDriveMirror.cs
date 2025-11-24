#if CF_GOOGLE_DRIVE

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using cfEngine;
using Godot;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using GoogleFile = Google.Apis.Drive.v3.Data.File;
using ILogger = cfEngine.ILogger;

namespace cfGodotEngine.GoogleDrive;

public struct RefreshStatus {
    public readonly GoogleFile file;
    public readonly IDownloadProgress status;
    public readonly float progress;

    public RefreshStatus(GoogleFile file, IDownloadProgress status, float progress) {
        this.file = file;
        this.status = status;
        this.progress = progress;
    }
}

public struct RefreshRequest {
    public IList<GoogleFile> googleFiles;
    public Func<GoogleFile, Res<Optional<SettingItem>, Exception>> getSetting;
    public IChangeHandler changeHandler;
}

public struct RefreshResult {
    public string newChangeChecksumToken;
}

public interface IFileMirrorHandler {
    IEnumerable<Task<RefreshStatus>> RefreshFilesAsync(DriveService driveService, RefreshRequest request);
    void RefreshFiles(DriveService driveService, in RefreshRequest request);
}

public partial class DriveMirror {
    private CancellationTokenSource _refreshCancelToken;
    private readonly IFileMirrorHandler _mirrorHandler;
    private readonly ILogger logger;

    public DriveMirror(IFileMirrorHandler mirrorHandler, ILogger logger) {
        _mirrorHandler = mirrorHandler;
        this.logger = logger;
    }

    private DriveService CreateDriveService() {
        var setting = DriveMirrorSetting.GetSetting();
        var credentialJson = setting.serviceAccountCredentialJson;
        if (string.IsNullOrEmpty(credentialJson)) {
            logger.LogInfo("[GDriveMirror.CreateFileRequest] setting.serviceAccountCredentialJson is null, refresh failed");
            return null;
        }

        var credential = GoogleCredential.FromJson(credentialJson)
            .CreateScoped(DriveService.ScopeConstants.Drive, DriveService.ScopeConstants.DriveMetadata);

        logger.LogInfo("Creating Drive Service");
        return new DriveService(new BaseClientService.Initializer() {
            HttpClientInitializer = credential,
            ApiKey = "ApiKeyTest"
        });
    }

    private FilesResource.ListRequest CreateFileRequest(DriveService service) {
        const string FIELDS = "files(id, name, mimeType, modifiedTime, md5Checksum, size)";
        var request = service.Files.List();
        request.Fields = FIELDS;
        return request;
    }

    public Task<IEnumerable<RefreshStatus>> ClearAllAndRefreshAsync() {
        var setting = DriveMirrorSetting.GetSetting();
        setting.changeChecksumToken = string.Empty;
        return RefreshAsync();
    }
    
    public async Task<IEnumerable<RefreshStatus>> RefreshAsync() {
        GD.Print("[GDriveMirror.RefreshAsync] start refresh files");

        _refreshCancelToken = new CancellationTokenSource();

        var driveService = CreateDriveService();
        if (driveService == null) throw new Exception("[GDriveMirror.RefreshAsync] CreateDriveService failed");

        var request = CreateFileRequest(driveService);
        if (request == null) throw new Exception("[GDriveMirror.RefreshAsync] CreateFileRequest failed");

        if (string.IsNullOrEmpty(driveService.ApiKey))
            throw new Exception("[GDriveMirror.RefreshAsync] DriveService.ApiKey is null or empty. Please check your service account credentials.");
        
        var changeHandler = new ChangeHandler(DriveUtil.godotLogger);
        var newChangeChecksumToken = await changeHandler.LoadChangesAsync(driveService, DriveMirrorSetting.GetSetting().changeChecksumToken);
        var response = await request.ExecuteAsync(_refreshCancelToken.Token);
        var refreshRequest = new RefreshRequest() {
            googleFiles = response.Files,
            getSetting = GetSetting,
            changeHandler = changeHandler
        };

        var result = await Task.WhenAll(_mirrorHandler.RefreshFilesAsync(driveService, refreshRequest));
        
        DriveMirrorSetting.GetSetting().changeChecksumToken = newChangeChecksumToken;
        return result;
    }

    public void Refresh() {
        logger.LogInfo("[GDriveMirror.Refresh] start refresh files");

        var driveService = CreateDriveService();
        if (driveService == null) return;

        var request = CreateFileRequest(driveService);
        if (request == null) return;

        var changeHandler = new ChangeHandler(DriveUtil.godotLogger);
        var newChecksumToken =
            changeHandler.LoadChanges(driveService, DriveMirrorSetting.GetSetting().changeChecksumToken);
        var response = request.Execute();
        var refreshRequest = new RefreshRequest() {
            googleFiles = response.Files,
            getSetting = GetSetting,
            changeHandler = changeHandler
        };

        try {
            _mirrorHandler.RefreshFiles(driveService, in refreshRequest);
        }
        catch (Exception e) {
            logger.LogException(new Exception("[GDriveMirror.Refresh] refresh files failed", e));
            return;
        }

        DriveMirrorSetting.GetSetting().changeChecksumToken = newChecksumToken;
        logger.LogInfo("[GDriveMirror.Refresh] refresh files succeed");
    }

    private Res<Optional<SettingItem>, Exception> GetSetting(GoogleFile file) {
        var filesSetting = DriveMirrorSetting.GetSetting();
        if (filesSetting == null || filesSetting.settingMap == null)
            return Res.Err<Optional<SettingItem>>(new Exception("GDriveMirrorSetting is not initialized."));

        if (!filesSetting.settingMap.TryGetValue(file.Id, out var setting)) {
            return Res.Ok(Optional.None<SettingItem>());
        }

        return Res.Ok(Optional.Some(setting));
    }
}

#endif