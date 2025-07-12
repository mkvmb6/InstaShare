using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using System.IO;
using System.Reflection;

namespace InstaShare.Services
{
    public class GoogleDriveFileManager : IFileManager
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveFile };
        private DriveService _driveService;

        public GoogleDriveFileManager()
        {
            Authenticate();
        }

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, string parentFolderId, Action<double, string, string> reportProgress = null)
        {
            var guid = Guid.NewGuid();
            var folderName = $"{Path.GetFileName(filePath)}-{guid}";
            var folderId = await GetOrCreateFolder(folderName, parentFolderId);
            var sharedLink = await ShareFolderAndGetLink(folderId);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { folderId },
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
                        reportProgress?.Invoke((double)progress.BytesSent / stream.Length * 100,"", sharedLink);
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

            return (file.Id, sharedLink);
        }

        public async Task<string> GetOrCreateFolder(string folderName, string? parentFolderId = null)
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

            return await CreateFolder(folderName, parentFolderId);
        }

        private async Task<string> CreateFolder(string folderName, string? parentFolderId)
        {
            // Create new folder
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder"
            };
            if (parentFolderId != null)
            {
                fileMetadata.Parents = [parentFolderId];
            }

            var folder = await _driveService.Files.Create(fileMetadata)
                .ExecuteAsync();

            return folder.Id;
        }

        public async Task<string> ShareFolderAndGetLink(string folderId)
        {
            // Make folder public
            var permission = new Permission
            {
                Type = "anyone",
                Role = "reader"
            };

            await _driveService.Permissions.Create(permission, folderId).ExecuteAsync();

            return $"https://drive.google.com/drive/folders/{folderId}?usp=sharing";
        }

        public async Task<(string folderId, string sharedLink)> UploadFolderWithStructure(string localRootPath, string driveRootFolderName, Action<string, string> statusCallback = null)
        {
            var rootDriveFolderId = await GetOrCreateFolder(driveRootFolderName);
            var currentFolderId = await CreateFolder(Path.GetFileName(localRootPath), rootDriveFolderId);
            var folderMap = new Dictionary<string, string>(); // local folder → Drive folder ID
            folderMap[localRootPath] = currentFolderId;
            var shareableLink = await ShareFolderAndGetLink(currentFolderId);

            var allDirs = Directory.GetDirectories(localRootPath, "*", SearchOption.AllDirectories);

            // Step 1: Create Drive folders matching the local structure
            foreach (var dir in allDirs)
            {
                string relativePath = Path.GetRelativePath(localRootPath, dir);
                string parentLocal = Directory.GetParent(dir).FullName;

                string driveParentId = folderMap[parentLocal];

                var folderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(dir),
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { driveParentId }
                };

                var folder = await _driveService.Files.Create(folderMetadata).ExecuteAsync();

                folderMap[dir] = folder.Id;
                statusCallback?.Invoke($"Created folder: {relativePath}", shareableLink);
            }

            // Step 2: Upload all files to their respective folders
            var allFiles = Directory.GetFiles(localRootPath, "*", SearchOption.AllDirectories);

            // Parallel upload of files to their respective folders
            await Parallel.ForEachAsync(allFiles, async (filePath, cancellationToken) =>
            {
                string fileFolder = Path.GetDirectoryName(filePath);
                string driveParentId = folderMap[fileFolder];

                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(filePath),
                    Parents = [driveParentId]
                };

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var uploadRequest = _driveService.Files.Create(fileMetadata, stream, "application/octet-stream");
                    uploadRequest.Fields = "id";
                    await uploadRequest.UploadAsync(cancellationToken);
                }

                string relativePath = Path.GetRelativePath(localRootPath, filePath);
                statusCallback?.Invoke($"Uploaded: {relativePath}", shareableLink);
            });

            return (rootDriveFolderId, shareableLink);
        }

        public async Task DeleteFile(string fileId)
        {
            await _driveService.Files.Delete(fileId).ExecuteAsync();
        }

        private void Authenticate()
        {
            var executingAssembly = Assembly.GetExecutingAssembly();
            var credPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
            using var stream = executingAssembly.GetManifestResourceStream("InstaShare.credentials.json");
            var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore(credPath, true)).Result;

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = Constants.AppName,
            });
        }
    }
}
