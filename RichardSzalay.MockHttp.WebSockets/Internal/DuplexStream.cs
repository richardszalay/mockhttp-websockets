using System.Buffers;
using System.IO.Pipelines;

namespace RichardSzalay.MockHttp.WebSockets.Internal;

/// <summary>
/// A stream that makes use of separate channels for reading/writing
/// </summary>
internal class DuplexStream : Stream
{
    private readonly Stream reader;
    private readonly Stream writer;

    public DuplexStream(PipeReader reader, PipeWriter writer)
    {
        this.reader = reader.AsStream();
        this.writer = writer.AsStream();
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;

    public override int Read(Span<byte> buffer)
    {
        if (disposed) return 0;
        return reader.Read(buffer);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (disposed) return 0;
        return await reader.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (disposed) return 0;
        return await reader.ReadAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (disposed) return 0;
        return reader.Read(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        AssertNotDisposed();
        await writer.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        AssertNotDisposed();
        await writer.WriteAsync(buffer, cancellationToken);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        AssertNotDisposed();
        writer.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        AssertNotDisposed();
        writer.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        AssertNotDisposed();
        writer.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        AssertNotDisposed();
        await writer.FlushAsync(cancellationToken);
    }

    // Stream features that we don't support
    public override bool CanSeek => false;
    public override long Length => throw new NotImplementedException();
    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    private void AssertNotDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(DuplexStream));
        }
    }

    private bool disposed = false;
    private TaskCompletionSource disposeTcs = new TaskCompletionSource();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposeTcs.SetResult();
            this.writer.Dispose();
            this.reader.Dispose();

            disposed = true;
            
            Disposing.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler Disposing;

    public async Task WaitDisposeAsync()
    {
        await disposeTcs.Task;
    }
}

