using System.IO;

namespace InstaShare.FileManagers.Impl
{
    /// <summary>
    /// Read-only view over the next <paramref name="length"/> bytes of <paramref name="inner"/> that reports bytes read to a tracker.
    /// </summary>
    internal sealed class ProgressStream(Stream inner, long length, TransferProgress? tracker = null) : Stream
    {
        private long _position;

        public override int Read(byte[] buffer, int offset, int count) =>
            Advance(inner.Read(buffer, offset, Limit(count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Advance(await inner.ReadAsync(buffer[..Limit(buffer.Length)], cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <summary>
        /// Takes back the bytes this stream reported, so a retried request doesn't count them twice.
        /// </summary>
        public void RollbackProgress()
        {
            tracker?.Add(-_position);
            _position = 0;
        }

        private int Limit(int count) => (int)Math.Min(count, length - _position);

        private int Advance(int bytesRead)
        {
            _position += bytesRead;
            tracker?.Add(bytesRead);
            return bytesRead;
        }

        // Required overrides
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
