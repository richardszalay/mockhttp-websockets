using System.Diagnostics;
using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets.Tests;

/// <remarks>
/// ClientWebSocket.CloseAsync will wait for the server to fully close
/// for 1 second before continuing. Since this library aims to provide
/// ultra-fast tests, we want to avoid that 1 second timeout.
/// </remarks>
public class WebSocketCloseTimeoutTests
{
    [Fact]
    public async ValueTask Starting_close_from_client_does_not_trigger_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var mockServer = new MockWebSocketServer();
        mockServer.AddEndpoint("/", async (ws, ct) =>
        {
            while (!ws.CloseStatus.HasValue)
            {
                await ws.ReceiveAsync(Array.Empty<byte>(), ct);
            }

            await ws.CloseAsync(ws.CloseStatus.Value, ws.CloseStatusDescription, ct);
        });

         var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
        Assert.True(stopwatch.Elapsed.TotalSeconds < 1, "ClientWebSocket.CloseAsync internal timeout triggered unexpectedly");
    }

    [Fact]
    public async ValueTask Starting_close_from_server_does_not_trigger_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var mockServer = new MockWebSocketServer();
        mockServer.AddEndpoint("/", async (ws, ct) =>
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", ct);
        });

        var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("wss://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        
        var receiveResult = await client.ReceiveAsync(Array.Empty<byte>(), cancellationToken);

        Assert.Equal(WebSocketMessageType.Close, receiveResult.MessageType);
        
        await client.CloseAsync(client.CloseStatus!.Value, client.CloseStatusDescription, cancellationToken);
        Assert.True(stopwatch.Elapsed.TotalSeconds < 1, "ClientWebSocket.CloseAsync internal timeout triggered unexpectedly");
    }
}