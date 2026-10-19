using System.Diagnostics;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// Thread-safe byte counter shared by every stream of one upload. Reports at most once per
    /// <see cref="ReportIntervalMs"/>, so the upload never waits on the UI.
    /// </summary>
    internal sealed class TransferProgress(long totalBytes, Action<double, string> report)
    {
        private const long ReportIntervalMs = 100;
        private const double Alpha = 0.3; // Smoothing factor for EMA per report, lower means more smooth

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new();
        private long _transferred;
        private long _lastReportMs;
        private long _lastReportBytes;
        private double? _emaBytesPerSecond;

        public void Add(long bytes)
        {
            Interlocked.Add(ref _transferred, bytes);
            if (_clock.ElapsedMilliseconds - Volatile.Read(ref _lastReportMs) >= ReportIntervalMs)
                Report(force: false);
        }

        public void ReportNow() => Report(force: true);

        private void Report(bool force)
        {
            double percent;
            string speedText;
            lock (_lock)
            {
                var nowMs = _clock.ElapsedMilliseconds;
                var elapsedMs = nowMs - _lastReportMs;
                if (!force && elapsedMs < ReportIntervalMs)
                    return;

                var transferred = Interlocked.Read(ref _transferred);
                // Forced reports can come a few ms apart; only sample speed over a full interval.
                if (elapsedMs >= ReportIntervalMs)
                {
                    var instant = Math.Max(0, transferred - _lastReportBytes) / (elapsedMs / 1000.0);
                    _emaBytesPerSecond = _emaBytesPerSecond is null ? instant : Alpha * instant + (1 - Alpha) * _emaBytesPerSecond.Value;
                    _lastReportBytes = transferred;
                    Volatile.Write(ref _lastReportMs, nowMs);
                }

                percent = totalBytes > 0 ? Math.Clamp(transferred * 100.0 / totalBytes, 0, 100) : 100;
                speedText = FormatSpeed(_emaBytesPerSecond ?? 0);
            }
            report(percent, speedText);
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            var kbps = bytesPerSecond / 1024.0;
            return kbps >= 1024 ? $"{kbps / 1024:0.0} MB/s" : $"{kbps:0.0} KB/s";
        }
    }
}
