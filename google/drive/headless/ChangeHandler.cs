#if CF_GOOGLE_DRIVE

using System.Collections.Generic;
using System.Threading.Tasks;
using cfEngine;
using cfEngine.Logging;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace cfGodotEngine.GoogleDrive
{
    public enum ChangeType
    {
        None,
        Modified,
        Removed
    }
    public struct ChangeInfo
    {
        public ChangeType type;
        public Optional<File> File;
    }
    
    public interface IChangeHandler
    {
        public string LoadChanges(DriveService driveService, string startPageToken);
        public Task<string> LoadChangesAsync(DriveService driveService, string startPageToken);
        public bool IsFileChanged(GoogleFile googleFile);
        public bool TryGetFileChange(File googleFile, out ChangeInfo? changeInfo);
    }
    
    public class ChangeHandler: IChangeHandler
    {
        private bool isInitialized = false;
        private bool isAllDirty = false;
        private List<Change> _changedFiles = new();
        public IReadOnlyList<Change> ChangedFiles => _changedFiles;

        private readonly ILogger logger;
        public ChangeHandler(ILogger logger)
        {
            this.logger = logger;
        }

        public string LoadChanges(DriveService driveService, string startPageToken)
        {
            _changedFiles.Clear();
            if (string.IsNullOrWhiteSpace(startPageToken))
            {
                var getTokenRequest = driveService.Changes.GetStartPageToken();
                var nextPageToken = getTokenRequest.Execute();
                isAllDirty = true;
                isInitialized = true;
                return nextPageToken.StartPageTokenValue;
            }

            var request = driveService.Changes.List(startPageToken);
            request.Fields = "nextPageToken,newStartPageToken,changes(fileId,removed,changeType,file(id,name,mimeType,modifiedTime,parents))";
            var response = request.Execute();
            foreach (var change in response.Changes)
            {
                if (change.File == null || change.File.Id == null)
                {
                    continue;
                }
                _changedFiles.Add(change);
            }
                
            isInitialized = true;
            return response.NewStartPageToken;
        }

        public async Task<string> LoadChangesAsync(DriveService driveService, string startPageToken)
        {
            _changedFiles.Clear();
            if (string.IsNullOrWhiteSpace(startPageToken))
            {
                var startPageTokenRequest = driveService.Changes.GetStartPageToken();
                var res = await startPageTokenRequest.ExecuteAsync();
                isAllDirty = true;
                isInitialized = true;
                return res.StartPageTokenValue;
            }
            
            var request = driveService.Changes.List(startPageToken);
            request.Fields = "nextPageToken,newStartPageToken,changes(fileId,removed,file(id,name,mimeType,modifiedTime,parents))";
            var response = await request.ExecuteAsync();
            foreach (var change in response.Changes)
            {
                if (change.File == null || change.File.Id == null)
                {
                    continue;
                }
                _changedFiles.Add(change);
            }
            
            isInitialized = true;
            return response.NewStartPageToken;
        }

        public bool IsFileChanged(GoogleFile googleFile)
        {
            if (!isInitialized)
            {
                logger.LogError("ChangeHandler is not initialized. Call LoadChanges or LoadChangesAsync first.");
                return false;
            }
            
            if (isAllDirty)
                return true;

            return TryGetFileChange(googleFile, out _);
        }

        public bool TryGetFileChange(File googleFile, out ChangeInfo? changeInfo)
        {
            changeInfo = null;
            if (!isInitialized)
            {
                logger.LogError("ChangeHandler is not initialized. Call LoadChanges or LoadChangesAsync first.");
                return false;
            }
            if (isAllDirty)
            {
                changeInfo = new ChangeInfo
                {
                    type = ChangeType.Modified,
                    File = Optional<File>.None()
                };
                return true;
            }
            
            foreach (var change in _changedFiles)
            {
                if (change.File.Id == googleFile.Id)
                {
                    ChangeType changeType;
                    if (change.Removed != null && change.Removed.Value)
                        changeType = ChangeType.Removed;
                    else
                        changeType = ChangeType.Modified;
                    
                    changeInfo = new ChangeInfo 
                    {
                        type = changeType,
                        File = change.File 
                    };
                    return true;
                }
            }

            return false;
        }
    }
}

#endif