using InstaShare.Models;
using InstaShare.Services;
using Newtonsoft.Json;
using System.IO;

namespace InstaShare.Schedulers
{
    class FileDeletionScheduler
    {
        IFileManager _fileManager;
        public FileDeletionScheduler()
        {
            _fileManager = new GoogleDriveFileManager();
        }
        public async Task DeleteExpiredFiles()
        {
            var list = JsonConvert.DeserializeObject<List<UploadRecord>>(File.ReadAllText(Constants.UploadedFilesJson));
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

            File.WriteAllText(Constants.UploadedFilesJson, JsonConvert.SerializeObject(updatedList));
        }

        public void SaveFileRecord(string fileId, string filePath, string link)
        {
            string jsonPath = Constants.UploadedFilesJson;
            List<UploadRecord> records = new List<UploadRecord>();

            if (File.Exists(jsonPath))
            {
                string existingData = File.ReadAllText(jsonPath);
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

            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(records, Formatting.Indented));
        }

    }
}
