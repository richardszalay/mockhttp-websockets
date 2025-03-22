using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets.Tests;

public class BinaryMessageTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    /// <summary>
    /// This test demonstrates that the raw WebSocket API can be used to send and receive binary messages. It acts as both a test
    /// and an example.
    /// 
    /// The test has a server that accepts int32 binary messages and increments the value by 1 before sending it back to the client
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async ValueTask Can_send_and_receive_binary_messages()
    {
        using var mockServer = MockWebSocketServer.ForEndpoint(async (ws, ct) =>
        {
            byte[] receiveBuffer = new byte[4];

            while (!ws.CloseStatus.HasValue)
            {
                var result = await ws.ReceiveAsync(receiveBuffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    continue;
                }

                var messageValue = BitConverter.ToInt32(receiveBuffer);

                await ws.SendAsync(BitConverter.GetBytes(messageValue + 1), WebSocketMessageType.Binary, false, ct);
            }

            if (ws.State == WebSocketState.CloseReceived)
            {
                await ws.CloseAsync(ws.CloseStatus.Value, ws.CloseStatusDescription, ct);
            }
        });

        var client = new ClientWebSocket();

        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), cancellationToken);

        int messageValue = 1;
        byte[] receiveBuffer = new byte[4];

        for (var i = 0; i < 10; i++)
        {
            await client.SendAsync(BitConverter.GetBytes(messageValue), WebSocketMessageType.Binary, false,
                cancellationToken);

            await client.ReceiveAsync(receiveBuffer, cancellationToken);

            messageValue = BitConverter.ToInt32(receiveBuffer);
        }

        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);

        Assert.Equal(11, messageValue);
    }
}
