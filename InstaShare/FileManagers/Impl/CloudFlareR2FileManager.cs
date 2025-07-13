using Amazon.S3;
using Amazon.S3.Model;
using InstaShare.Models;
using InstaShare.Services;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;

namespace InstaShare.FileManagers.Impl
{
    public class CloudFlareR2FileManager : IFileManager
    {
        private readonly IAmazonS3 _s3;
        public CloudFlareR2FileManager()
        {
            _s3 = R2ClientFactory.CreateR2Client();
        }
        public async Task DeleteFile(string fileId)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = Constants.R2BucketName,
                Key = fileId
            };
            await _s3.DeleteObjectAsync(request);
        }

        public Task<string> GetOrCreateFolder(string folderName, string? parentFolderId = null)
        {
            throw new NotImplementedException();
        }

        public Task<string> ShareFolderAndGetLink(string folderId)
        {
            throw new NotImplementedException();
        }

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double, string, string>? reportProgress = null)
        {
            var fileName = Path.GetFileName(filePath);
            var folderId = $"{fileName}-{Guid.NewGuid()}";
            var fileId = $"{folderId}/{fileName}";
            var sharedLink = $"{Constants.AppBaseUrl}/{folderId}";
            using var http = new HttpClient();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var totalSize = fileStream.Length;

            using var progressStream = new ProgressStream(fileStream, totalSize, sharedLink, reportProgress);

            var content = new StreamContent(progressStream);
            content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
            content.Headers.ContentLength = totalSize;

            var response = await http.PutAsync(_s3.GetPreSignedUrl(fileId), content);
            response.EnsureSuccessStatusCode();

            reportProgress?.Invoke(0, "Uploading index.json", sharedLink);
            await UploadIndexJson(fileName, totalSize, folderId, folderId, sharedLink);
            reportProgress?.Invoke(100, "Uploaded index.json", sharedLink);

            return (fileId, sharedLink);
        }

        public async Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, string driveRootFolderName, Action<string, string>? reportProgress = null)
        {
            if (!Directory.Exists(localRootPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {localRootPath}");
            }
            var uniqueFolderId = Guid.NewGuid().ToString();
            var files = Directory.GetFiles(localRootPath, "*", SearchOption.AllDirectories);
            var index = new ConcurrentBag<FileIndex>();
            var folderName = Path.GetFileName(localRootPath);
            var baseFolder = $"{uniqueFolderId}/{folderName}";
            var sharedLink = $"{Constants.AppBaseUrl}/{baseFolder}";

            await Parallel.ForEachAsync(files, async (file, cancellationToken) =>
            {
                var relativePath = Path.GetRelativePath(localRootPath, file).Replace("\\", "/");
                var fileId = $"{baseFolder}/{relativePath}";

                reportProgress?.Invoke("Uploading: " + relativePath, sharedLink);

                using var http = new HttpClient();
                using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var totalSize = fileStream.Length;

                using var progressStream = new ProgressStream(fileStream, totalSize, sharedLink);

                var content = new StreamContent(progressStream);
                content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
                content.Headers.ContentLength = totalSize;

                var response = await http.PutAsync(_s3.GetPreSignedUrl(fileId), content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var fileInfo = new FileInfo(file);
                index.Add(new FileIndex
                {
                    path = $"{folderName}/{relativePath}",
                    size = totalSize,
                    url = $"{Constants.CdnBaseUrl}/{fileId}"
                });

            });

            reportProgress?.Invoke("Uploading index.json", sharedLink);
            await UploadIndexJson([.. index], uniqueFolderId, sharedLink);
            reportProgress?.Invoke("Uploaded index.json", sharedLink);
            return (baseFolder, sharedLink);
        }

        private async Task UploadIndexJson(string fileName, long fileSize, string folderId, string baseFolder, string sharedLink)
        {
            var index = new List<FileIndex>();
            var uploadRecord = new FileIndex
            {
                path = fileName,
                url = $"{Constants.CdnBaseUrl}/{folderId}/{fileName}",
                size = fileSize,
            };
            index.Add(uploadRecord);
            await UploadIndexJson(index, baseFolder, sharedLink);
        }

        private async Task UploadIndexJson(List<FileIndex> index, string baseFolder, string sharedLink)
        {
            var indexJson = JsonConvert.SerializeObject(index);
            var indexKey = $"{baseFolder}/index.json";

            using var http = new HttpClient();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(indexJson));
            var content = new StreamContent(ms);
            content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
            content.Headers.ContentLength = ms.Length;
            var response = await http.PutAsync(_s3.GetPreSignedUrl(indexKey), content);
            response.EnsureSuccessStatusCode();
        }
    }
}
