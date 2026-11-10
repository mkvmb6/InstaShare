using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// A share as the API knows it: its top-level folder and the token that proves we created it.
    /// </summary>
    internal sealed record ShareRef(string Prefix, string Token);

    internal sealed record PutRequest(string Key, long Size, string ContentType, string? ContentDisposition);

    internal sealed class ShareApiException(HttpStatusCode? status, string message) : Exception(message)
    {
        public HttpStatusCode? Status { get; } = status;
    }

    /// <summary>
    /// Client for the InstaShare API Worker. The Worker holds the storage keys and hands out presigned URLs;
    /// file bytes go straight to storage and never through it.
    /// </summary>
    /// <param name="apiKey">Read before every request, so a key pasted into Settings applies at once.</param>
    internal sealed class ShareApi(HttpClient http, string baseUrl, Func<string?> apiKey)
    {
        private const int MaxAttempts = 4;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        private static readonly JsonSerializerSettings Json = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public async Task<(ShareRef share, DateTime expiresUtc)> CreateShare(bool isFolder, string? name, TimeSpan expiry, CancellationToken cancellationToken)
        {
            var response = await Post<CreatedShare>("/v1/shares", new { kind = isFolder ? "folder" : "file", name, expiresInHours = expiry.TotalHours }, cancellationToken);
            return (new ShareRef(response.Prefix, response.Token), response.ExpiresUtc);
        }

        public async Task<IReadOnlyList<string>> SignPuts(ShareRef share, IReadOnlyList<PutRequest> objects, CancellationToken cancellationToken)
        {
            var response = await Post<SignedUrls>("/v1/sign", new
            {
                prefix = share.Prefix,
                token = share.Token,
                objects = objects.Select(o => new { key = o.Key, size = o.Size, contentType = o.ContentType, contentDisposition = o.ContentDisposition }),
            }, cancellationToken);
            return CheckCount(response.Urls, objects.Count);
        }

        /// <summary>
        /// A link for writing the share's index.json, reused for every update during the upload.
        /// </summary>
        public async Task<string> SignIndex(ShareRef share, CancellationToken cancellationToken) =>
            (await Post<IndexUrl>("/v1/index", new { prefix = share.Prefix, token = share.Token }, cancellationToken)).Url;

        public async Task<string> CreateMultipart(ShareRef share, PutRequest put, CancellationToken cancellationToken)
        {
            var response = await Post<Multipart>("/v1/multipart/create", new
            {
                prefix = share.Prefix,
                token = share.Token,
                key = put.Key,
                size = put.Size,
                contentType = put.ContentType,
                contentDisposition = put.ContentDisposition,
            }, cancellationToken);
            return response.UploadId;
        }

        public async Task<IReadOnlyList<string>> SignParts(ShareRef share, string key, string uploadId, IReadOnlyList<(int partNumber, long size)> parts, CancellationToken cancellationToken)
        {
            var response = await Post<SignedUrls>("/v1/multipart/sign", new
            {
                prefix = share.Prefix,
                token = share.Token,
                key,
                uploadId,
                parts = parts.Select(p => new { partNumber = p.partNumber, size = p.size }),
            }, cancellationToken);
            return CheckCount(response.Urls, parts.Count);
        }

        public Task CompleteMultipart(ShareRef share, string key, string uploadId, IReadOnlyList<(int partNumber, string etag)> parts, CancellationToken cancellationToken) =>
            Post<object>("/v1/multipart/complete", new
            {
                prefix = share.Prefix,
                token = share.Token,
                key,
                uploadId,
                parts = parts.Select(p => new { partNumber = p.partNumber, etag = p.etag }),
            }, cancellationToken);

        public Task AbortMultipart(ShareRef share, string key, string uploadId, CancellationToken cancellationToken) =>
            Post<object>("/v1/multipart/abort", new { prefix = share.Prefix, token = share.Token, key, uploadId }, cancellationToken);

        public Task DeleteShare(ShareRef share, CancellationToken cancellationToken) =>
            Post<object>("/v1/shares/delete", new { prefix = share.Prefix, token = share.Token }, cancellationToken);

        private async Task<T> Post<T>(string path, object body, CancellationToken cancellationToken)
        {
            var payload = JsonConvert.SerializeObject(body, Json);
            for (var attempt = 1; ; attempt++)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);
                try
                {
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path) { Content = content };
                    if (apiKey() is { Length: > 0 } key)
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                    using var response = await http.SendAsync(request, timeout.Token);
                    var text = await response.Content.ReadAsStringAsync(timeout.Token);
                    if (response.IsSuccessStatusCode)
                        return JsonConvert.DeserializeObject<T>(text, Json) ?? throw new ShareApiException(response.StatusCode, "Empty response from the server.");

                    var error = new ShareApiException(response.StatusCode, ReadError(text) ?? $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                    if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                        throw error;
                }
                catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested
                    && ex is HttpRequestException or OperationCanceledException or IOException)
                {
                    // Network error or timeout; retry below.
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("The InstaShare server didn't respond in time.");
                }
                catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ShareApiException(null, $"Couldn't reach the InstaShare server: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        private static bool IsTransient(HttpStatusCode status) =>
            status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

        private static string? ReadError(string text)
        {
            try
            {
                return JsonConvert.DeserializeObject<ErrorBody>(text)?.Error;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static IReadOnlyList<string> CheckCount(IReadOnlyList<string>? urls, int expected) =>
            urls?.Count == expected ? urls : throw new ShareApiException(null, "The server returned the wrong number of upload links.");

        private sealed record CreatedShare(string Prefix, string Token, DateTime ExpiresUtc);
        private sealed record SignedUrls(IReadOnlyList<string> Urls);
        private sealed record Multipart(string UploadId);
        private sealed record IndexUrl(string Url);
        private sealed record ErrorBody(string? Error);
    }
}
