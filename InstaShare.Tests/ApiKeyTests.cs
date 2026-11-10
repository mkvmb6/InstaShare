using InstaShare.FileManagers.Impl;
using InstaShare.Services;
using System.IO;
using System.Net;
using System.Net.Http;

namespace InstaShare.Tests
{
    public sealed class ApiKeyTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InstaShare.Tests", Guid.NewGuid().ToString());
        private string KeyFile => Path.Combine(_dir, "apikey.bin");

        public ApiKeyTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private const string Key = "isk_AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcd";

        [Fact]
        public void SavesEncryptedAndReadsBack()
        {
            new ApiKeyStore(KeyFile).Set(Key);

            Assert.Equal(Key, new ApiKeyStore(KeyFile).Get());
            // The file on disk isn't the key in plain text.
            Assert.DoesNotContain("isk_", File.ReadAllText(KeyFile));
        }

        [Fact]
        public void RemovingDeletesTheFile()
        {
            var store = new ApiKeyStore(KeyFile);
            store.Set(Key);
            store.Set("  ");

            Assert.Null(store.Get());
            Assert.False(File.Exists(KeyFile));
            Assert.Null(new ApiKeyStore(KeyFile).Get());
        }

        [Fact]
        public void DamagedFileCountsAsNoKey()
        {
            File.WriteAllBytes(KeyFile, [1, 2, 3, 4]);
            Assert.Null(new ApiKeyStore(KeyFile).Get());
        }

        [Theory]
        [InlineData(Key, true)]
        [InlineData("isk_short", false)]
        [InlineData("sk_AbCdEfGhIjKlMnOpQrStUvWxYz0123456789", false)]
        [InlineData("isk_has spaces in it AbCdEfGhIjKlMnOp", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ChecksTheShape(string? key, bool expected) => Assert.Equal(expected, ApiKeyStore.LooksValid(key));

        [Fact]
        public void MasksAllButTheEnd() => Assert.Equal("isk_…abcd", ApiKeyStore.Mask(Key));

        [Fact]
        public async Task SendsTheKeyAsABearerToken()
        {
            var handler = new RecordingHandler();
            var key = Key;
            var api = new ShareApi(new HttpClient(handler), "https://api.test", () => key);

            await api.CreateShare(isFolder: true, null, TimeSpan.FromHours(1), CancellationToken.None);
            Assert.Equal($"Bearer {Key}", handler.Authorization);

            // A key changed in Settings applies to the next request without restarting.
            key = null;
            await api.CreateShare(isFolder: true, null, TimeSpan.FromHours(1), CancellationToken.None);
            Assert.Null(handler.Authorization);
        }

        [Fact]
        public async Task ServerRefusalSurfacesAsUnauthorized()
        {
            var api = new ShareApi(new HttpClient(new RecordingHandler(HttpStatusCode.Unauthorized, """{"error":"That API key isn't valid."}""")), "https://api.test", () => Key);

            var error = await Assert.ThrowsAsync<ShareApiException>(() => api.CreateShare(true, null, TimeSpan.FromHours(1), CancellationToken.None));
            Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
            Assert.Equal("That API key isn't valid.", error.Message);
        }

        private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string? body = null) : HttpMessageHandler
        {
            public string? Authorization { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Authorization = request.Headers.Authorization?.ToString();
                var json = body ?? """{"prefix":"00000000-0000-4000-8000-000000000000/","token":"t","expiresUtc":"2026-11-10T12:00:00Z"}""";
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
            }
        }
    }
}
