using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Net;

namespace RichardSzalay.MockHttp.WebSockets.Internal;

/// <summary>
/// Handles the upgrade to a duplex stream when returned as HttpResponseMessage.Content
/// </summary>
internal class MockWebSocketContent : HttpContent
{
    private readonly Func<WebSocket, Task> onAccept;

    public MockWebSocketContent(Func<WebSocket, Task> onAccept)
    {
        this.onAccept = onAccept;
    }

    private static Tuple<DuplexStream, WebSocket> CreateWebSocketPair(string? subProtocol)
    {
        var requestPipe = new Pipe();
        var responsePipe = new Pipe();

        var clientStream = new DuplexStream(responsePipe.Reader, requestPipe.Writer);
        var serverStream = new DuplexStream(requestPipe.Reader, responsePipe.Writer);

        return Tuple.Create(
            clientStream,
            WebSocket.CreateFromStream(serverStream, new WebSocketCreationOptions
            {
                IsServer = true,
                SubProtocol = subProtocol
            })
        );
    }

    /// <remarks>
    /// WebSocket (technically the internal WebSocketHandle) relies on the HttpContent stream
    /// a) Being duplex (read/write)
    /// b) Staying open until the WebSocket is closed
    /// 
    /// The only way to achieve this is by overriding the entire Stream.
    /// </remarks>
    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        var (clientStream, serverWebSocket) = CreateWebSocketPair(null);

        Task.Run(async () =>
        {
            await onAccept(serverWebSocket);

            // This is necessary because otherwise the server WebSocket (and its underlying
            // write stream) is disposed, causing the client's CloseAsync call to trigger a 1-second
            // timeout.
            await clientStream.WaitDisposeAsync();
        }, cancellationToken);

        return clientStream;
    }

    // Unused, but required as the method is abstract
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new NotSupportedException("Not expected to be called, since we are intercepting CreateContentReadStream");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}