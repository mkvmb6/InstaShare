using InstaShare.Models;
using InstaShare.Services;
using Newtonsoft.Json;
using System.IO;

namespace InstaShare.Schedulers
{
    class FileDeletionScheduler
    {
        IFileManager _fileManager;
        private string uploadedFilesJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.UploadedFilesJson);
        public FileDeletionScheduler()
        {
            _fileManager = new GoogleDriveFileManager();
        }
        public async Task DeleteExpiredFiles()
        {
            if (!File.Exists(uploadedFilesJsonPath))
            {
                return; // No records to process
            }
            var list = JsonConvert.DeserializeObject<List<UploadRecord>>(File.ReadAllText(uploadedFilesJsonPath));
            var now = DateTime.UtcNow;
            var updatedList = new List<UploadRecord>();

            foreach (var item in list)
            {
                if ((now - item.UploadTimeUtc).TotalDays >= 2)
                {
                    await _fileManager.DeleteFile(item.FileId);
                }
                else
                {
                    updatedList.Add(item);
                }
            }

            File.WriteAllText(uploadedFilesJsonPath, JsonConvert.SerializeObject(updatedList));
        }

        public void SaveFileRecord(string fileId, string filePath, string link)
        {
            List<UploadRecord> records = new List<UploadRecord>();

            if (File.Exists(uploadedFilesJsonPath))
            {
                string existingData = File.ReadAllText(uploadedFilesJsonPath);
                if (!string.IsNullOrWhiteSpace(existingData))
                {
                    records = JsonConvert.DeserializeObject<List<UploadRecord>>(existingData);
                }
            }

            records.Add(new UploadRecord
            {
                FileId = fileId,
                FilePath = filePath,
                ShareableLink = link,
                UploadTimeUtc = DateTime.UtcNow
            });

            File.WriteAllText(uploadedFilesJsonPath, JsonConvert.SerializeObject(records, Formatting.Indented));
        }

    }
}
