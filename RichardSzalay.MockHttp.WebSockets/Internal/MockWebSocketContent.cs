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

        var serverWebSocket = WebSocket.CreateFromStream(serverStream, new WebSocketCreationOptions
        {
            IsServer = true,
            SubProtocol = subProtocol
        });

        // This is needed for client-sourced Aborts to work
        clientStream.Disposing += (sender, args) => serverStream.Dispose();
        serverStream.Disposing += (sender, args) =>
        {
            // If the client starts the Close handshake and the server response with a Close,
            // it (the server) will immediately dispose the WebSocket _before_ the client has
            // finished reading the close message from the server.
            // 
            // (This workaround can likely be avoided by having DuplexStream use Pipe{Reader|Writer} directly
            // and allowing any buffered read data to be read before an ObjectDisposedException is thrown)
            if (serverWebSocket.State != WebSocketState.Closed)
            {
                clientStream.Dispose();
            }
        };

        return Tuple.Create(clientStream, serverWebSocket);
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