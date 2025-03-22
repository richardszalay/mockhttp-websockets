using System.Net.WebSockets;

namespace RichardSzalay.MockHttp.WebSockets;

public delegate Task AcceptWebSocketHandler(WebSocket webSocket, CancellationToken cancellationToken);
public delegate Task<HttpResponseMessage?> ValidateWebSocketRequestHandler(HttpRequestMessage request);

public class MockWebSocketEndpoint
{
    private readonly ValidateWebSocketRequestHandler? validateHandler = null;

    private readonly AcceptWebSocketHandler acceptHandler;

    public MockWebSocketEndpoint(AcceptWebSocketHandler acceptFunc)
    {
        this.acceptHandler = acceptFunc;
    }

    public MockWebSocketEndpoint(ValidateWebSocketRequestHandler validateHandler, AcceptWebSocketHandler acceptHandler)
        : this(acceptHandler)
    {
        this.validateHandler = validateHandler;
    }

    public virtual Task<HttpResponseMessage?> ValidateAsync(HttpRequestMessage request)
    {
        if (validateHandler == null)
        {
            return Task.FromResult((HttpResponseMessage?)null);
        }

        return validateHandler.Invoke(request);
    }

    public Task AcceptAsync(WebSocket webSocket, CancellationToken cancellationToken)
        => acceptHandler(webSocket, cancellationToken);
}
