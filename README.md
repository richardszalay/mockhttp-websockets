Write blazing fast tests for ClientWebSocket with no abstractions. Real WebSockets, no network.

**This repository is in early development phase. There are no official releases yet.**

## Raw WebSocket Usage

```csharp
var cancellationToken = CancellationToken.None;

using var mockServer = MockWebSocketServer.ForEndpoint(async (ws, ct) =>
{
    // This is the 'server'. When it exits, the Websocket will shutdown.
    
    Memory<byte> buffer = new(new byte[4]);

    while (!ws.CloseStatus.HasValue)
    {
        var result = await ws.ReceiveAsync(buffer, ct);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            continue;
        }

        var messageValue = int.Parse(buffer.Span.Slice(0, result.Count));
        
        var sendBytes = Encoding.UTF8.GetBytes((messageValue + 1).ToString(), buffer.Span);

        await ws.SendAsync(buffer.Slice(0, sendBytes), WebSocketMessageType.Binary, false, ct);
    }

    if (ws.State == WebSocketState.CloseReceived)
    {
        await ws.CloseAsync(ws.CloseStatus.Value, ws.CloseStatusDescription, ct);
    }
});

// This needs to be passed into ClientWebSocket.ConnectAsync
var messageInvoker = mockServer.ToMessageInvoker();

// Everything below is the 'client', so probably in your application code not your test

var client = new ClientWebSocket();
await client.ConnectAsync(new Uri("ws://localhost"), messageInvoker, cancellationToken);

int messageValue = 1;
Memory<byte> buffer = new(new byte[4]);

for (var i = 0; i < 10; i++)
{
    var bufferLength = Encoding.UTF8.GetBytes(messageValue.ToString(), buffer.Span);

    await client.SendAsync(buffer.Slice(0, bufferLength), WebSocketMessageType.Text, false,
        cancellationToken);

    var receiveResult = await client.ReceiveAsync(buffer, cancellationToken);

    messageValue = int.Parse(buffer.Span.Slice(0, receiveResult.Count));
}

await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
```

## Client Mock Support

This library primarily focuses on mocking remote WebSocket servers in order to test
ClientWebSocket code. The reverse is already possible using [Microsoft.AspNetCore.Mvc.Testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests), 
but can be supplemented by MockHttp.WebSocket's serialization helpers since they simply wrap WebSocket:

```csharp
// Standard Microsoft.AspNetCore.Mvc.Testing stuff, no custom injection required
using var host = new WebApplicationFactory<Program>();
var webSocketClient = host.Server.CreateWebSocketClient();
var webSocket = await webSocketClient.ConnectAsync(new Uri(streamUrl!), CancellationToken.None);

var mockJsonClient = new JsonWebSocket(webSocket);

// Have at...
```