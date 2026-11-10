using InstaShare.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace InstaShare.Tests
{
    public sealed class UpdaterTests : IDisposable
    {
        private const string Repo = "mkvmb6/InstaShare";
        private const string Download = $"https://github.com/{Repo}/releases/download/v2.2.0/";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InstaShare.Tests", Guid.NewGuid().ToString());

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private static string Release(string tag = "v2.2.0", bool prerelease = false, string exeUrl = Download + "InstaShare.exe", bool withChecksum = true) => $$"""
            {
              "tag_name": "{{tag}}",
              "draft": false,
              "prerelease": {{(prerelease ? "true" : "false")}},
              "html_url": "https://github.com/{{Repo}}/releases/tag/{{tag}}",
              "assets": [
                { "name": "InstaShare.exe", "size": 1234, "browser_download_url": "{{exeUrl}}" }
                {{(withChecksum ? $$""", { "name": "InstaShare.exe.sha256", "size": 80, "browser_download_url": "{{Download}}InstaShare.exe.sha256" }""" : "")}}
              ]
            }
            """;

        [Fact]
        public void FindsANewerRelease()
        {
            var update = Updater.ParseRelease(Release(), Repo, new Version(2, 1, 0, 0));

            Assert.NotNull(update);
            Assert.Equal(new Version(2, 2, 0), update.Version);
            Assert.Equal(Download + "InstaShare.exe", update.ExeUrl);
            Assert.Equal(Download + "InstaShare.exe.sha256", update.ChecksumUrl);
            Assert.Equal(1234, update.Size);
        }

        [Theory]
        [InlineData("v2.1.0")]
        [InlineData("2.1.0.0")]
        [InlineData("v2.0.5")]
        [InlineData("not-a-version")]
        public void IgnoresReleasesThatArentNewer(string tag) =>
            Assert.Null(Updater.ParseRelease(Release(tag), Repo, new Version(2, 1, 0, 0)));

        [Fact]
        public void IgnoresPrereleasesAndReleasesWithoutBothFiles()
        {
            var current = new Version(2, 1, 0);
            Assert.Null(Updater.ParseRelease(Release(prerelease: true), Repo, current));
            Assert.Null(Updater.ParseRelease(Release(withChecksum: false), Repo, current));
        }

        [Theory]
        [InlineData("https://evil.example/InstaShare.exe")]
        [InlineData("https://github.com/someone-else/InstaShare/releases/download/v2.2.0/InstaShare.exe")]
        public void OnlyDownloadsFromThisReposReleases(string exeUrl) =>
            Assert.Null(Updater.ParseRelease(Release(exeUrl: exeUrl), Repo, new Version(2, 1, 0)));

        [Theory]
        [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789  InstaShare.exe\n", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        [InlineData("too-short  InstaShare.exe", null)]
        [InlineData("", null)]
        public void ReadsChecksumFiles(string text, string? expected) => Assert.Equal(expected, Updater.ParseChecksum(text));

        [Fact]
        public void ReadsUpdateArguments()
        {
            Assert.Null(Updater.ParseApplyArguments(["--background"], out _));
            Assert.Equal(1234, Updater.ParseApplyArguments([Updater.ApplyArgument, "1234", Updater.RestartArgument], out var restart));
            Assert.True(restart);
            Assert.Equal(0, Updater.ParseApplyArguments([Updater.ApplyArgument], out restart));
            Assert.False(restart);
        }

        [Fact]
        public async Task ChecksGitHubWithAUserAgent()
        {
            var handler = new FakeGitHub { ReleaseJson = Release() };
            var updater = new Updater(new HttpClient(handler), Repo, new Version(2, 1, 0), _dir);

            var update = await updater.CheckAsync(CancellationToken.None);

            Assert.Equal(new Version(2, 2, 0), update?.Version);
            Assert.Equal($"https://api.github.com/repos/{Repo}/releases/latest", handler.Requests[0].Url);
            Assert.StartsWith("InstaShare/2.1.0", handler.Requests[0].UserAgent);
        }

        [Fact]
        public async Task NoReleasesMeansNoUpdate()
        {
            var updater = new Updater(new HttpClient(new FakeGitHub()), Repo, new Version(2, 1, 0), _dir);
            Assert.Null(await updater.CheckAsync(CancellationToken.None));
        }

        [Fact]
        public async Task DownloadsAndVerifiesTheExe()
        {
            var exe = Encoding.UTF8.GetBytes("pretend this is InstaShare 2.2.0");
            var handler = new FakeGitHub { Exe = exe, Checksum = $"{Hash(exe)}  InstaShare.exe\n" };
            var updater = new Updater(new HttpClient(handler), Repo, new Version(2, 1, 0), _dir);
            var update = Updater.ParseRelease(Release(), Repo, updater.Current)!;

            var path = await updater.DownloadAsync(update, null, CancellationToken.None);

            Assert.Equal(exe, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(_dir, "*.part"));

            // A second call reuses the verified file instead of downloading again.
            var downloads = handler.Requests.Count(r => r.Url.EndsWith(".exe"));
            await updater.DownloadAsync(update, null, CancellationToken.None);
            Assert.Equal(downloads, handler.Requests.Count(r => r.Url.EndsWith(".exe")));
        }

        [Fact]
        public async Task RejectsADownloadThatDoesntMatchItsChecksum()
        {
            var handler = new FakeGitHub { Exe = [1, 2, 3], Checksum = Hash([4, 5, 6]) };
            var updater = new Updater(new HttpClient(handler), Repo, new Version(2, 1, 0), _dir);
            var update = Updater.ParseRelease(Release(), Repo, updater.Current)!;

            await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(update, null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(_dir));
        }

        private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

        private sealed class FakeGitHub : HttpMessageHandler
        {
            public string? ReleaseJson { get; init; }
            public byte[] Exe { get; init; } = [];
            public string Checksum { get; init; } = "";
            public List<(string Url, string UserAgent)> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri!.ToString();
                Requests.Add((url, request.Headers.UserAgent.ToString()));
                HttpResponseMessage response = url switch
                {
                    _ when url.EndsWith("/releases/latest") => ReleaseJson == null
                        ? new(HttpStatusCode.NotFound)
                        : new(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson) },
                    _ when url.EndsWith(".sha256") => new(HttpStatusCode.OK) { Content = new StringContent(Checksum) },
                    _ when url.EndsWith(".exe") => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Exe) },
                    _ => new(HttpStatusCode.NotFound),
                };
                return Task.FromResult(response);
            }
        }
    }
}
