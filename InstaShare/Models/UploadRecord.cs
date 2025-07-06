namespace InstaShare.Models
{
    public class UploadRecord
    {
        public required string FileId { get; set; }
        public required string FilePath { get; set; }
        public required string ShareableLink { get; set; }
        public required DateTime UploadTimeUtc { get; set; }
    }
}
