using System.Net;
using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets.Tests;

/// <summary>
/// Tests related to MockWebSocketServer's behaviour as an <see cref="HttpMessageHandler"/>
/// </summary>
public class MockWebSocketServerTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    
    [Fact]
    public async Task Returns_426_for_non_WebSocket_requests()
    {
        var server = MockWebSocketServer.ForEndpoint(async (WebSocket ws, CancellationToken ct) =>
        {
        });

        var messageInvoker = server.ToMessageInvoker();

        var response = await messageInvoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/"), cancellationToken);
        
        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task WaitForFinalStateAsync_returns_after_sockets_are_all_closed_or_aborted()
    {
        var server = MockWebSocketServer.ForEndpoint(async (WebSocket ws, CancellationToken ct) =>
        {
            if (ws.State != WebSocketState.Aborted)
            {
                while (!ws.CloseStatus.HasValue)
                {
                    await ws.ReceiveAsync(Array.Empty<byte>(), ct);
                }

                await ws.CloseAsync(ws.CloseStatus.Value, ws.CloseStatusDescription, ct);
            }
        });

        var client1 = new ClientWebSocket();
        var client2 = new ClientWebSocket();
        
        await client1.ConnectAsync(new Uri("ws://localhost/"), server.ToMessageInvoker(), cancellationToken);
        await client2.ConnectAsync(new Uri("ws://localhost/"), server.ToMessageInvoker(), cancellationToken);

        await client1.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        client2.Abort();
        
        var finalStates = await server.WaitForFinalStatesAsync(cancellationToken);
        
        Assert.Equal([WebSocketState.Closed, WebSocketState.Aborted], finalStates.Order());
        
        Assert.Equal(WebSocketState.Closed, client1.State);
        Assert.Equal(WebSocketState.Aborted, client2.State);
    }
}
