namespace InstaShare.Models
{
    /// <summary>
    /// Snapshot of an in-flight upload. RemoteId and SharedLink are known before any bytes are sent.
    /// </summary>
    public record UploadProgress(string RemoteId, string SharedLink, string Status, double Percent, string? Speed);
}
