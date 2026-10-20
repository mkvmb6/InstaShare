using InstaShare.Models;
using Newtonsoft.Json;
using System.IO;

namespace InstaShare.Services
{
    /// <summary>
    /// The list of shares in uploadedFiles.json, and their deletion once they expire.
    /// </summary>
    internal sealed class ShareHistory
    {
        private readonly IFileManager _fileManager;
        private readonly string _path;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);

        public ShareHistory(IFileManager fileManager, string path, string? legacyPath = null)
        {
            _fileManager = fileManager;
            _path = path;
            if (legacyPath != null && !File.Exists(path) && File.Exists(legacyPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(legacyPath, path);
            }
        }

        /// <summary>
        /// Raised after any change, on the thread that made it.
        /// </summary>
        public event EventHandler? Changed;

        public IReadOnlyList<UploadRecord> GetAll()
        {
            lock (_lock)
                return Load();
        }

        public void Add(UploadRecord record) => Mutate(records => records.Add(record));

        public void Update(string fileId, Action<UploadRecord> change) =>
            Mutate(records =>
            {
                foreach (var record in records.Where(r => r.FileId == fileId))
                    change(record);
            });

        public void Remove(string fileId) => Mutate(records => records.RemoveAll(r => r.FileId == fileId));

        /// <summary>
        /// Deletes a share from storage and then from the history.
        /// </summary>
        public async Task Delete(string fileId, CancellationToken cancellationToken = default)
        {
            await _fileManager.DeleteShare(fileId, cancellationToken);
            Remove(fileId);
        }

        /// <summary>
        /// Deletes every expired share. One that fails to delete stays in the history and is tried again next time.
        /// </summary>
        /// <param name="isInUse">Shares still being uploaded are skipped even if they have expired.</param>
        /// <returns>How many shares were deleted, and how many couldn't be.</returns>
        public async Task<(int deleted, int failed)> DeleteExpired(DateTime nowUtc, Func<string, bool>? isInUse = null, CancellationToken cancellationToken = default)
        {
            // Startup, the timer and the button can all ask at once; one pass is enough.
            await _cleanupLock.WaitAsync(cancellationToken);
            try
            {
                var expired = GetAll().Where(r => r.EffectiveExpiresUtc <= nowUtc && isInUse?.Invoke(r.FileId) != true).ToList();
                int deleted = 0, failed = 0;
                foreach (var record in expired)
                {
                    try
                    {
                        await Delete(record.FileId, cancellationToken);
                        deleted++;
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        failed++;
                    }
                }
                return (deleted, failed);
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        private void Mutate(Action<List<UploadRecord>> change)
        {
            lock (_lock)
            {
                var records = Load();
                change(records);
                AtomicFile.WriteAllText(_path, JsonConvert.SerializeObject(records, Formatting.Indented));
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private List<UploadRecord> Load()
        {
            if (!File.Exists(_path))
                return [];
            var json = File.ReadAllText(_path);
            return string.IsNullOrWhiteSpace(json) ? [] : JsonConvert.DeserializeObject<List<UploadRecord>>(json) ?? [];
        }
    }
}
