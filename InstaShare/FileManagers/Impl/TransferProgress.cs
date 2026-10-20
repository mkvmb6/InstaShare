using System.Diagnostics;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// Thread-safe byte counter shared by every stream of one upload. Reports at most once per
    /// <see cref="ReportIntervalMs"/>, so the upload never waits on the UI.
    /// </summary>
    /// <remarks>
    /// The progress bar moves on every report, but speed is averaged over <see cref="SpeedWindowMs"/> and the
    /// speed and ETA only change once per <see cref="SpeedRefreshMs"/>. HttpClient reads the file in bursts,
    /// so anything shorter makes the numbers jump around.
    /// </remarks>
    internal sealed class TransferProgress
    {
        private const long ReportIntervalMs = 100;
        private const long SpeedRefreshMs = 500;
        private const long SpeedWindowMs = 5000;
        private const long FirstSpeedAfterMs = 500;

        private readonly long _totalBytes;
        private readonly Action<double, string?, long, TimeSpan?> _report;
        private readonly Func<long> _nowMs;
        private readonly object _lock = new();
        // Recent (time, bytes sent) points; speed is the slope from the oldest to now.
        private readonly Queue<(long ms, long sent)> _samples = new();
        private readonly long _startMs;
        private long _transferred;
        private long _sent;
        private long _lastReportMs;
        private long _lastSpeedMs;
        private double? _bytesPerSecond;
        private TimeSpan? _eta;

        /// <param name="report">Receives percent, speed text, bytes transferred and ETA. Speed and ETA are null until measured.</param>
        /// <param name="nowMs">Clock in milliseconds; tests pass a fake one.</param>
        public TransferProgress(long totalBytes, Action<double, string?, long, TimeSpan?> report, Func<long>? nowMs = null)
        {
            _totalBytes = totalBytes;
            _report = report;
            if (nowMs == null)
            {
                var clock = Stopwatch.StartNew();
                nowMs = () => clock.ElapsedMilliseconds;
            }
            _nowMs = nowMs;
            _startMs = _lastReportMs = _lastSpeedMs = nowMs();
            _samples.Enqueue((_startMs, 0));
        }

        public void Add(long bytes)
        {
            Interlocked.Add(ref _transferred, bytes);
            // A retry rolls its bytes back out of the progress, but they did cross the network, so speed keeps them.
            if (bytes > 0)
                Interlocked.Add(ref _sent, bytes);
            if (_nowMs() - Volatile.Read(ref _lastReportMs) >= ReportIntervalMs)
                Report(force: false);
        }

        public void ReportNow() => Report(force: true);

        private void Report(bool force)
        {
            double percent;
            long transferred;
            string? speedText;
            TimeSpan? eta;
            lock (_lock)
            {
                var nowMs = _nowMs();
                if (!force && nowMs - _lastReportMs < ReportIntervalMs)
                    return;
                Volatile.Write(ref _lastReportMs, nowMs);

                transferred = Interlocked.Read(ref _transferred);
                var sent = Interlocked.Read(ref _sent);
                Sample(nowMs, sent);

                var due = _bytesPerSecond == null ? nowMs - _startMs >= FirstSpeedAfterMs : nowMs - _lastSpeedMs >= SpeedRefreshMs;
                if (due)
                {
                    var (oldestMs, oldestSent) = _samples.Peek();
                    _bytesPerSecond = (sent - oldestSent) * 1000.0 / Math.Max(1, nowMs - oldestMs);
                    _eta = EstimateRemaining(_totalBytes - transferred, _bytesPerSecond.Value);
                    _lastSpeedMs = nowMs;
                }

                percent = _totalBytes > 0 ? Math.Clamp(transferred * 100.0 / _totalBytes, 0, 100) : 100;
                speedText = _bytesPerSecond is { } speed ? FormatSpeed(speed) : null;
                eta = transferred >= _totalBytes ? null : _eta;
            }
            _report(percent, speedText, transferred, eta);
        }

        private void Sample(long nowMs, long sent)
        {
            // Forced reports can come in quick succession (small files in a folder); one point per interval is plenty.
            if (nowMs - LastSampleMs() >= ReportIntervalMs)
                _samples.Enqueue((nowMs, sent));
            // Keep the oldest point just past the window, so the average always spans the full window.
            while (_samples.Count > 2 && nowMs - _samples.ElementAt(1).ms >= SpeedWindowMs)
                _samples.Dequeue();
        }

        private long LastSampleMs() => _samples.Count == 0 ? long.MinValue : _samples.Last().ms;

        private static TimeSpan? EstimateRemaining(long remainingBytes, double bytesPerSecond)
        {
            if (remainingBytes <= 0 || bytesPerSecond <= 0)
                return null;
            var seconds = remainingBytes / bytesPerSecond;
            // A stalled transfer gives an absurd estimate; show nothing rather than "300 days left".
            return seconds < TimeSpan.FromDays(1).TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;
        }

        internal static string FormatSpeed(double bytesPerSecond)
        {
            var kbps = bytesPerSecond / 1024.0;
            // Switch units on the rounded value, so 1023.97 KB/s shows as "1.0 MB/s" rather than "1024 KB/s".
            if (Math.Round(kbps) >= 1024)
                return $"{kbps / 1024:0.0} MB/s";
            // Whole numbers above 100 KB/s; the decimal only adds flicker there.
            return kbps >= 100 ? $"{kbps:0} KB/s" : $"{kbps:0.0} KB/s";
        }
    }
}
