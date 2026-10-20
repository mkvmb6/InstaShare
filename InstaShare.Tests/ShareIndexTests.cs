using InstaShare.FileManagers.Impl;
using InstaShare.Models;
using Newtonsoft.Json.Linq;

namespace InstaShare.Tests
{
    public class ShareIndexTests
    {
        private static FileIndex Entry(string path) => new() { path = path, url = $"https://cdn/{path}", size = 3, status = UploadStatus.Uploading };

        [Fact]
        public async Task PublishesNameKindExpiryAndFiles()
        {
            string? written = null;
            var expires = new DateTime(2026, 10, 21, 8, 30, 0, DateTimeKind.Utc);
            var entry = Entry("Photos/a.jpg");
            var index = new IndexPublisher(new ShareIndexDocument { name = "Photos", kind = "folder", expiresUtc = expires, files = [entry] },
                (json, _) => { written = json; return Task.CompletedTask; });

            index.SetStatus(entry, UploadStatus.Uploaded);
            await index.FlushAsync(CancellationToken.None);

            var doc = JObject.Parse(written!);
            Assert.Equal("Photos", (string?)doc["name"]);
            Assert.Equal("folder", (string?)doc["kind"]);
            Assert.Equal("2026-10-21T08:30:00Z", doc["expiresUtc"]!.ToObject<DateTime>().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Assert.Equal("uploaded", (string?)doc["files"]![0]!["status"]);
        }

        [Fact]
        public void ReadsTheOldBareArrayFormat()
        {
            var doc = ShareIndexDocument.Parse("""[{"path":"a.txt","url":"u","size":1,"status":"failed"}]""");
            Assert.Null(doc.name);
            Assert.Equal("a.txt", Assert.Single(doc.files).path);
        }

        [Fact]
        public void ReadsTheNewFormat()
        {
            var doc = ShareIndexDocument.Parse("""{"name":"F","kind":"folder","files":[{"path":"F/a","url":"u","size":1,"status":"uploaded"}]}""");
            Assert.Equal("F", doc.name);
            Assert.Single(doc.files);
        }
    }
}
