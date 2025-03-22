using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using RichardSzalay.MockHttp.WebSockets.Serialization;

namespace RichardSzalay.MockHttp.WebSockets.Tests.Examples;

/// <summary>
/// This test demonstrates sending and receiving JSON messages using the JsonWebSocket class.
///
/// In this example, the client appends an ! to end of the Text property and the server appends a ? 
/// </summary>
/// <returns></returns>
public class JsonWebSocketTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;

    [Fact]
    public async ValueTask Can_send_and_receive_json_messages()
    {
        var mockServer = MockWebSocketServer.ForEndpoint(async (ws, ct) =>
        {
            var jsonServer = new JsonWebSocket(ws, JsonOptions);

            while (!jsonServer.RawWebSocket.CloseStatus.HasValue)
            {
                var result = await jsonServer.TryReceiveAsync<SimpleMessage>(ct);

                if (result.TryGetMessage(out var message))
                {
                    await jsonServer.SendAsync(new SimpleMessage(message.Text + "?"), ct);
                }
                else
                {
                    break;
                }
            }

            if (jsonServer.RawWebSocket.CloseStatus.HasValue)
            {
                await jsonServer.RawWebSocket.CloseAsync(jsonServer.RawWebSocket.CloseStatus.Value, jsonServer.RawWebSocket.CloseStatusDescription, ct);
            }
        });

        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri("ws://localhost"), mockServer.ToMessageInvoker(), CancellationToken.None);
        var jsonClient = new JsonWebSocket(client, JsonOptions);

        var message = new SimpleMessage("Test");

        for (var i = 0; i < 3; i++)
        {
            // Client appends a !
            await jsonClient.SendAsync(new SimpleMessage(message.Text + "!"), cancellationToken);
            
            // Server appends a ?
            message = await jsonClient.ExpectAsync<SimpleMessage>(cancellationToken);
        }
        
        Assert.Equal("Test!?!?!?", message.Text);

        await jsonClient.RawWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "We're done here", cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Source generation friendly!
        TypeInfoResolver = new SimpleMessageContext()
    };
}

public record SimpleMessage(string Text);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SimpleMessage))]
internal partial class SimpleMessageContext : JsonSerializerContext
{
}