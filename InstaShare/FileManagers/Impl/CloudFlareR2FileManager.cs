using Amazon.S3;
using Amazon.S3.Model;
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
    public class CloudFlareR2FileManager : IFileManager
    {
        // Objects above this size go up in parts, so they can retry per part and exceed the 5 GB single PUT limit.
        private const long MultipartThreshold = 100L * 1024 * 1024;
        private const long MinPartSize = 16L * 1024 * 1024;
        private const int MaxParts = 10_000;
        private const int PartConcurrency = 4;
        private const int FileConcurrency = 4;
        private const int DeleteConcurrency = 8;
        private const int MaxAttempts = 4;
        private const long MinBytesPerSecond = 50_000; // Assuming min speed around 50KBps
        private static readonly TimeSpan MinRequestTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan IndexFlushInterval = TimeSpan.FromSeconds(1);

        // One client for the app's lifetime; per-request timeouts are applied through cancellation tokens.
        private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        private readonly IAmazonS3 _s3;
        public CloudFlareR2FileManager()
        {
            _s3 = R2ClientFactory.CreateR2Client();
        }

        public async Task DeleteShare(string remoteId, CancellationToken cancellationToken = default)
        {
            // A share is every object under its top-level folder: the files plus index.json.
            var prefix = ShareKeys.GetSharePrefix(remoteId);
            string? continuationToken = null;
            do
            {
                var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = Constants.R2BucketName,
                    Prefix = prefix,
                    ContinuationToken = continuationToken
                }, cancellationToken);

                var keys = page.S3Objects?.Select(o => o.Key).ToList() ?? [];
                await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = DeleteConcurrency, CancellationToken = cancellationToken },
                    async (key, ct) => await _s3.DeleteObjectAsync(Constants.R2BucketName, key, ct));

                continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
            } while (continuationToken != null);
        }

        public async Task<(string fileId, string sharedLink)> UploadFile(string filePath, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var fileName = Path.GetFileName(filePath);
            var folderId = $"{fileName}-{Guid.NewGuid()}";
            var fileId = $"{folderId}/{fileName}";
            var sharedLink = ShareKeys.GetShareLink(folderId);
            var totalSize = new FileInfo(filePath).Length;

            var entry = new FileIndex
            {
                path = fileName,
                url = ShareKeys.GetCdnUrl(fileId),
                size = totalSize,
                status = UploadStatus.Uploading
            };
            var index = new IndexPublisher([entry], (json, ct) => UploadIndexJson(json, folderId, ct));

            progress?.Report(new UploadProgress(fileId, sharedLink, "Generating link...", 0, null, 0, totalSize, null, LinkLive: false));
            await index.FlushAsync(cancellationToken);
            progress?.Report(new UploadProgress(fileId, sharedLink, "Uploading", 0, null, 0, totalSize, null));

            var tracker = new TransferProgress(totalSize, (percent, speed, sent, eta) =>
                progress?.Report(new UploadProgress(fileId, sharedLink, "Uploading", percent, speed, sent, totalSize, eta)));
            try
            {
                await UploadObject(filePath, fileId, tracker, cancellationToken);
                index.SetStatus(entry, UploadStatus.Uploaded);
            }
            catch
            {
                index.SetStatus(entry, UploadStatus.Failed);
                await TryCompleteIndex(index);
                throw;
            }

            await index.CompleteAsync();
            return (fileId, sharedLink);
        }

        public async Task<(string folderId, string sharedLink, IReadOnlyList<string> failedFiles)> UploadFolderWithStructure(string localRootPath, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(localRootPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {localRootPath}");
            }
            var uniqueFolderId = Guid.NewGuid().ToString();
            var files = Directory.GetFiles(localRootPath, "*", SearchOption.AllDirectories);
            var folderName = Path.GetFileName(localRootPath);
            var baseFolder = $"{uniqueFolderId}/{folderName}";
            var sharedLink = ShareKeys.GetShareLink(baseFolder);

            var entries = GetIndex(files, folderName, baseFolder, localRootPath);
            var index = new IndexPublisher(entries, (json, ct) => UploadIndexJson(json, uniqueFolderId, ct));

            var totalSize = entries.Sum(e => e.size);
            progress?.Report(new UploadProgress(baseFolder, sharedLink, "Generating link...", 0, null, 0, totalSize, null, LinkLive: false));
            await index.FlushAsync(cancellationToken);
            index.StartPeriodicFlush(IndexFlushInterval);
            progress?.Report(new UploadProgress(baseFolder, sharedLink, $"0/{files.Length} files", 0, null, 0, totalSize, null));

            var failedFiles = await UploadFolderFiles(localRootPath, baseFolder, sharedLink, [.. files.Zip(entries)], totalSize, index, progress, cancellationToken);
            await index.CompleteAsync();
            return (baseFolder, sharedLink, failedFiles);
        }

        public async Task<IReadOnlyList<string>> RetryFolderFiles(string folderId, string localRootPath, IReadOnlyList<string> relativePaths, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var uniqueFolderId = ShareKeys.GetSharePrefix(folderId).TrimEnd('/');
            var folderName = folderId[(uniqueFolderId.Length + 1)..];
            var sharedLink = ShareKeys.GetShareLink(folderId);

            // The published index is the source of truth for every other file's status.
            List<FileIndex> entries;
            using (var response = await _s3.GetObjectAsync(Constants.R2BucketName, $"{uniqueFolderId}/index.json", cancellationToken))
            using (var reader = new StreamReader(response.ResponseStream))
                entries = JsonConvert.DeserializeObject<List<FileIndex>>(await reader.ReadToEndAsync(cancellationToken)) ?? [];

            var byPath = entries.ToDictionary(e => e.path);
            var work = new List<(string file, FileIndex entry)>();
            foreach (var relativePath in relativePaths)
            {
                if (!byPath.TryGetValue($"{folderName}/{relativePath}", out var entry))
                    continue;
                var file = Path.Combine(localRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file))
                    entry.size = new FileInfo(file).Length;
                entry.status = UploadStatus.Uploading;
                work.Add((file, entry));
            }

            var index = new IndexPublisher(entries, (json, ct) => UploadIndexJson(json, uniqueFolderId, ct));
            var totalSize = work.Sum(w => w.entry.size);
            progress?.Report(new UploadProgress(folderId, sharedLink, "Preparing retry...", 0, null, 0, totalSize, null));
            await index.FlushAsync(cancellationToken);
            index.StartPeriodicFlush(IndexFlushInterval);

            var stillFailed = await UploadFolderFiles(localRootPath, folderId, sharedLink, work, totalSize, index, progress, cancellationToken);
            await index.CompleteAsync();
            return stillFailed;
        }

        /// <summary>
        /// Uploads files of a folder share in parallel. A file that fails is marked failed and returned, the rest carry on.
        /// </summary>
        private async Task<IReadOnlyList<string>> UploadFolderFiles(string localRootPath, string baseFolder, string sharedLink, IReadOnlyList<(string file, FileIndex entry)> work, long totalSize, IndexPublisher index, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
        {
            var failedFiles = new ConcurrentBag<string>();
            var completedFiles = 0;
            var tracker = new TransferProgress(totalSize, (percent, speed, sent, eta) =>
                progress?.Report(new UploadProgress(baseFolder, sharedLink,
                    $"{Volatile.Read(ref completedFiles)}/{work.Count} files", percent, speed, sent, totalSize, eta)));

            try
            {
                await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (item, ct) =>
                {
                    var (file, entry) = item;
                    var relativePath = Path.GetRelativePath(localRootPath, file).Replace("\\", "/");
                    try
                    {
                        await UploadObject(file, $"{baseFolder}/{relativePath}", tracker, ct);
                        index.SetStatus(entry, UploadStatus.Uploaded);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // One bad file (locked, deleted, network gave up) shouldn't sink the whole share.
                        index.SetStatus(entry, UploadStatus.Failed);
                        failedFiles.Add(relativePath);
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

        private static List<FileIndex> GetIndex(string[] files, string folderName, string baseFolder, string localRootPath)
        {
            var index = new List<FileIndex>(files.Length);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(localRootPath, file).Replace("\\", "/");
                var fileId = $"{baseFolder}/{relativePath}";
                index.Add(new FileIndex
                {
                    path = $"{folderName}/{relativePath}",
                    size = new FileInfo(file).Length,
                    url = ShareKeys.GetCdnUrl(fileId),
                    status = UploadStatus.Uploading
                });
            }
            return index;
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

        private async Task UploadIndexJson(string indexJson, string baseFolder, CancellationToken cancellationToken)
        {
            var indexKey = $"{baseFolder}/index.json";
            var body = Encoding.UTF8.GetBytes(indexJson);
            using var response = await SendWithRetry(() =>
            {
                var content = new ByteArrayContent(body);
                AddObjectHeaders(content, "application/json", null);
                return (_s3.GetPreSignedUrl(indexKey, "application/json"), content, null);
            }, body.Length, cancellationToken);
        }

        private async Task UploadObject(string filePath, string key, TransferProgress tracker, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(filePath);
            var contentType = ContentTypes.FromFileName(fileName);
            // PDFs stay inline so the viewer's iframe can render them; everything else downloads under its real name.
            var contentDisposition = contentType == "application/pdf" ? null : ContentTypes.AttachmentDisposition(fileName);
            var size = new FileInfo(filePath).Length;

            if (size > MultipartThreshold)
            {
                await UploadMultipart(filePath, key, size, contentType, contentDisposition, tracker, cancellationToken);
                return;
            }

            using var response = await SendWithRetry(() =>
            {
                var stream = new ProgressStream(OpenAt(filePath, 0), size, tracker);
                var content = new StreamContent(stream);
                content.Headers.ContentLength = size; // presigned PUTs don't accept chunked bodies
                AddObjectHeaders(content, contentType, contentDisposition);
                return (_s3.GetPreSignedUrl(key, contentType, contentDisposition), content, stream);
            }, size, cancellationToken);
        }

        private async Task UploadMultipart(string filePath, string key, long size, string contentType, string? contentDisposition, TransferProgress tracker, CancellationToken cancellationToken)
        {
            var partSize = Math.Max(MinPartSize, (size + MaxParts - 1) / MaxParts);
            var partCount = (int)((size + partSize - 1) / partSize);

            var initiate = new InitiateMultipartUploadRequest
            {
                BucketName = Constants.R2BucketName,
                Key = key,
                ContentType = contentType,
                CannedACL = S3CannedACL.PublicRead,
            };
            if (contentDisposition != null)
                initiate.Headers.ContentDisposition = contentDisposition;
            var uploadId = (await _s3.InitiateMultipartUploadAsync(initiate, cancellationToken)).UploadId;

            try
            {
                var etags = new PartETag[partCount];
                await Parallel.ForEachAsync(Enumerable.Range(1, partCount), new ParallelOptions { MaxDegreeOfParallelism = PartConcurrency, CancellationToken = cancellationToken }, async (partNumber, ct) =>
                {
                    var offset = (partNumber - 1) * partSize;
                    var length = Math.Min(partSize, size - offset);
                    using var response = await SendWithRetry(() =>
                    {
                        var stream = new ProgressStream(OpenAt(filePath, offset), length, tracker);
                        var content = new StreamContent(stream);
                        content.Headers.ContentLength = length;
                        return (_s3.GetPreSignedPartUrl(key, uploadId, partNumber), content, stream);
                    }, length, ct);
                    etags[partNumber - 1] = new PartETag(partNumber, response.Headers.ETag?.Tag
                        ?? throw new InvalidOperationException($"No ETag returned for part {partNumber}."));
                });

                await _s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
                {
                    BucketName = Constants.R2BucketName,
                    Key = key,
                    UploadId = uploadId,
                    PartETags = [.. etags],
                }, cancellationToken);
            }
            catch
            {
                try
                {
                    await _s3.AbortMultipartUploadAsync(Constants.R2BucketName, key, uploadId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Orphaned parts get cleaned up by the bucket's multipart abort rule.
                }
                throw;
            }
        }

        /// <summary>
        /// Sends a PUT, building a fresh request per attempt and retrying transient failures with backoff.
        /// </summary>
        private static async Task<HttpResponseMessage> SendWithRetry(Func<(string url, HttpContent content, ProgressStream? stream)> createRequest, long size, CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(MinRequestTimeout.TotalSeconds, size / MinBytesPerSecond));
            for (var attempt = 1; ; attempt++)
            {
                var (url, content, stream) = createRequest();
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
                    if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                        throw error;
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

        private static void AddObjectHeaders(HttpContent content, string contentType, string? contentDisposition)
        {
            // Values are part of the presigned signature, so send them exactly as signed.
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            content.Headers.TryAddWithoutValidation("x-amz-acl", "public-read");
            if (contentDisposition != null)
                content.Headers.TryAddWithoutValidation("Content-Disposition", contentDisposition);
        }

        private static FileStream OpenAt(string filePath, long offset)
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            stream.Seek(offset, SeekOrigin.Begin);
            return stream;
        }
    }
}
