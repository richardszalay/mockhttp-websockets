using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Text.Json;

namespace RichardSzalay.MockHttp.WebSockets.Serialization;

/// <summary>
/// WebSocket that serializes and deserializes JSON messages
/// </summary>
public class JsonWebSocket : SerializedWebSocket
{
    private readonly JsonSerializerOptions jsonOptions;

    /// <param name="jsonOptions">JSON options to apply to (de)serialization</param>
    public JsonWebSocket(WebSocket rawWebSocket, JsonSerializerOptions jsonOptions) : base(rawWebSocket)
    {
        this.jsonOptions = jsonOptions;
    }

    protected override async ValueTask<T> DeserializeAsync<T>(WebSocketMessageType messageType, Stream stream)
    {
        var result = await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions);

        if (result == null)
        {
            throw new InvalidOperationException("Received JSON message was 'null'");
        }

        return result;
    }

    protected override ValueTask<WebSocketMessageType> SerializeAsync<T>(T value, Stream stream,
        CancellationToken cancellationToken)
    {
        // Intentionally synchronous as we know the stream writes to a pipe,
        // and using the synchronous Serialize method can make use of fast-path
        // serialization codegen
        JsonSerializer.Serialize(stream, value, jsonOptions);

        return ValueTask.FromResult(WebSocketMessageType.Text);
    }
    
    public static WebSocketMessageLoop<JsonWebSocket, TBaseClass> MessageLoop<TBaseClass>(JsonSerializerOptions jsonOptions) =>
        new(ws => new JsonWebSocket(ws, jsonOptions));
}