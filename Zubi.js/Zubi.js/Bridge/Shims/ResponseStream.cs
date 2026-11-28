namespace Zubi.js.Bridge.Shims
{
    internal sealed class ResponseStream : Stream
    {
        private readonly HttpListenerResponse _owner;
        public ResponseStream(HttpListenerResponse owner) { _owner = owner; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _owner.FlushSync();
        public override Task FlushAsync(CancellationToken ct) => _owner.FlushAsync();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => _owner.WriteBodySync(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => _owner.WriteBodyAsync(buffer, offset, count, ct);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            => _owner.WriteBodyAsync(buffer, ct);
    }
}