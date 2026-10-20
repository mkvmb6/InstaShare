using InstaShare.Models;
using InstaShare.Services;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// Uploads to Cloudflare R2 through the InstaShare API. The API holds the storage keys and signs each upload;
    /// the bytes go straight from here to R2.
    /// </summary>
    public class CloudFlareR2FileManager : IFileManager
    {
        // Objects above this size go up in parts, so they can retry per part and exceed the 5 GB single PUT limit.
        private const long MultipartThreshold = 100L * 1024 * 1024;
        private const long MinPartSize = 16L * 1024 * 1024;
        private const int MaxParts = 10_000;
        private const int PartConcurrency = 4;
        private const int FileConcurrency = 4;
        private const int MaxAttempts = 4;
        // Upload links are signed this many at a time, so a big folder costs a handful of API calls, not one per file.
        private const int SignBatchSize = 100;
        private const int SignConcurrency = 4;
        private const long MinBytesPerSecond = 50_000; // Assuming min speed around 50KBps
        private static readonly TimeSpan MinRequestTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan IndexFlushInterval = TimeSpan.FromSeconds(1);
        // Inside the share, so it's deleted with it; the dot keeps it apart from shared files and folders.
        private const string PreviewKey = ".instashare/preview.jpg";

        // One client for the app's lifetime; per-request timeouts are applied through cancellation tokens.
        private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private readonly ShareApi _api = new(Http, Constants.ApiBaseUrl);

        private delegate Task<(string url, HttpContent content, ProgressStream? stream)> RequestFactory(bool freshUrl);

        public Task DeleteShare(string remoteId, string token, CancellationToken cancellationToken = default) =>
            _api.DeleteShare(new ShareRef(ShareKeys.GetSharePrefix(remoteId), token), cancellationToken);

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var fileName = Path.GetFileName(filePath);
            var totalSize = new FileInfo(filePath).Length;
            var (share, expiresUtc) = await _api.CreateShare(isFolder: false, fileName, expiry, cancellationToken);
            var fileId = share.Prefix + fileName;
            var sharedLink = ShareKeys.GetShareLink(share.Prefix.TrimEnd('/'));

            var entry = new FileIndex
            {
                path = fileName,
                url = ShareKeys.GetCdnUrl(fileId),
                size = totalSize,
                status = UploadStatus.Uploading
            };
            var index = new IndexPublisher(new ShareIndexDocument { name = fileName, kind = "file", expiresUtc = expiresUtc, files = [entry] }, IndexWriter(share));

            var report = new UploadProgress(fileId, sharedLink, "Uploading", 0, null, 0, totalSize, null, true, share.Token, expiresUtc);
            progress?.Report(report with { Status = "Generating link...", LinkLive = false });
            await index.FlushAsync(cancellationToken);
            progress?.Report(report);
            var preview = PublishPreview(share, PreviewRenderer.PickCandidates([(filePath, fileName)]), index, cancellationToken);

            var tracker = new TransferProgress(totalSize, (percent, speed, sent, eta) =>
                progress?.Report(report with { Percent = percent, Speed = speed, BytesSent = sent, Eta = eta }));
            try
            {
                await UploadObject(share, filePath, PutFor(filePath, fileId, totalSize), presignedUrl: null, tracker, cancellationToken);
                index.SetStatus(entry, UploadStatus.Uploaded);
            }
            catch
            {
                index.SetStatus(entry, UploadStatus.Failed);
                await TryCompleteIndex(index);
                throw;
            }

            await preview;
            await index.CompleteAsync();
            return (fileId, sharedLink);
        }

        public Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadFolderWithStructure(string localRootPath, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(localRootPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {localRootPath}");
            }
            // A drive root like "D:\" has no name of its own.
            var folderName = Path.GetFileName(localRootPath.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : "Folder";
            var files = Directory.GetFiles(localRootPath, "*", SearchOption.AllDirectories)
                .Select(file => (file, Path.GetRelativePath(localRootPath, file).Replace("\\", "/")))
                .ToList();
            return UploadAsFolderShare(folderName, files, expiry, progress, cancellationToken);
        }

        public Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadItems(IReadOnlyList<string> paths, string shareName, TimeSpan expiry, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            UploadAsFolderShare(SafeFolderName(shareName), ShareLayout.Arrange(paths), expiry, progress, cancellationToken);

        /// <summary>
        /// Uploads files as one folder share named <paramref name="folderName"/>, each at its relative path inside it.
        /// </summary>
        private async Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadAsFolderShare(string folderName, IReadOnlyList<(string localPath, string relativePath)> files, TimeSpan expiry, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
        {
            var (share, expiresUtc) = await _api.CreateShare(isFolder: true, null, expiry, cancellationToken);
            var baseFolder = share.Prefix + folderName;
            var sharedLink = ShareKeys.GetShareLink(baseFolder);

            var work = files.Select(f =>
            {
                var key = $"{baseFolder}/{f.relativePath}";
                var size = new FileInfo(f.localPath).Length;
                var entry = new FileIndex
                {
                    path = $"{folderName}/{f.relativePath}",
                    size = size,
                    url = ShareKeys.GetCdnUrl(key),
                    status = UploadStatus.Uploading
                };
                return new FolderFile(f.localPath, f.relativePath, entry, PutFor(f.localPath, key, size));
            }).ToList();
            var index = new IndexPublisher(new ShareIndexDocument { name = folderName, kind = "folder", expiresUtc = expiresUtc, files = [.. work.Select(w => w.Entry)] }, IndexWriter(share));

            var report = new UploadProgress(baseFolder, sharedLink, $"0/{work.Count} files", 0, null, 0, work.Sum(w => w.Entry.size), null, true, share.Token, expiresUtc);
            progress?.Report(report with { Status = "Generating link...", LinkLive = false });
            await index.FlushAsync(cancellationToken);
            index.StartPeriodicFlush(IndexFlushInterval);
            progress?.Report(report);
            var preview = PublishPreview(share, PreviewRenderer.PickCandidates(work.Select(w => (w.LocalPath, w.Entry.path))), index, cancellationToken);

            var failedFiles = await UploadFolderFiles(share, work, index, report, progress, cancellationToken);
            await preview;
            await index.CompleteAsync();
            return (baseFolder, sharedLink, failedFiles);
        }

        /// <summary>
        /// A share's top-level folder name: no slashes or characters that would break keys, and never empty.
        /// </summary>
        private static string SafeFolderName(string name)
        {
            var cleaned = new string(name.Select(c => c is '/' or '\\' || char.IsControl(c) ? ' ' : c).ToArray()).Trim().Trim('.');
            return cleaned.Length == 0 ? "Shared files" : cleaned.Length > 120 ? cleaned[..120].TrimEnd() : cleaned;
        }

        public async Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string token, IReadOnlyDictionary<string, string> localPaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var share = new ShareRef(ShareKeys.GetSharePrefix(folderId), token);
            var folderName = folderId[share.Prefix.Length..];
            var sharedLink = ShareKeys.GetShareLink(folderId);

            // The published index is the source of truth for every other file's status.
            // The query string skips any copy the CDN still has cached.
            var indexUrl = $"{ShareKeys.GetCdnUrl(share.Prefix + "index.json")}?v={DateTime.UtcNow.Ticks}";
            var document = ShareIndexDocument.Parse(await Http.GetStringAsync(indexUrl, cancellationToken));
            var entries = document.files;

            var byPath = entries.ToDictionary(e => e.path);
            var work = new List<FolderFile>();
            foreach (var (relativePath, file) in localPaths)
            {
                if (!byPath.TryGetValue($"{folderName}/{relativePath}", out var entry))
                    continue;
                if (File.Exists(file))
                    entry.size = new FileInfo(file).Length;
                entry.status = UploadStatus.Uploading;
                work.Add(new FolderFile(file, relativePath, entry, PutFor(file, $"{folderId}/{relativePath}", entry.size)));
            }

            var index = new IndexPublisher(document, IndexWriter(share));
            var report = new UploadProgress(folderId, sharedLink, "Preparing retry...", 0, null, 0, work.Sum(w => w.Entry.size), null, true, token, null);
            progress?.Report(report);
            await index.FlushAsync(cancellationToken);
            index.StartPeriodicFlush(IndexFlushInterval);

            var stillFailed = await UploadFolderFiles(share, work, index, report, progress, cancellationToken);
            await index.CompleteAsync();
            return stillFailed;
        }

        private sealed record FolderFile(string LocalPath, string RelativePath, FileIndex Entry, PutRequest Put);

        /// <summary>
        /// Uploads files of a folder share in parallel. A file that fails is marked failed and returned, the rest carry on.
        /// </summary>
        private async Task<IReadOnlyList<string>> UploadFolderFiles(ShareRef share, IReadOnlyList<FolderFile> work, IndexPublisher index, UploadProgress report, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
        {
            var failedFiles = new ConcurrentBag<string>();
            var completedFiles = 0;
            var tracker = new TransferProgress(report.TotalBytes, (percent, speed, sent, eta) =>
                progress?.Report(report with
                {
                    Status = $"{Volatile.Read(ref completedFiles)}/{work.Count} files",
                    Percent = percent,
                    Speed = speed,
                    BytesSent = sent,
                    Eta = eta,
                }));

            try
            {
                var urls = await SignSmallObjects(share, work.Select(w => w.Put), cancellationToken);
                await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (item, ct) =>
                {
                    try
                    {
                        await UploadObject(share, item.LocalPath, item.Put, urls.GetValueOrDefault(item.Put.Key), tracker, ct);
                        index.SetStatus(item.Entry, UploadStatus.Uploaded);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // One bad file (locked, deleted, too big, network gave up) shouldn't sink the whole share.
                        index.SetStatus(item.Entry, UploadStatus.Failed);
                        failedFiles.Add(item.RelativePath);
                    }
                    Interlocked.Increment(ref completedFiles);
                    tracker.ReportNow();
                });
            }
            catch
            {
                await TryCompleteIndex(index, UploadStatus.Failed);
                throw;
            }

            return [.. failedFiles];
        }

        /// <summary>
        /// Signs upload links for every file small enough for a single PUT, in batches.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, string>> SignSmallObjects(ShareRef share, IEnumerable<PutRequest> puts, CancellationToken cancellationToken)
        {
            var urls = new ConcurrentDictionary<string, string>();
            var batches = puts.Where(p => p.Size <= MultipartThreshold).Chunk(SignBatchSize);
            await Parallel.ForEachAsync(batches, new ParallelOptions { MaxDegreeOfParallelism = SignConcurrency, CancellationToken = cancellationToken }, async (batch, ct) =>
            {
                var signed = await _api.SignPuts(share, batch, ct);
                for (var i = 0; i < batch.Length; i++)
                    urls[batch[i].Key] = signed[i];
            });
            return urls;
        }

        /// <summary>
        /// Renders the chat-app preview from the local files, uploads it next to them and adds it to the index.
        /// Runs alongside the upload and never throws: a share without a preview still works.
        /// </summary>
        private async Task PublishPreview(ShareRef share, IReadOnlyList<PreviewCandidate> candidates, IndexPublisher index, CancellationToken cancellationToken)
        {
            try
            {
                var preview = await PreviewRenderer.RenderAsync(candidates, cancellationToken);
                if (preview == null)
                    return;

                var key = share.Prefix + PreviewKey;
                var put = new PutRequest(key, preview.Jpeg.Length, "image/jpeg", null);
                string? url = null;
                using var response = await SendWithRetry(async freshUrl =>
                {
                    if (freshUrl || url == null)
                        url = (await _api.SignPuts(share, [put], cancellationToken))[0];
                    var content = new ByteArrayContent(preview.Jpeg);
                    content.Headers.ContentLength = preview.Jpeg.Length;
                    AddObjectHeaders(content, put);
                    return (url, content, null);
                }, preview.Jpeg.Length, cancellationToken);

                index.SetPreview(new SharePreview { url = ShareKeys.GetCdnUrl(key), width = preview.Width, height = preview.Height });
                await index.FlushAsync(cancellationToken);
            }
            catch (Exception)
            {
                // Cancelled, offline, or nothing decodable; the link just shows the generic card.
            }
        }

        private static PutRequest PutFor(string filePath, string key, long size)
        {
            var fileName = Path.GetFileName(filePath);
            var contentType = ContentTypes.FromFileName(fileName);
            // PDFs stay inline so the viewer's iframe can render them; everything else downloads under its real name.
            var contentDisposition = contentType == "application/pdf" ? null : ContentTypes.AttachmentDisposition(fileName);
            return new PutRequest(key, size, contentType, contentDisposition);
        }

        /// <summary>
        /// Best-effort final index write after a failure, so the viewer doesn't show "uploading" forever.
        /// </summary>
        private static async Task TryCompleteIndex(IndexPublisher index, string? pendingStatus = null)
        {
            try
            {
                await index.CompleteAsync(pendingStatus);
            }
            catch (Exception)
            {
                // The original error matters more than the index write.
            }
        }

        /// <summary>
        /// Writes index.json through one reusable signed link, fetching a new one if it expires during a long upload.
        /// IndexPublisher serializes writes, so the cached link needs no lock.
        /// </summary>
        private Func<string, CancellationToken, Task> IndexWriter(ShareRef share)
        {
            string? url = null;
            return async (json, cancellationToken) =>
            {
                var body = Encoding.UTF8.GetBytes(json);
                using var response = await SendWithRetry(async freshUrl =>
                {
                    if (freshUrl || url == null)
                        url = await _api.SignIndex(share, cancellationToken);
                    var content = new ByteArrayContent(body);
                    content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                    return (url, content, null);
                }, body.Length, cancellationToken);
            };
        }

        private async Task UploadObject(ShareRef share, string filePath, PutRequest put, string? presignedUrl, TransferProgress tracker, CancellationToken cancellationToken)
        {
            if (put.Size > MultipartThreshold)
            {
                await UploadMultipart(share, filePath, put, tracker, cancellationToken);
                return;
            }

            using var response = await SendWithRetry(async freshUrl =>
            {
                if (freshUrl || presignedUrl == null)
                    presignedUrl = (await _api.SignPuts(share, [put], cancellationToken))[0];
                var stream = new ProgressStream(OpenAt(filePath, 0), put.Size, tracker);
                var content = new StreamContent(stream);
                // The link only accepts the size it was signed for, so send exactly that, not chunked.
                content.Headers.ContentLength = put.Size;
                AddObjectHeaders(content, put);
                return (presignedUrl, content, stream);
            }, put.Size, cancellationToken);
        }

        private async Task UploadMultipart(ShareRef share, string filePath, PutRequest put, TransferProgress tracker, CancellationToken cancellationToken)
        {
            var size = put.Size;
            var partSize = Math.Max(MinPartSize, (size + MaxParts - 1) / MaxParts);
            var partCount = (int)((size + partSize - 1) / partSize);

            var uploadId = await _api.CreateMultipart(share, put, cancellationToken);
            var partUrls = new PartUrls(_api, share, put.Key, uploadId, size, partSize);
            try
            {
                var etags = new string[partCount];
                await Parallel.ForEachAsync(Enumerable.Range(1, partCount), new ParallelOptions { MaxDegreeOfParallelism = PartConcurrency, CancellationToken = cancellationToken }, async (partNumber, ct) =>
                {
                    var offset = (partNumber - 1) * partSize;
                    var length = Math.Min(partSize, size - offset);
                    using var response = await SendWithRetry(async freshUrl =>
                    {
                        var url = await partUrls.Get(partNumber, freshUrl, ct);
                        var stream = new ProgressStream(OpenAt(filePath, offset), length, tracker);
                        var content = new StreamContent(stream);
                        content.Headers.ContentLength = length;
                        return (url, content, stream);
                    }, length, ct);
                    etags[partNumber - 1] = response.Headers.ETag?.Tag
                        ?? throw new InvalidOperationException($"No ETag returned for part {partNumber}.");
                });

                await _api.CompleteMultipart(share, put.Key, uploadId, [.. etags.Select((etag, i) => (i + 1, etag))], cancellationToken);
            }
            catch
            {
                try
                {
                    await _api.AbortMultipart(share, put.Key, uploadId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Orphaned parts get cleaned up by the bucket's multipart abort rule.
                }
                throw;
            }
        }

        /// <summary>
        /// Part upload links, signed a batch at a time as the upload reaches them.
        /// </summary>
        private sealed class PartUrls(ShareApi api, ShareRef share, string key, string uploadId, long size, long partSize)
        {
            private readonly ConcurrentDictionary<int, string> _urls = new();
            private readonly SemaphoreSlim _lock = new(1, 1);
            private readonly int _partCount = (int)((size + partSize - 1) / partSize);

            public async Task<string> Get(int partNumber, bool fresh, CancellationToken cancellationToken)
            {
                if (!fresh && _urls.TryGetValue(partNumber, out var url))
                    return url;

                await _lock.WaitAsync(cancellationToken);
                try
                {
                    if (!fresh && _urls.TryGetValue(partNumber, out url))
                        return url;
                    // This part plus the next ones not signed yet; parts are uploaded roughly in order.
                    var batch = Enumerable.Range(partNumber, Math.Min(SignBatchSize, _partCount - partNumber + 1))
                        .Where(n => n == partNumber || !_urls.ContainsKey(n))
                        .Select(n => (n, Math.Min(partSize, size - (n - 1) * partSize)))
                        .ToList();
                    var signed = await api.SignParts(share, key, uploadId, batch, cancellationToken);
                    for (var i = 0; i < batch.Count; i++)
                        _urls[batch[i].n] = signed[i];
                    return _urls[partNumber];
                }
                finally
                {
                    _lock.Release();
                }
            }
        }

        /// <summary>
        /// Sends a PUT, building a fresh request per attempt and retrying transient failures with backoff.
        /// </summary>
        private static async Task<HttpResponseMessage> SendWithRetry(RequestFactory createRequest, long size, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(MinRequestTimeout.TotalSeconds, size / MinBytesPerSecond));
            var freshUrl = false;
            for (var attempt = 1; ; attempt++)
            {
                var (url, content, stream) = await createRequest(freshUrl);
                var retryNow = false;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
                    var response = await Http.SendAsync(request, timeoutCts.Token);
                    if (response.IsSuccessStatusCode)
                        return response;

                    var error = new HttpRequestException(
                        $"Upload failed with {(int)response.StatusCode} {response.ReasonPhrase}: {await ReadErrorBody(response)}",
                        null, response.StatusCode);
                    response.Dispose();
                    if (response.StatusCode == HttpStatusCode.Forbidden && !freshUrl && attempt < MaxAttempts)
                    {
                        // Upload links expire after a few hours, and a long queue can outlive one. Get a new link once.
                        freshUrl = retryNow = true;
                    }
                    else if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                    {
                        throw error;
                    }
                }
                catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested
                    && ex is HttpRequestException { StatusCode: null } or OperationCanceledException or IOException)
                {
                    // Network error or our own timeout; retry below.
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Upload timed out after {timeout.TotalMinutes:0} minutes.");
                }
                finally
                {
                    content.Dispose();
                }

                stream?.RollbackProgress();
                if (!retryNow)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }

        private static bool IsTransient(HttpStatusCode status) =>
            status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

        private static async Task<string> ReadErrorBody(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                return body.Length > 500 ? body[..500] : body;
            }
            catch (Exception)
            {
                return "(no response body)";
            }
        }

        private static void AddObjectHeaders(HttpContent content, PutRequest put)
        {
            // Values are part of the presigned signature, so send them exactly as signed.
            content.Headers.TryAddWithoutValidation("Content-Type", put.ContentType);
            if (put.ContentDisposition != null)
                content.Headers.TryAddWithoutValidation("Content-Disposition", put.ContentDisposition);
        }

        private static FileStream OpenAt(string filePath, long offset)
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            stream.Seek(offset, SeekOrigin.Begin);
            return stream;
        }
    }
}
