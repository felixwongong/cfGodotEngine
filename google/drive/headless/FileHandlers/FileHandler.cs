#if CF_GOOGLE_DRIVE

using System.IO;
using System.Threading.Tasks;
using cfEngine.Util;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace cfGodotEngine.GoogleDrive
{
    public interface FileHandler
    {
        public struct DownloadRequest
        {
            public string googleFileId;
            public DirectoryInfo rootDirectoryInfo;
            public string localName;
            public IChangeHandler changeHandler;
        }

        public struct FileItem
        {
            public PathSegment RelativePathSegment;
            public GoogleFile googleFile;
        }

        public IDownloadProgress DownloadWithStatus(FilesResource filesResource, in DownloadRequest downloadRequest);
        public Task<IDownloadProgress> DownloadAsync(FilesResource filesResource, DownloadRequest downloadRequest);
    }
}

#endif
