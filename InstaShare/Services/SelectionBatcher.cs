namespace InstaShare.Services
{
    /// <summary>
    /// Gathers paths that arrive one launch at a time (Explorer starts a separate InstaShare for each selected
    /// item) and hands them over together once they stop coming.
    /// </summary>
    internal sealed class SelectionBatcher : IDisposable
    {
        private readonly TimeSpan _quietTime;
        private readonly TimeSpan _maxWait;
        private readonly Action<IReadOnlyList<string>> _flush;
        private readonly Func<DateTime> _now;
        private readonly object _lock = new();
        private readonly List<string> _paths = [];
        private readonly Timer _timer;
        private DateTime _firstUtc;
        private bool _disposed;

        /// <param name="quietTime">How long without a new path before the batch is complete.</param>
        /// <param name="maxWait">The longest a batch waits after its first path, however many keep arriving.</param>
        /// <param name="flush">Receives each batch, on a thread-pool thread.</param>
        /// <param name="now">Clock; tests pass a fake one.</param>
        public SelectionBatcher(TimeSpan quietTime, TimeSpan maxWait, Action<IReadOnlyList<string>> flush, Func<DateTime>? now = null)
        {
            _quietTime = quietTime;
            _maxWait = maxWait;
            _flush = flush;
            _now = now ?? (() => DateTime.UtcNow);
            _timer = new Timer(_ => Flush());
        }

        public void Add(IEnumerable<string> paths)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                var now = _now();
                if (_paths.Count == 0)
                    _firstUtc = now;
                _paths.AddRange(paths);

                // Restart the quiet period, but never past the batch's deadline.
                var untilDeadline = _maxWait - (now - _firstUtc);
                var due = untilDeadline < _quietTime ? untilDeadline : _quietTime;
                _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Hands over whatever is waiting now. Called by the timer, and by tests.
        /// </summary>
        internal void Flush()
        {
            List<string> batch;
            lock (_lock)
            {
                if (_paths.Count == 0)
                    return;
                batch = [.. _paths];
                _paths.Clear();
            }
            _flush(batch);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _timer.Dispose();
            }
        }
    }
}
