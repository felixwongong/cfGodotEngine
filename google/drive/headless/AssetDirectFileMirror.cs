#if CF_GOOGLE_DRIVE

using System.Collections.Generic;
using cfEngine.Logging;
using cfEngine.Util;
using cfGodotEngine.GoogleDrive;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using GoogleFile = Google.Apis.Drive.v3.Data.File;
using SystemFile = System.IO.File;

namespace cfGodotTemplate.GoogleDrive
{
    public class AssetDirectFileMirror : IFileMirrorHandler
    {
        private readonly ILogger logger;
        private readonly string assetDirectoryPath;
        
        public AssetDirectFileMirror(ILogger logger, string assetDirectoryPath)
        {
            this.logger = logger;
            this.assetDirectoryPath = assetDirectoryPath;
        }

        public async IAsyncEnumerable<RefreshStatus> RefreshFilesAsync(DriveService driveService, RefreshRequest request)
        {
            var googleFiles = request.googleFiles;
            var getSetting = request.getSetting;
            var fileResource = driveService.Files;
            var changeHandler = request.changeHandler;
            for (var i = 0; i < googleFiles.Count; i++)
            {
                var googleFile = googleFiles[i];
                var getFileSetting = getSetting(googleFile);
                if (getFileSetting.TryGetError(out var error))
                {
                    logger.LogException(error);
                    continue;
                }
                
                if (!getFileSetting.TryGetValue(out var optionalSetting) || !optionalSetting.TryGetValue(out var setting))
                    continue;
                
                if (!DriveUtil.MimeFileHandlers.TryGetValue(googleFile.MimeType, out var handler))
                {
                    logger.LogInfo($"[AssetDirectFileMirror.RefreshFilesAsync] No handler found for mime type: {googleFile.MimeType}");
                    continue;
                }   
                
                var directory = DirectoryUtil.CreateDirectoryIfNotExists(assetDirectoryPath, setting.assetPath);
                var localFileName = GetLocalFileName(googleFile, setting);
                
                var result = await handler.DownloadAsync(
                    fileResource,
                    new FileHandler.DownloadRequest
                    {
                        googleFileId = googleFile.Id,
                        rootDirectoryInfo = directory,
                        localName = localFileName,
                        changeHandler = changeHandler
                    }
                );
                
                LogDownloadProgress(result, googleFile);
                if (result != null && result.Status == DownloadStatus.Completed)
                {
                    yield return new RefreshStatus(googleFile, result, (float)i / googleFiles.Count);
                }
                OnDownloadEnd(result, googleFile, setting);
            }
        }

        public void RefreshFiles(DriveService driveService, in RefreshRequest request)
        {
            var filesResource = driveService.Files;
            var googleFiles = request.googleFiles;
            var getSetting = request.getSetting;
            var changeHandler = request.changeHandler;
            foreach (var googleFile in googleFiles)
            {
                var getFileSetting = getSetting(googleFile);
                if (getFileSetting.TryGetError(out var err))
                {
                    logger.LogException(err);
                    continue;
                }

                if (!getFileSetting.TryGetValue(out var optionalSetting) || !optionalSetting.TryGetValue(out var setting))
                    continue;

                if (!DriveUtil.MimeFileHandlers.TryGetValue(googleFile.MimeType, out var handler))
                {
                    logger.LogInfo($"[AssetDirectFileMirror.RefreshFiles] No handler found for mime type: {googleFile.MimeType}");
                    continue;
                }

                var directory = DirectoryUtil.CreateDirectoryIfNotExists(assetDirectoryPath, setting.assetPath);
                var localFileName = GetLocalFileName(googleFile, setting);
                
                var result = handler.DownloadWithStatus(
                    filesResource,
                    new FileHandler.DownloadRequest
                    {
                        googleFileId = googleFile.Id,
                        rootDirectoryInfo = directory,
                        localName = localFileName,
                        changeHandler = changeHandler
                    }
                );

                LogDownloadProgress(result, googleFile);
                OnDownloadEnd(result, googleFile, setting);
            }
        }

        private void OnDownloadEnd(IDownloadProgress progress, GoogleFile googleFile, SettingItem setting)
        {
            if (progress is { Status: DownloadStatus.Completed })
            {
                DriveUtil.godotLogger.LogInfo($"[AssetDirectFileMirror.RefreshFiles] Download completed, google file: {googleFile.Name}");
                if (!setting.fileName.Equals(googleFile.Name)) 
                {
                    setting.fileName = googleFile.Name;
                    DriveUtil.godotLogger.LogInfo($"[AssetDirectFileMirror.RefreshFiles] Updated setting fileName to {googleFile.Name}");
                }
            }
        }

        private string GetLocalFileName(GoogleFile googleFile, SettingItem setting)
        {
            var localAssetName = setting.fileName;
            if (string.IsNullOrWhiteSpace(localAssetName))
            {
                localAssetName = $"{googleFile.Name}";
            }

            return localAssetName;
        }

        private void LogDownloadProgress(IDownloadProgress progress, GoogleFile googleFile)
        {
            switch (progress.Status)
            {
                case DownloadStatus.Completed:
                    logger.LogInfo($"[AssetDirectFileMirror.RefreshFiles] Download completed, google file: {googleFile.Name}");
                    break;
                case DownloadStatus.Failed:
                    logger.LogError($"[AssetDirectFileMirror.RefreshFiles] Download failed, google file: {googleFile.Name}, status: {progress.Status}\n Error: {progress.Exception?.Message}");
                    break;
                default:
                    logger.LogWarning($"[AssetDirectFileMirror.RefreshFiles] Download status: {progress.Status}, google file: {googleFile.WritersCanShare}");
                    break;
            }
        }
    }
}

#endif