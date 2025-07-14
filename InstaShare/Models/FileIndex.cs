namespace InstaShare.Models
{
    internal class FileIndex
    {
        public required string url { get; set; }
        public required string path { get; set; }
        public long size { get; set; }
        public required string status { get; set; }
    }

    internal class UploadStatus
    {
        public const string Uploading = "uploading";
        public const string Uploaded = "uploaded";
    }
}
