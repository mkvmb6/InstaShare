namespace InstaShare.Models
{
    internal class FileIndex
    {
        public required string url { get; set; }
        public required string path { get; set; }
        public long size { get; set; }
        public required string status { get; set; }
    }

    /// <summary>
    /// The index.json the viewer reads. Shares from before desktop 2.0 published a bare array of files instead,
    /// and the viewer still accepts that.
    /// </summary>
    internal class ShareIndexDocument
    {
        public string? name { get; set; }
        public string? kind { get; set; }
        public DateTime? expiresUtc { get; set; }
        /// <summary>
        /// The picture chat apps show for the link. Added once it's uploaded, so it can be missing.
        /// </summary>
        public SharePreview? preview { get; set; }
        public required IReadOnlyList<FileIndex> files { get; set; }

        /// <summary>
        /// Reads either format, so a retry of an older share still works.
        /// </summary>
        public static ShareIndexDocument Parse(string json)
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
            if (token is Newtonsoft.Json.Linq.JArray array)
                return new ShareIndexDocument { files = array.ToObject<List<FileIndex>>() ?? [] };
            return token.ToObject<ShareIndexDocument>() ?? throw new FormatException("index.json is empty.");
        }
    }

    internal class SharePreview
    {
        public required string url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    internal class UploadStatus
    {
        public const string Uploading = "uploading";
        public const string Uploaded = "uploaded";
        public const string Failed = "failed";
    }
}
