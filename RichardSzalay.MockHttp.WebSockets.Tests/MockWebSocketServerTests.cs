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
}
