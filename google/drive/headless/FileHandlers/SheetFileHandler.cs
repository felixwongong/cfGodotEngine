#if CF_GOOGLE_DRIVE

using System.IO;
using System.Threading.Tasks;
using cfEngine.Serialize;
using CofyDev.Xml.Doc;
using Google.Apis.Download;
using Google.Apis.Drive.v3;

namespace cfGodotEngine.GoogleDrive
{
    public struct SheetFileHandler : FileHandler
    {
        public IDownloadProgress DownloadWithStatus(FilesResource filesResource, in FileHandler.DownloadRequest downloadRequest)
        {
            var fileId = downloadRequest.googleFileId;
            var request = filesResource.Export(fileId, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using var sheetMemoryStream = new MemoryStream();
            var status = request.DownloadWithStatus(sheetMemoryStream);

            var sheetByte = sheetMemoryStream.ToArray();
            var sheetData = CofyXmlDocParser.ParseExcel(sheetByte);
            var serialized = JsonSerializer.Instance.Serialize(sheetData);
            var fullPath = Path.Combine(downloadRequest.rootDirectoryInfo.FullName, $"{downloadRequest.localName}.json");
            File.WriteAllText(fullPath, serialized);
            return status;
        }

        public async Task<IDownloadProgress> DownloadAsync(FilesResource filesResource, FileHandler.DownloadRequest downloadRequest)
        {
            var fileId = downloadRequest.googleFileId;
            var request = filesResource.Export(fileId, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using var sheetMemoryStream = new MemoryStream();
            var status = await request.DownloadAsync(sheetMemoryStream);

            var sheetByte = sheetMemoryStream.ToArray();
            var sheetData = CofyXmlDocParser.ParseExcel(sheetByte);
            var serialized = await JsonSerializer.Instance.SerializeAsync(sheetData);
            var fullPath = Path.Combine(downloadRequest.rootDirectoryInfo.FullName, $"{downloadRequest.localName}.json");
            await File.WriteAllTextAsync(fullPath, serialized);
            return status;
        }
    }
}

#endif