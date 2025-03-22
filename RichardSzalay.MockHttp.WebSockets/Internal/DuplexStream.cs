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
        return reader.Read(buffer);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        
        return await reader.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return await reader.ReadAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return reader.Read(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await writer.WriteAsync(buffer, cancellationToken);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        writer.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        writer.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        writer.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
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
    
    private TaskCompletionSource disposeTcs = new TaskCompletionSource();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposeTcs.SetResult();
            this.writer.Dispose();
        }
    }

    public async Task WaitDisposeAsync()
    {
        await disposeTcs.Task;
    }
}

