using InstaShare.Models;
using Newtonsoft.Json;
using System.IO;

namespace InstaShare.Services
{
    /// <summary>
    /// The list of shares in uploadedFiles.json. The server deletes shares when they expire; this only keeps the list tidy.
    /// </summary>
    internal sealed class ShareHistory
    {
        private readonly IFileManager _fileManager;
        private readonly string _path;
        private readonly object _lock = new();

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
        /// <exception cref="InvalidOperationException">The share is from before the InstaShare API and has no token.</exception>
        public async Task Delete(string fileId, CancellationToken cancellationToken = default)
        {
            var record = GetAll().FirstOrDefault(r => r.FileId == fileId);
            if (record?.Token is not { } token)
                throw new InvalidOperationException("This share was made by an older version of InstaShare and can't be deleted early. It's removed automatically when it expires.");
            await _fileManager.DeleteShare(fileId, token, cancellationToken);
            Remove(fileId);
        }

        /// <summary>
        /// Drops expired shares from the history. The server has already deleted them, or will within the hour.
        /// </summary>
        /// <param name="isInUse">Shares still being uploaded are kept even if they have expired.</param>
        /// <returns>How many entries were removed.</returns>
        public int RemoveExpired(DateTime nowUtc, Func<string, bool>? isInUse = null)
        {
            var removed = 0;
            Mutate(records => removed = records.RemoveAll(r => r.EffectiveExpiresUtc <= nowUtc && isInUse?.Invoke(r.FileId) != true));
            return removed;
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
