using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.IO;

namespace InstaShare.Services
{
    public class GoogleDriveFileManager : IFileManager
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private DriveService _driveService;

        public GoogleDriveFileManager()
        {
            Authenticate().Wait();
        }

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double> reportProgress = null)
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { parentFolderId },
            };

            FilesResource.CreateMediaUpload request;

            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                request = _driveService.Files.Create(fileMetadata, stream, "application/octet-stream");
                request.Fields = "id";

                request.ProgressChanged += progress =>
                {
                    if (progress.Status == UploadStatus.Uploading)
                    {
                        reportProgress?.Invoke((double)progress.BytesSent / stream.Length * 100);
                    }
                };

                await request.UploadAsync();
            }

            var file = request.ResponseBody;

            // Make file public
            var permission = new Permission()
            {
                Type = "anyone",
                Role = "reader"
            };
            await _driveService.Permissions.Create(permission, file.Id).ExecuteAsync();

            return (file.Id, $"https://drive.google.com/file/d/{file.Id}/view?usp=sharing");
        }

        public async Task<string> GetOrCreateFolder(string folderName)
        {
            // Search for existing folder
            var request = _driveService.Files.List();
            request.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{folderName}' and trashed = false";
            request.Fields = "files(id, name)";
            var result = await request.ExecuteAsync();

            if (result.Files.Count > 0)
            {
                return result.Files[0].Id; // Folder already exists
            }

            // Create new folder
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder"
            };

            var folder = await _driveService.Files.Create(fileMetadata)
                .ExecuteAsync();

            return folder.Id;
        }


        public async Task DeleteFile(string fileId)
        {
            await _driveService.Files.Delete(fileId).ExecuteAsync();
        }

        private async Task Authenticate()
        {
            using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = "token.json";
                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true));

                _driveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = Constants.AppName,
                });
            }
        }
    }
}
