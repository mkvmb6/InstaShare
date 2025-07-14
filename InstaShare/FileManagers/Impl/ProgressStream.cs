using System.IO;

namespace InstaShare.FileManagers.Impl
{
    public class ProgressStream(Stream inner, long totalSize, string sharedLink, Action<double, string, string>? progressCallback = null) : Stream
    {
        private long _bytesRead = 0;
        private readonly List<(DateTime timestamp, long bytes)> _readHistory = [];
        private readonly object _lock = new();

        private double? _emaSpeedKBps = null;
        private const double Alpha = 0.01; // Smoothing factor for EMA, lower means more smooth

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesReadNow = inner.Read(buffer, offset, count);
            if (bytesReadNow > 0)
            {
                _bytesRead += bytesReadNow;

                double progress = (double)_bytesRead / totalSize * 100;


                lock (_lock)
                {
                    DateTime now = DateTime.UtcNow;
                    _readHistory.Add((now, bytesReadNow));

                    // Remove data older than 1 second
                    _readHistory.RemoveAll(x => (now - x.timestamp).TotalSeconds > 1);

                    // Calculate recent speed
                    double recentBytes = _readHistory.Sum(x => x.bytes);
                    double recentKBps = recentBytes / 1024.0;

                    // Apply EMA smoothing
                    if (_emaSpeedKBps == null)
                        _emaSpeedKBps = recentKBps; // initialize
                    else
                        _emaSpeedKBps = Alpha * recentKBps + (1 - Alpha) * _emaSpeedKBps.Value;
                }

                string speedText = _emaSpeedKBps >= 1024
                    ? $"{(_emaSpeedKBps.Value / 1024):0.0} MB/s"
                    : $"{_emaSpeedKBps.Value:0.0} KB/s";

                progressCallback?.Invoke(progress, speedText, sharedLink);
            }
            return bytesReadNow;
        }

        // Required overrides
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

}
