#if CF_GOOGLE_DRIVE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfEngine.Util;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using DirectoryUtil = cfEngine.Util.DirectoryUtil;
using Path = System.IO.Path;

namespace cfGodotEngine.GoogleDrive
{
    public struct FolderMimeHandler : FileHandler
    {
        private class DownloadProgress : IDownloadProgress
        {
            private DownloadStatus _status;
            private long _byteDownloaded;
            private Exception _exception;

            public DownloadStatus Status { get { Commit(); return _status; } } 
            public long BytesDownloaded { get { Commit(); return _byteDownloaded; } }
            public Exception Exception { get { Commit(); return _exception; } }

            private List<IDownloadProgress> _progressList = new();
            private bool isCommitted = false;
            
            public void AddProgress(IDownloadProgress progress)
            {
                _progressList.Add(progress);
            }

            private void Commit()
            {
                if (isCommitted)
                    return;

                _status = _progressList.All(x => x.Status == DownloadStatus.Completed) ? DownloadStatus.Completed : DownloadStatus.Failed;
                _byteDownloaded = _progressList.Sum(x => x.BytesDownloaded);
                if (_progressList.Any(x => x.Exception != null))
                {
                    _exception = new AggregateException(_progressList.Where(x => x.Exception != null).Select(x => x.Exception));
                }
                else
                {
                    _exception = null;
                }
                isCommitted = true;
            }
        }
        
        private ILogger logger;
        private string assetDirectoryPath;
        public FolderMimeHandler(ILogger logger, string assetDirectoryPath)
        {
            this.logger = logger;
            this.assetDirectoryPath = assetDirectoryPath;
        }

        public IEnumerable<FileHandler.FileItem> GetFolderContent(FilesResource filesResource, string googleFileId)
        {
            var subItemRequest = CreateGoogleFolderItemRequest(filesResource, googleFileId);
            var result = subItemRequest.Execute();
            var googleFolderItems = result.Files;
            if (googleFolderItems.Count == 0)
                return Array.Empty<FileHandler.FileItem>();
            
            var fileItems = new List<FileHandler.FileItem>(googleFolderItems.Count);
            foreach (var folderItem in googleFolderItems)
            {
                using var pathBuilder = new PathSegmentBuilder();
                fileItems.Add(new FileHandler.FileItem()
                {
                    googleFile = folderItem,
                    RelativePathSegment = pathBuilder.AppendPath(folderItem.Name).Build(),
                });
            }

            return fileItems;
        }

        public async Task<IEnumerable<FileHandler.FileItem>> GetFolderContentAsync(FilesResource filesResource, string googleFileId)
        {          
            var subItemRequest = CreateGoogleFolderItemRequest(filesResource, googleFileId);
            var result = await subItemRequest.ExecuteAsync();
            var googleFolderItems = result.Files;
            if (googleFolderItems.Count == 0)
                return Array.Empty<FileHandler.FileItem>();
            
            var fileItems = new List<FileHandler.FileItem>(googleFolderItems.Count);
            foreach (var folderItem in googleFolderItems)
            {
                using var pathBuilder = new PathSegmentBuilder();
                fileItems.Add(new FileHandler.FileItem()
                {
                    googleFile = folderItem,
                    RelativePathSegment = pathBuilder.AppendPath(folderItem.Name).Build(),
                });
            }

            return fileItems;
        }

        public IDownloadProgress DownloadWithStatus(FilesResource filesResource, in FileHandler.DownloadRequest downloadRequest)
        {
            var changeHandler = downloadRequest.changeHandler;
            var folderContent = GetFolderContent(filesResource, downloadRequest.googleFileId);
            var directory = new DirectoryInfo(Path.Combine(downloadRequest.rootDirectoryInfo.FullName, downloadRequest.localName));
            
            var downloadProgress = new DownloadProgress(); 
            foreach (var folderFile in folderContent)
            {
                var googleFile = folderFile.googleFile;
                if (!changeHandler.TryGetFileChange(googleFile, out var getChangeInfo) || !getChangeInfo.HasValue)
                {
                    logger.LogInfo($"[FolderMimeHandler.DownloadWithStatus] File not changed, skipping download: {googleFile.Name}");
                    continue;
                }

                var fullPath = Path.Combine(directory.FullName, folderFile.RelativePathSegment.GetOsPath());
                var changeInfo = getChangeInfo.Value;
                if (changeInfo.type == ChangeType.Removed)
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    } else if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }
                    continue;
                }
                
                DirectoryUtil.CreateDirectoryIfNotExists(assetDirectoryPath, Path.GetDirectoryName(fullPath));
                if (!DriveUtil.MimeFileHandlers.TryGetValue(googleFile.MimeType, out var fileHandler))
                {
                    logger.LogWarning($"[FolderMimeHandler.DownloadWithStatus] No handler for mime type: {googleFile.MimeType}, file: {googleFile.Name} (ID: {googleFile.Id})");
                    continue;
                }

                var progress = fileHandler.DownloadWithStatus(filesResource, new FileHandler.DownloadRequest()
                {
                    googleFileId = googleFile.Id,
                    rootDirectoryInfo = directory,
                    localName =  googleFile.Name,
                    changeHandler = changeHandler
                });
                downloadProgress.AddProgress(progress);
            }

            return downloadProgress;
        }

        public async Task<IDownloadProgress> DownloadAsync(FilesResource filesResource, FileHandler.DownloadRequest downloadRequest)
        {
            var folderContent = await GetFolderContentAsync(filesResource, downloadRequest.googleFileId);
            var directory = new DirectoryInfo(Path.Combine(downloadRequest.rootDirectoryInfo.FullName, downloadRequest.localName));
            if (directory.Exists)
            {
                directory.Delete(true);
                logger.LogInfo($"[FolderMimeHandler.DownloadAsync] Clearing existing directory: {directory.FullName}");
            }
            var downloadProgress = new DownloadProgress();
            foreach (var folderFile in folderContent)
            {
                var googleFile = folderFile.googleFile;
                var fullPath = Path.Combine(directory.FullName, folderFile.RelativePathSegment.GetOsPath());
                DirectoryUtil.CreateDirectoryIfNotExists(assetDirectoryPath, Path.GetDirectoryName(fullPath));
                if (!DriveUtil.MimeFileHandlers.TryGetValue(googleFile.MimeType, out var fileHandler))
                {
                    logger.LogWarning($"[FolderMimeHandler.DownloadAsync] No handler for mime type: {googleFile.MimeType}, file: {googleFile.Name} (ID: {googleFile.Id})");
                    continue;
                }

                var progress = await fileHandler.DownloadAsync(filesResource, new FileHandler.DownloadRequest()
                {
                    googleFileId = googleFile.Id,
                    rootDirectoryInfo = directory,
                    localName =  googleFile.Name,
                    changeHandler = downloadRequest.changeHandler
                });
                downloadProgress.AddProgress(progress);
            }

            return downloadProgress;
        }

        private FilesResource.ListRequest CreateGoogleFolderItemRequest(FilesResource filesResource, string folderId)
        {
            var request = filesResource.List();
            request.Q = $"'{folderId}' in parents";
            request.Fields = "files(id, name, mimeType)";
            return request;
        }
    }
}

#endif