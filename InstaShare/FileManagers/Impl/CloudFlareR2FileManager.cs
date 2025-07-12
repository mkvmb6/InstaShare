using Amazon.S3;
using InstaShare.Services;
using System.IO;
using System.Net.Http;

namespace InstaShare.FileManagers.Impl
{
    public class CloudFlareR2FileManager : IFileManager
    {
        private readonly IAmazonS3 _s3;
        private const string _bucketName = "instashare";
        private const string _publicBaseUrl = "https://f59116491dc293d67d65c68edc5c5142.r2.cloudflarestorage.com";
        public CloudFlareR2FileManager()
        {
            _s3 = R2ClientFactory.CreateR2Client();
        }
        public Task DeleteFile(string fileId)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetOrCreateFolder(string folderName, string? parentFolderId = null)
        {
            throw new NotImplementedException();
        }

        public Task<string> ShareFolderAndGetLink(string folderId)
        {
            throw new NotImplementedException();
        }

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double, string, string> reportProgress = null)
        {
            var fileName = Path.GetFileName(filePath);
            var fileId = $"instashare/{fileName}".Replace("\\", "/");
            var sharedLink = $"{_publicBaseUrl}/{fileId}";
            using var http = new HttpClient();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var totalSize = fileStream.Length;

            using var progressStream = new ProgressStream(fileStream, totalSize, sharedLink, reportProgress);

            var content = new StreamContent(progressStream);
            content.Headers.Add("x-amz-acl", "public-read"); // must match presign headers
            content.Headers.ContentLength = totalSize;

            // Optional: Add progress support later here via custom content

            var response = await http.PutAsync(_s3.GetPreSignedUrl(fileName), content);
            response.EnsureSuccessStatusCode();

            return (fileId, sharedLink);
        }


        public Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, string driveRootFolderName, Action<string, string> statusCallback = null)
        {
            throw new NotImplementedException();
        }
    }
}
