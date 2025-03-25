using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets.Serialization;

public delegate Task WebSocketMessageHandler<TWebSocket, TMessage>(TMessage message, TWebSocket webSocket, CancellationToken ctCancellationToken);

public class WebSocketMessageLoop<TWebSocket, TBaseClass> where TWebSocket : SerializedWebSocket
{
    private HashSet<Type> handledTypes = new();
    private List<Func<TBaseClass, TWebSocket, CancellationToken, ValueTask<bool>>> handlers = new();

    private Func<TWebSocket, CancellationToken, Task>? connectHandler = null;
    private Func<WebSocket, CancellationToken, Task>? closeHandler = null;
    
    private readonly Func<WebSocket, TWebSocket> factory;

    public WebSocketMessageLoop(Func<WebSocket, TWebSocket> factory)
    {
        this.factory = factory;
    }
    
    public WebSocketMessageLoop<TWebSocket, TBaseClass> OnConnect(Func<TWebSocket, CancellationToken, Task> handler)
    {
        if (connectHandler == null)
        {
            throw new ArgumentException($"A connect handler is already registered");
        }
        
        connectHandler = handler;

        return this;
    } 

    public WebSocketMessageLoop<TWebSocket, TBaseClass> OnClose(Func<WebSocket, CancellationToken, Task> handler)
    {
        if (closeHandler != null)
        {
            throw new ArgumentException($"A close handler is already registered");
        }

        closeHandler = handler;

        return this;
    }

    public WebSocketMessageLoop<TWebSocket, TBaseClass> AutoClose()
    {
        return OnClose(async (webSocket, ct) =>
        {
            if (webSocket.State == WebSocketState.CloseReceived)
            {
                await webSocket.CloseOutputAsync(webSocket.CloseStatus!.Value, webSocket.CloseStatusDescription, ct);
            }
        });
    }
    
    public WebSocketMessageLoop<TWebSocket, TBaseClass> On<TMessage>(WebSocketMessageHandler<TWebSocket, TMessage> handler)
        where TMessage : TBaseClass
    {
        if (!handledTypes.Add(typeof(TMessage)))
        {
            throw new ArgumentException($"A handler for type {typeof(TMessage)} is already registered");
        }
        
        handlers.Add(async (message, ws, ct) =>
        {
            if (message is not TMessage typedMessage)
            {
                return false;
            }

            await handler(typedMessage, ws, ct);
            return true;
        });

        return this;
    }

    private async Task AcceptAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var serializedWebSocket = factory(webSocket);

        if (connectHandler != null)
        {
            await connectHandler(serializedWebSocket, cancellationToken);
        }

        while (!webSocket.CloseStatus.HasValue)
        {
            var result = await serializedWebSocket.TryReceiveAsync<TBaseClass>(cancellationToken);

            if (result.TryGetMessage(out var message))
            {
                await this.Handle(message, serializedWebSocket, cancellationToken);
            }
        }

        if (closeHandler != null)
        {
            await closeHandler(webSocket, cancellationToken)!;
        }
    }

    private async Task Handle(TBaseClass message, TWebSocket serializedWebSocket, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            if (await handler(message, serializedWebSocket, cancellationToken))
            {
                break;
            }
        }
    }

    public static implicit operator AcceptWebSocketHandler(WebSocketMessageLoop<TWebSocket, TBaseClass> messageLoop) => messageLoop.AcceptAsync;
}
