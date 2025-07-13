namespace InstaShare.Models
{
    internal class FileIndex
    {
        public required string url { get; set; }
        public required string path { get; set; }
        public long size { get; set; }
    }
}
