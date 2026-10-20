namespace InstaShare.Models
{
    /// <summary>
    /// Snapshot of an in-flight upload. RemoteId, SharedLink and Token are known before any bytes are sent.
    /// LinkLive turns true once the share's index is published, so the link opens instead of 404ing.
    /// Speed and Eta are null until a transfer rate has been measured.
    /// Token proves we created the share; it's needed to delete it or retry its files.
    /// ExpiresUtc is when the server deletes the share, or null on a retry of an existing share.
    /// </summary>
    public record UploadProgress(string RemoteId, string SharedLink, string Status, double Percent, string? Speed, long BytesSent, long TotalBytes, TimeSpan? Eta, bool LinkLive, string Token, DateTime? ExpiresUtc);
}
