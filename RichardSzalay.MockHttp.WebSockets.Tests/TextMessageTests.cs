using System.Net.WebSockets;
using System.Text;

namespace RichardSzalay.MockHttp.WebSockets.Tests;

public class TextMessageTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    /// <summary>
    /// This test demonstrates that the raw WebSocket API can be used to send and receive binary messages. It acts as both a test
    /// and an example.
    /// 
    /// The test has a server that accepts int32-formatted text messages and increments the value by 1 before sending it back to the client
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async ValueTask Can_send_and_receive_text_messages()
    {
        using var mockServer = MockWebSocketServer.ForEndpoint(async (ws, ct) =>
        {
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

        var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

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

        Assert.Equal(11, messageValue);
    }
}
