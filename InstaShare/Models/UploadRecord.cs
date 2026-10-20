using System.IO;

namespace InstaShare.Models
{
    /// <summary>
    /// One share in uploadedFiles.json. Records written before expiry choices existed only have the first four fields.
    /// Token proves this app created the share; records from before the InstaShare API have none, and the server
    /// cleans those up on its own.
    /// </summary>
    public class UploadRecord
    {
        public static readonly TimeSpan LegacyExpiry = TimeSpan.FromDays(2);

        public required string FileId { get; set; }
        public required string FilePath { get; set; }
        public required string ShareableLink { get; set; }
        public required DateTime UploadTimeUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public long? SizeBytes { get; set; }
        public bool IsFolder { get; set; }
        public ShareStatus Status { get; set; } = ShareStatus.Completed;
        public string? Token { get; set; }

        public DateTime EffectiveExpiresUtc => ExpiresUtc ?? UploadTimeUtc + LegacyExpiry;

        public string DisplayName
        {
            get
            {
                var name = Path.GetFileName(FilePath.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(name) ? FilePath : name;
            }
        }
    }

    public enum ShareStatus
    {
        Completed,
        Uploading,
        CompletedWithErrors,
        Failed,
        Cancelled,
    }
}
