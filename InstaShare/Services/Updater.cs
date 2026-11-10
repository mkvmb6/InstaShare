using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace InstaShare.Services
{
    internal sealed record UpdateInfo(Version Version, string ExeUrl, string ChecksumUrl, string PageUrl, long Size);

    /// <summary>
    /// Finds new versions in the repo's GitHub releases, downloads the exe and checks it against the
    /// release's .sha256 file. Installing is done by the new exe itself; see <see cref="ApplyArgument"/>.
    /// </summary>
    internal sealed class Updater
    {
        /// <summary>
        /// Passed to a downloaded exe: wait for the running copy to exit, install over it, start it again.
        /// </summary>
        public const string ApplyArgument = "--apply-update";

        /// <summary>
        /// Passed to the installed copy after an update, so it can say so.
        /// </summary>
        public const string UpdatedArgument = "--updated";

        public const string ExeAsset = $"{Constants.AppName}.exe";
        public const string ChecksumAsset = $"{ExeAsset}.sha256";

        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;
        private readonly string _repo;
        private readonly string _downloadDirectory;

        public Updater(HttpClient http, string repo, Version current, string downloadDirectory)
        {
            _http = http;
            _repo = repo;
            _downloadDirectory = downloadDirectory;
            Current = Normalize(current);
        }

        public Version Current { get; }

        /// <summary>
        /// The newest release if it's newer than this copy, otherwise null.
        /// </summary>
        public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repo}/releases/latest");
            AddHeaders(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _http.SendAsync(request, timeout.Token);
            // No releases yet.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new HttpRequestException("GitHub is limiting requests from this network. Try again in an hour.");
            response.EnsureSuccessStatusCode();
            return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), _repo, Current);
        }

        /// <summary>
        /// Reads a GitHub "latest release" response. Null when it isn't newer or lacks the exe and its checksum.
        /// </summary>
        internal static UpdateInfo? ParseRelease(string json, string repo, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
                || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;
            version = Normalize(version);
            if (version <= Normalize(current))
                return null;

            // Only files attached to this repo's releases; never somewhere a tampered response points to.
            var allowed = $"https://github.com/{repo}/releases/download/";
            string? exeUrl = null, checksumUrl = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (url == null || !url.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(name, ExeAsset, StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = url;
                        size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
                    }
                    else if (string.Equals(name, ChecksumAsset, StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = url;
                    }
                }
            }
            if (exeUrl == null || checksumUrl == null)
                return null;

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            return new UpdateInfo(version, exeUrl, checksumUrl, page ?? $"https://github.com/{repo}/releases", size);
        }

        /// <summary>
        /// Downloads the new exe and returns its path. Throws if it doesn't match the published checksum.
        /// </summary>
        public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_downloadDirectory);
            var target = Path.Combine(_downloadDirectory, $"{Constants.AppName}-{update.Version.ToString(3)}.exe");

            string expected;
            using (var request = new HttpRequestMessage(HttpMethod.Get, update.ChecksumUrl))
            {
                AddHeaders(request);
                using var response = await _http.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                expected = ParseChecksum(await response.Content.ReadAsStringAsync(cancellationToken))
                    ?? throw new InvalidDataException("The release's checksum file is unreadable.");
            }

            // Downloaded earlier and still intact.
            if (File.Exists(target) && await HashFile(target, cancellationToken) == expected)
                return target;

            var partial = target + ".part";
            using (var request = new HttpRequestMessage(HttpMethod.Get, update.ExeUrl))
            {
                AddHeaders(request);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? update.Size;

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;
                    if (total > 0)
                        progress?.Report(Math.Min(1, (double)done / total));
                }
            }

            if (await HashFile(partial, cancellationToken) != expected)
            {
                File.Delete(partial);
                throw new InvalidDataException("The download was damaged (its checksum doesn't match). It will be tried again later.");
            }
            File.Move(partial, target, overwrite: true);
            return target;
        }

        /// <summary>
        /// The hash from a sha256sum-style file ("hex  name"), lower case, or null.
        /// </summary>
        internal static string? ParseChecksum(string text)
        {
            var first = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first != null && first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
        }

        /// <summary>
        /// Passed with <see cref="ApplyArgument"/> when the new version should start once installed.
        /// </summary>
        public const string RestartArgument = "--restart";

        /// <summary>
        /// Starts the downloaded exe, which installs itself once this process has exited.
        /// </summary>
        public static void Launch(string exe, bool restart)
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            start.ArgumentList.Add(ApplyArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (restart)
                start.ArgumentList.Add(RestartArgument);
            Process.Start(start);
        }

        /// <summary>
        /// The process id after <see cref="ApplyArgument"/>, or null when this isn't an update launch.
        /// </summary>
        internal static int? ParseApplyArguments(string[] args, out bool restart)
        {
            restart = args.Contains(RestartArgument);
            var i = Array.IndexOf(args, ApplyArgument);
            if (i < 0)
                return null;
            return i + 1 < args.Length && int.TryParse(args[i + 1], out var pid) ? pid : 0;
        }

        /// <summary>
        /// Removes downloads that have been installed (or abandoned). Files still in use are left for next time.
        /// </summary>
        public void CleanUp()
        {
            try
            {
                if (!Directory.Exists(_downloadDirectory))
                    return;
                foreach (var file in Directory.EnumerateFiles(_downloadDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception)
                    {
                        // The update process may still be finishing.
                    }
                }
            }
            catch (Exception)
            {
                // Not worth bothering anyone about.
            }
        }

        /// <summary>
        /// 2.1.0 and 2.1.0.0 are the same version.
        /// </summary>
        internal static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

        private static async Task<string> HashFile(string path, CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        private void AddHeaders(HttpRequestMessage request) =>
            // GitHub rejects requests without a User-Agent.
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(Constants.AppName, Current.ToString(3)));
    }
}
