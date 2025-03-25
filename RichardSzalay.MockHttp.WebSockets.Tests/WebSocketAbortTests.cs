using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace RichardSzalay.MockHttp.WebSockets.Tests;

public class WebSocketAbortTests
{
    [Fact]
    public async ValueTask Reflects_server_aborted_WebSocket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var mockServer = new MockWebSocketServer();
        mockServer.AddEndpoint("/", async (ws, ct) =>
        {
            // Enough to wait for the message to start arriving but leaves data in the buffer
            await ws.ReceiveAsync(Array.Empty<byte>(), ct);
            ws.Abort();
        });

         var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

        await client.SendAsync(new byte[] { 0x1, 0x2, 0x3 }, WebSocketMessageType.Binary, true, cancellationToken);
        
        var ex = await Assert.ThrowsAsync<WebSocketException>(async () =>
            await client.ReceiveAsync(Array.Empty<byte>(), cancellationToken));
        
        Assert.Equal(WebSocketError.ConnectionClosedPrematurely, ex.WebSocketErrorCode);
        Assert.Equal(WebSocketState.Aborted, client.State);
    }
    
    /// <remarks>
    /// This test needs a little TaskCompletionSource-finessing to control _when_ the abort signal is transmitted
    /// so that we can assert it properly, but the important thing that it fails in the expected way.
    /// </remarks>
    [Fact]
    public async ValueTask Reflects_client_aborted_WebSocket()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        
        TaskCompletionSource<Func<Task>> serverCompletionSource = new();
        TaskCompletionSource clientCompletionSource = new();

        using var mockServer = new MockWebSocketServer();
        mockServer.AddEndpoint("/", async (ws, ct) =>
        {
            // Ths will be called by the client so we can assert the result within the ExecutionContext of the test
            serverCompletionSource.SetResult(async () =>
            {
                var ex = await Assert.ThrowsAsync<WebSocketException>(async () =>
                    await ws.ReceiveAsync(Array.Empty<byte>(), ct));
        
                Assert.Equal(WebSocketError.ConnectionClosedPrematurely, ex.WebSocketErrorCode);
                Assert.Equal(WebSocketState.Aborted, ws.State);
            });

            // Wait for the client to finish
            await clientCompletionSource.Task;
        });

        var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

        await client.SendAsync(new byte[] { 0x1, 0x2, 0x3 }, WebSocketMessageType.Binary, true, cancellationToken);
        client.Abort();

        try
        {
            // Assert from the server
            await (await serverCompletionSource.Task)();
        }
        finally
        {
            // Let the server finish up
            clientCompletionSource.SetResult();
        }
    }
}