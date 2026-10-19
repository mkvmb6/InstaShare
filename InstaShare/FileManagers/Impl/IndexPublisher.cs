using InstaShare.Models;
using Newtonsoft.Json;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// Owns an upload's index.json. Status changes are batched, and writes are serialized so an
    /// older snapshot can never overwrite a newer one.
    /// </summary>
    internal sealed class IndexPublisher(IReadOnlyList<FileIndex> entries, Func<string, CancellationToken, Task> upload)
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private bool _dirty = true;
        private CancellationTokenSource? _loopStop;
        private Task? _loop;

        public void SetStatus(FileIndex entry, string status)
        {
            lock (_lock)
            {
                entry.status = status;
                _dirty = true;
            }
        }

        /// <summary>
        /// Writes the index if anything changed since the last successful write.
        /// </summary>
        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                string json;
                lock (_lock)
                {
                    if (!_dirty)
                        return;
                    json = JsonConvert.SerializeObject(entries);
                    _dirty = false;
                }

                try
                {
                    await upload(json, cancellationToken);
                }
                catch
                {
                    lock (_lock) _dirty = true;
                    throw;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void StartPeriodicFlush(TimeSpan interval)
        {
            _loopStop = new CancellationTokenSource();
            _loop = RunPeriodicFlush(interval, _loopStop.Token);
        }

        /// <summary>
        /// Stops periodic flushing, optionally marks entries still uploading with <paramref name="pendingStatus"/>, and writes the final index.
        /// </summary>
        public async Task CompleteAsync(string? pendingStatus = null)
        {
            if (_loopStop != null)
            {
                _loopStop.Cancel();
                await _loop!;
                _loopStop.Dispose();
                _loopStop = null;
            }

            if (pendingStatus != null)
            {
                lock (_lock)
                {
                    foreach (var entry in entries.Where(e => e.status == UploadStatus.Uploading))
                    {
                        entry.status = pendingStatus;
                        _dirty = true;
                    }
                }
            }

            await FlushAsync(CancellationToken.None);
        }

        private async Task RunPeriodicFlush(TimeSpan interval, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    try
                    {
                        await FlushAsync(cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Still dirty, so the next tick retries.
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
