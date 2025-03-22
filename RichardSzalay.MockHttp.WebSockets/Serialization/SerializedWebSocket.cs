using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets.Serialization;

/// <summary>
/// Base class for all WebSocket readers/writers
/// </summary>
public abstract class SerializedWebSocket
{
    private static readonly PipeOptions PipeOptions;

    static SerializedWebSocket()
    {
        PipeOptions = new PipeOptions(
            pauseWriterThreshold: 0 // We don't want large packets blocking the writer
        );
    }

    private readonly Pipe incomingPipe = new(PipeOptions);
    private readonly Pipe outgoingPipe = new(PipeOptions);
    private readonly SemaphoreSlim writeSemaphore = new(1);

    public WebSocket RawWebSocket { get; private init; }

    protected SerializedWebSocket(WebSocket rawWebSocket)
    {
        RawWebSocket = rawWebSocket;
    }

    /// <summary>
    /// Expect to receive a message that can be deserialized as <typeparamref name="TDeserializeAs"/>, but
    /// assert that it is actually the subclass <typeparamref name="TExpected"/>. Polymorphic deserialization
    /// is the responsibility of implementor configuration.
    ///
    /// An exception will be thrown if the deserialized type is not <paramref name="TExpected"/> or if a close message is received.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TExpected">The subclass of <typeparamref name="TDeserializeAs"/> that is expected</typeparam>
    /// <typeparam name="TDeserializeAs">The base class the message will be deserialized as</typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Thrown if the deserialized type is not <typeparamref name="TExpected"/> or a close message</exception>
    public async ValueTask<TExpected> ExpectAsync<TExpected, TDeserializeAs>(CancellationToken cancellationToken = default)
        where TExpected : TDeserializeAs
    {
        var result = await TryReceiveAsync<TDeserializeAs>(cancellationToken);

        if (result.TryGetMessage(out var message))
        {
            if (message is TExpected expectedResult)
            {
                return expectedResult;
            }

            throw new InvalidOperationException(
                $"Expected message of type {typeof(TExpected).Name}, but received {result.GetType().Name}");
        }

        throw new InvalidOperationException(
            $"Expected message of type {typeof(TExpected).Name}, but received close message");
    }

    /// <summary>
    /// Expect to receive a text/binary message, rather than a close message, and deserialize it as <typeparamref name="TDeserializeAs"/>. An
    /// exception will be thrown if a close message is received instead.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TDeserializeAs"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async ValueTask<TDeserializeAs> ExpectAsync<TDeserializeAs>(CancellationToken cancellationToken = default)
    {
        var result = await TryReceiveAsync<TDeserializeAs>(cancellationToken);
        if (result.TryGetMessage(out var message))
        {
            return message;
        }
        throw new InvalidOperationException("Expected text/binary message, but received close message");
    }

    /// <summary>
    /// Expect to receive a close message, or throw an exception if another message type is received.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Thrown if a non-close message type is received</exception>
    public async ValueTask<WebSocketReceiveResult> ExpectCloseAsync(CancellationToken cancellationToken = default)
    {
        var result = await RawWebSocket.ReceiveAsync(Array.Empty<byte>(), cancellationToken);

        if (result.MessageType != WebSocketMessageType.Close)
        {
            throw new InvalidOperationException($"Expected Close message, but was {result.MessageType}");
        }

        return result;
    }

    /// <summary>
    /// Try to receive a message deserialized as <typeparamref name="TDeserializeAs"/>, or a subclass of it. If a close message is received,
    /// <see cref="WebSocketReadResult{T}.TryGetMessage"/> on the returned value will return false.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="TDeserializeAs"></typeparam>
    /// <returns></returns>
    public async ValueTask<WebSocketReadResult<TDeserializeAs>> TryReceiveAsync<TDeserializeAs>(CancellationToken cancellationToken = default)
    {
        var pipeWriter = incomingPipe.Writer;

        const int bufferSize = 4096;

        WebSocketMessageType messageType = WebSocketMessageType.Text;

        while (true)
        {
            var memory = pipeWriter.GetMemory(bufferSize);

            var receiveResult = await RawWebSocket.ReceiveAsync(memory, cancellationToken);

            messageType = receiveResult.MessageType;

            pipeWriter.Advance(receiveResult.Count);

            // Large websocket messages can come in multiple 'frames'.
            // If we're not at the end of the message, we'll continue
            // to fill the pipeWriter until we receive an EOM
            if (receiveResult.EndOfMessage)
            {
                break;
            }
        }

        if (messageType == WebSocketMessageType.Close)
        {
            return WebSocketReadResult<TDeserializeAs>.Closed;
        }

        await pipeWriter.FlushAsync(cancellationToken);
        await pipeWriter.CompleteAsync();

        try
        {
            var deserialized = await DeserializeAsync<TDeserializeAs>(messageType, incomingPipe.Reader.AsStream());

            return new WebSocketReadResult<TDeserializeAs>(messageType, deserialized);
        }
        finally
        {
            await incomingPipe.Reader.CompleteAsync();
            incomingPipe.Reset();
        }
    }

    protected abstract ValueTask<T> DeserializeAsync<T>(WebSocketMessageType messageType, Stream stream);

    /// <summary>
    /// Serializes a message and sends it over the WebSocket. The message may be sent across multiple frames if it cannot be serialized into
    /// the buffer made available by the internal pool.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="cancellationToken"></param>
    /// <typeparam name="T"></typeparam>
    public virtual async ValueTask SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        await writeSemaphore.WaitAsync(cancellationToken);

        var messageType = await SerializeAsync(message, outgoingPipe.Writer.AsStream(), cancellationToken);

        await outgoingPipe.Writer.FlushAsync(cancellationToken);

        await outgoingPipe.Writer.CompleteAsync();

        while (outgoingPipe.Reader.TryRead(out var readResult))
        {
            var buffer = readResult.Buffer;

            // PipeReaders use ReadOnlySequence<T>, which reuses buffers. If the buffers
            // are smaller than the message we'll need to send it across multiple WS messages
            while (!buffer.IsSingleSegment)
            {
                await RawWebSocket.SendAsync(buffer.First, messageType, false, cancellationToken).ConfigureAwait(false);
                buffer = buffer.Slice(buffer.First.Length);
            }

            await RawWebSocket.SendAsync(buffer.First, messageType, true, cancellationToken).ConfigureAwait(false);

            outgoingPipe.Reader.AdvanceTo(buffer.End);

            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await outgoingPipe.Reader.CompleteAsync();
        outgoingPipe.Reset();

        writeSemaphore.Release();
    }

    /// <summary>
    /// Serialize the provided value into <paramref name="stream"/>
    /// </summary>
    protected abstract ValueTask<WebSocketMessageType> SerializeAsync<T>(T value, Stream stream, CancellationToken cancellationToken);
}

/// <summary>
/// A read result from a WebSocket that may contain a deserialized message, if a close message was not received
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct WebSocketReadResult<T>(WebSocketMessageType messageType, T? message)
{
    /// <summary>
    /// Returns true if a close message was received from the WebSocket
    /// </summary>
    public bool IsClosed => MessageType == WebSocketMessageType.Close;

    /// <summary>
    /// The type of message reveived from the WebSocket
    /// </summary>
    public WebSocketMessageType MessageType { get; init; } = messageType;
    
    /// <summary>
    /// The deserialized message, or null if a close message was received.
    /// </summary>
    public T? Message { get; init; } = message;

    /// <summary>
    /// Attempts to get the message from the result. Returns false if read resulted in a close message
    /// </summary>
    /// <param name="message">When the WebSocket is not closed, the typed message</param>
    /// <returns></returns>
    public bool TryGetMessage([NotNullWhen(true)] out T? message)
    {
        if (IsClosed)
        {
            message = default;
            return false;
        }

        message = Message!;
        return true;
    }

    internal static WebSocketReadResult<T> Closed => new(WebSocketMessageType.Close, default);
}
