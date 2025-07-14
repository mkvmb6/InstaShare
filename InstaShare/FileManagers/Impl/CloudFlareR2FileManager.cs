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

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, Action<double, string, string>? reportProgress = null)
        {
            var fileName = Path.GetFileName(filePath);
            var folderId = $"{fileName}-{Guid.NewGuid()}";
            var fileId = $"{folderId}/{fileName}";
            var sharedLink = $"{Constants.AppBaseUrl}/{folderId}";
            using var http = new HttpClient();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var totalSize = fileStream.Length;

            await UploadIndexJson(fileName, totalSize, folderId, folderId, sharedLink, UploadStatus.Uploading);
            using var progressStream = new ProgressStream(fileStream, totalSize, sharedLink, reportProgress);

            var content = new StreamContent(progressStream);
            content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
            content.Headers.ContentLength = totalSize;

            var response = await http.PutAsync(_s3.GetPreSignedUrl(fileId), content);
            response.EnsureSuccessStatusCode();

            await UploadIndexJson(fileName, totalSize, folderId, folderId, sharedLink, UploadStatus.Uploaded);
            return (fileId, sharedLink);
        }

        public async Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, Action<string, string>? reportProgress = null)
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

            index = GetIndex(files, folderName, baseFolder, localRootPath);
            await UploadIndexJson([.. index], uniqueFolderId, sharedLink);

            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (file, cancellationToken) =>
            {
                var relativePath = Path.GetRelativePath(localRootPath, file).Replace("\\", "/");
                var fileId = $"{baseFolder}/{relativePath}";

                reportProgress?.Invoke("Uploading: " + relativePath, sharedLink);

                using var http = new HttpClient();
                using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var totalSize = fileStream.Length;
                var timeoutSeconds = Math.Max(300, totalSize / 50000); // Assuming min speed around 50KBps
                http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                using var progressStream = new ProgressStream(fileStream, totalSize, sharedLink);

                var content = new StreamContent(progressStream);
                content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
                content.Headers.ContentLength = totalSize;

                var response = await http.PutAsync(_s3.GetPreSignedUrl(fileId), content, cancellationToken);
                response.EnsureSuccessStatusCode();
                var path = $"{folderName}/{relativePath}";
                index.First(idx => idx.path == path).status = UploadStatus.Uploaded;
                await UploadIndexJson([.. index], uniqueFolderId, sharedLink);

            });

            return (baseFolder, sharedLink);
        }

        private static ConcurrentBag<FileIndex> GetIndex(string[] files, string folderName, string baseFolder, string localRootPath)
        {
            var index = new ConcurrentBag<FileIndex>();
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(localRootPath, file).Replace("\\", "/");
                var fileId = $"{baseFolder}/{relativePath}";
                index.Add(new FileIndex
                {
                    path = $"{folderName}/{relativePath}",
                    size = new FileInfo(file).Length,
                    url = $"{Constants.CdnBaseUrl}/{fileId}",
                    status = UploadStatus.Uploading
                });
            }
            return index;
        }

        private async Task UploadIndexJson(string fileName, long fileSize, string folderId, string baseFolder, string sharedLink, string status)
        {
            var index = new List<FileIndex>();
            var uploadRecord = new FileIndex
            {
                path = fileName,
                url = $"{Constants.CdnBaseUrl}/{folderId}/{fileName}",
                size = fileSize,
                status = status
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
