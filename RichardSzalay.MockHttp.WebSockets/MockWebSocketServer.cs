using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Net;
using RichardSzalay.MockHttp.WebSockets.Internal;
using System.Text;
using System.Security.Cryptography;

namespace RichardSzalay.MockHttp.WebSockets;

/// <summary>
/// A mock server that replace a remote WebSocket server for testing purposes, without requiring any real network connections
/// </summary>
public class MockWebSocketServer : HttpMessageHandler
{
    private readonly Dictionary<string, MockWebSocketEndpoint> endpoints = new();
    
    /// <summary>
    /// Creates a MockWebSocketServer with a single <see cref="MockWebSocketEndpoint"/> at the default path (/),
    /// using the provided <see cref="AcceptWebSocketHandler"/> to 'run' the endpoint.
    /// </summary>
    /// <param name="acceptHandler">An async delegate that will accept WebSocket connections from a remote client. When this handler exits,
    /// the WebSocket connection will be shut down.</param>
    public static MockWebSocketServer ForEndpoint(AcceptWebSocketHandler acceptHandler)
        => ForEndpoint(new MockWebSocketEndpoint(acceptHandler));
    
    /// <summary>
    /// Creates a MockWebSocketServer with a single <see cref="MockWebSocketEndpoint"/> at the default path (/),
    /// using the provided <see cref="AcceptWebSocketHandler"/> to 'run' the endpoint and a <see cref="ValidateWebSocketRequestHandler"/>
    /// to validate the incoming request.
    /// </summary>
    /// <param name="validateHandler">A delegate that will validate the request before a WebSocket connection is accepted. Commonly used for
    /// header (authentication or signature) validation</param>
    /// <param name="acceptHandler">An async delegate that will accept WebSocket connections from a remote client. When this handler exits,
    /// the WebSocket connection will be shut down.</param>
    public static MockWebSocketServer ForEndpoint(ValidateWebSocketRequestHandler validateHandler, AcceptWebSocketHandler acceptHandler)
            => ForEndpoint(new MockWebSocketEndpoint(validateHandler, acceptHandler));
    
    /// <summary>
    /// Creates a MockWebSocketServer with a single <see cref="MockWebSocketEndpoint"/> at the default path (/)
    /// </summary>
    /// <param name="endpoint">The <see cref="MockWebSocketEndpoint"/> that will handle WebSocket requests to the default path (/)</param>
    public static MockWebSocketServer ForEndpoint(MockWebSocketEndpoint endpoint)
        => ForEndpoint("/", endpoint);

    /// <summary>
    /// Creates a MockWebSocketServer with a single <see cref="MockWebSocketEndpoint"/> at the given <paramref name="path"/>
    /// </summary>
    /// <param name="path">The server path to apply the endpoint</param>
    /// <param name="endpoint">The <see cref="MockWebSocketEndpoint"/> that will handle WebSocket requests to <paramref name="path"/></param>
    public static MockWebSocketServer ForEndpoint(string path, MockWebSocketEndpoint endpoint)
    {
        var server = new MockWebSocketServer();
        server.AddEndpoint(path, endpoint);
        return server;
    }
    
    /// <summary>
    /// All the WebSockets that have been connected to this instance
    /// </summary>
    public IEnumerable<WebSocket> WebSockets => webSockets.AsEnumerable();
    
    private readonly ConcurrentBag<WebSocket> webSockets = new();

    /// <summary>
    /// Creates an <see cref="HttpMessageInvoker"/>, which can be used with <see cref="ClientWebSocket"/> to connect
    /// endpoints defined by this instance.
    /// </summary>
    /// <returns>An <see cref="HttpMessageInvoker"/>, which can be used with <see cref="ClientWebSocket"/></returns>
    public HttpMessageInvoker ToMessageInvoker() => new HttpMessageInvoker(this);

    /// <summary>
    /// Add an HTTP endpoint that accepts WebSocket connections
    /// </summary>
    /// <param name="path"></param>
    /// <param name="endpoint">
    /// An async delegate that will accept WebSocket connections from a remote client.
    /// When this handler exits, the WebSocket connection will be closed.
    /// </param>
    public void AddEndpoint(string path, MockWebSocketEndpoint endpoint)
    {
        endpoints[path] = endpoint;
    }

    /// <summary>
    /// Add an HTTP endpoint that accepts WebSocket connections
    /// </summary>
    /// <param name="path"></param>
    /// <param name="acceptFunc">
    /// An async delegate that will accept WebSocket connections from a remote client.
    /// When this handler exits, the WebSocket connection will be shut down.
    /// </param>
    public void AddEndpoint(string path, AcceptWebSocketHandler acceptFunc) =>
        AddEndpoint(path, new MockWebSocketEndpoint(acceptFunc));

    public void AddEndpoint(string path, ValidateWebSocketRequestHandler validateHandler, AcceptWebSocketHandler acceptHandler) =>
        AddEndpoint(path, new MockWebSocketEndpoint(validateHandler, acceptHandler));

    /// <summary>
    /// Waits for the given state on all connected WebSockets
    /// </summary>
    /// <param name="state"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task WaitForAllStatesAsync(WebSocketState state, CancellationToken cancellationToken = default)
    {
        if (cancellationToken == CancellationToken.None)
        {
            cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        }

        var allValid = false;

        while (!allValid)
        {
            allValid = WebSockets.All(ws => ws.State == state);

            await Task.Delay(100, cancellationToken);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!endpoints.TryGetValue(request.RequestUri!.AbsolutePath, out var endpoint))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No MockWebSocketServer endpoint found for this path: {request.RequestUri!.AbsolutePath}")
            };
        }
        
        var isWebSocketRequest = request.Headers.Connection.Contains("Upgrade", StringComparer.OrdinalIgnoreCase);

        if (!isWebSocketRequest)
        {
            // 400 is more common, but 426 is technically the correct status code
            return new HttpResponseMessage(HttpStatusCode.UpgradeRequired);
        }

        if (await endpoint.ValidateAsync(request) is { } validationResponse)
        {
            // If the endpoint's validation delegate returns an HttpResponseMessage, that
            // is returned rather than connecting to the WebSocket
            return validationResponse;
        }

        var response = new HttpResponseMessage(HttpStatusCode.SwitchingProtocols)
        {
            RequestMessage = request,
            Content = new MockWebSocketContent(async (ws) =>
            {
                webSockets.Add(ws);
                await endpoint.AcceptAsync(ws, cancellationToken);
            })
        };

        UpgradeWebSocketResponse(response);

        return response;
    }

    /// <summary>
    /// Sets the required headers on the response to indicate to the caller that the connection should be upgraded to a WebSocket connection
    /// </summary>
    public static void UpgradeWebSocketResponse(HttpResponseMessage response)
    {
        var request = response.RequestMessage!;

        var responseHeaders = response.Headers;
        responseHeaders.Connection.Add("Upgrade");
        responseHeaders.Upgrade.Add(new System.Net.Http.Headers.ProductHeaderValue("websocket"));
        responseHeaders.TryAddWithoutValidation("Sec-WebSocket-Accept", CreateResponseKey(request.Headers.GetValues("Sec-WebSocket-Key").First()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var ws in webSockets)
            {
                ws.Dispose();
            }
        }
        
        base.Dispose(disposing);
    }

    private static ReadOnlySpan<byte> EncodedWebSocketKey => "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"u8;

    // Taken, with thanks!, from dotnet/aspnetcore/src/Middleware/WebSockets/src/HandshakeHelpers.cs
    private static string CreateResponseKey(string requestKey)
    {
        // "The value of this header field is constructed by concatenating /key/, defined above in step 4
        // in Section 4.2.2, with the string "258EAFA5-E914-47DA-95CA-C5AB0DC85B11", taking the SHA-1 hash of
        // this concatenated value to obtain a 20-byte value and base64-encoding"
        // https://tools.ietf.org/html/rfc6455#section-4.2.2

        // requestKey is already verified to be small (24 bytes) by 'IsRequestKeyValid()' and everything is 1:1 mapping to UTF8 bytes
        // so this can be hardcoded to 60 bytes for the requestKey + static websocket string
        Span<byte> mergedBytes = stackalloc byte[60];
        Encoding.UTF8.GetBytes(requestKey, mergedBytes);
        EncodedWebSocketKey.CopyTo(mergedBytes[24..]);

        Span<byte> hashedBytes = stackalloc byte[20];
        var written = SHA1.HashData(mergedBytes, hashedBytes);
        if (written != 20)
        {
            throw new InvalidOperationException("Could not compute the hash for the 'Sec-WebSocket-Accept' header.");
        }

        return Convert.ToBase64String(hashedBytes);
    }
}