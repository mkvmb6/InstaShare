namespace InstaShare.Models
{
    /// <summary>
    /// Snapshot of an in-flight upload. RemoteId and SharedLink are known before any bytes are sent.
    /// LinkLive turns true once the share's index is published, so the link opens instead of 404ing.
    /// Speed and Eta are null until a transfer rate has been measured.
    /// </summary>
    public record UploadProgress(string RemoteId, string SharedLink, string Status, double Percent, string? Speed, long BytesSent, long TotalBytes, TimeSpan? Eta, bool LinkLive = true);
}
