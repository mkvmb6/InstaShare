using System.IO;
using InstaShare.Models;
using InstaShare.Services;

namespace InstaShare.Tests
{
    public sealed class ShareHistoryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InstaShare.Tests", Guid.NewGuid().ToString());
        private readonly FakeFileManager _files = new();

        private string HistoryPath => Path.Combine(_dir, "uploadedFiles.json");

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private static UploadRecord Record(string id, DateTime uploaded, DateTime? expires = null, string? token = "t") => new()
        {
            FileId = id,
            FilePath = $@"C:\files\{id}",
            ShareableLink = $"https://example.test/{id}",
            UploadTimeUtc = uploaded,
            ExpiresUtc = expires,
            Token = token,
        };

        [Fact]
        public void LegacyRecordsExpireAfterTwoDays()
        {
            var uploaded = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(uploaded.AddDays(2), Record("a", uploaded).EffectiveExpiresUtc);
        }

        [Fact]
        public void MigratesLegacyFileOnce()
        {
            Directory.CreateDirectory(_dir);
            var legacy = Path.Combine(_dir, "legacy.json");
            File.WriteAllText(legacy, """[{"FileId":"x","FilePath":"C:\\x","ShareableLink":"l","UploadTimeUtc":"2025-07-13T18:54:49Z"}]""");

            var history = new ShareHistory(_files, HistoryPath, legacy);

            var record = Assert.Single(history.GetAll());
            Assert.Equal("x", record.FileId);
            Assert.Equal(ShareStatus.Completed, record.Status);
            Assert.Null(record.ExpiresUtc);
        }

        [Fact]
        public void RemovesOnlyExpiredAndIdleEntries()
        {
            var now = DateTime.UtcNow;
            var history = new ShareHistory(_files, HistoryPath);
            history.Add(Record("old", now.AddDays(-3), token: null));           // legacy 2-day expiry, past
            history.Add(Record("fresh", now.AddHours(-1), now.AddDays(6)));     // chose 7 days
            history.Add(Record("hour", now.AddHours(-2), now.AddHours(-1)));    // chose 1 hour, past
            history.Add(Record("busy", now.AddDays(-3), now.AddDays(-1)));      // expired but still uploading

            var removed = history.RemoveExpired(now, id => id == "busy");

            Assert.Equal(2, removed);
            Assert.Empty(_files.Deleted); // The server deletes; the app only forgets
            Assert.Equal(["fresh", "busy"], history.GetAll().Select(r => r.FileId));
        }

        [Fact]
        public async Task DeleteSendsTheShareToken()
        {
            var history = new ShareHistory(_files, HistoryPath);
            history.Add(Record("a", DateTime.UtcNow, token: "secret-token"));

            await history.Delete("a");

            Assert.Equal([("a", "secret-token")], _files.DeletedWithToken);
            Assert.Empty(history.GetAll());
        }

        [Fact]
        public async Task LegacyShareCantBeDeletedEarly()
        {
            var history = new ShareHistory(_files, HistoryPath);
            history.Add(Record("legacy", DateTime.UtcNow, token: null));

            await Assert.ThrowsAsync<InvalidOperationException>(() => history.Delete("legacy"));
            Assert.Single(history.GetAll());
        }

        [Fact]
        public async Task FailedDeleteKeepsTheEntry()
        {
            var history = new ShareHistory(_files, HistoryPath);
            history.Add(Record("broken", DateTime.UtcNow));
            _files.FailFor.Add("broken");

            await Assert.ThrowsAsync<IOException>(() => history.Delete("broken"));
            Assert.Single(history.GetAll());
        }

        [Fact]
        public void UpdateAndChangedEvent()
        {
            var history = new ShareHistory(_files, HistoryPath);
            var changes = 0;
            history.Changed += (_, _) => changes++;

            history.Add(Record("a", DateTime.UtcNow));
            history.Update("a", r => r.Status = ShareStatus.CompletedWithErrors);

            Assert.Equal(ShareStatus.CompletedWithErrors, Assert.Single(history.GetAll()).Status);
            Assert.Equal(2, changes);
            Assert.False(File.Exists(HistoryPath + ".tmp"));
        }

        private sealed class FakeFileManager : IFileManager
        {
            public List<(string id, string token)> DeletedWithToken { get; } = [];
            public IEnumerable<string> Deleted => DeletedWithToken.Select(d => d.id);
            public HashSet<string> FailFor { get; } = [];

            public Task DeleteShare(string remoteId, string token, CancellationToken cancellationToken = default)
            {
                if (FailFor.Contains(remoteId))
                    throw new IOException("network down");
                DeletedWithToken.Add((remoteId, token));
                return Task.CompletedTask;
            }

            public Task<(string fileId, string sharedLink)> UploadFile(string filePath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadFolderWithStructure(string localRootPath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadItems(IReadOnlyList<string> paths, string shareName, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string token, IReadOnlyDictionary<string, string> localPaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
