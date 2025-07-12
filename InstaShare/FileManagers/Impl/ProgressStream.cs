using System.Diagnostics;
using System.IO;

namespace InstaShare.FileManagers.Impl
{
    public class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<double, string, string> _progressCallback;
        private readonly long _totalSize;
        private long _bytesRead = 0;
        private string _sharedLink;
        private readonly List<(DateTime timestamp, long bytes)> _readHistory = new();
        private readonly object _lock = new();

        private double? _emaSpeedKBps = null;
        private const double Alpha = 0.01; // Smoothing factor for EMA, lower means more smooth

        public ProgressStream(Stream inner, long totalSize, string sharedLink, Action<double, string, string> progressCallback)
        {
            _inner = inner;
            _totalSize = totalSize;
            _progressCallback = progressCallback;
            _sharedLink = sharedLink;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesReadNow = _inner.Read(buffer, offset, count);
            if (bytesReadNow > 0)
            {
                _bytesRead += bytesReadNow;

                double progress = (double)_bytesRead / _totalSize * 100;


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

                //double seconds = _stopwatch.Elapsed.TotalSeconds;
                //string speedText;
                //if (seconds > 0)
                //{
                //    double speedKBps = _bytesRead / 1024.0 / seconds;
                //    if (speedKBps >= 1024)
                //    {
                //        speedText = $"{(speedKBps / 1024):0.0} MB/s";
                //    }
                //    else
                //    {
                //        speedText = $"{speedKBps:0.0} KB/s";
                //    }
                //}
                //else
                //{
                //    speedText = "Calculating...";
                //}

                _progressCallback?.Invoke(progress, speedText, _sharedLink);
            }
            return bytesReadNow;
        }

        // Required overrides
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

}
