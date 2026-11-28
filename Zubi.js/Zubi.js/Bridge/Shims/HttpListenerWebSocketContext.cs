using System.Net.WebSockets;

namespace Zubi.js.Bridge.Shims
{
    internal sealed class HttpListenerWebSocketContext
    {
        public HttpListenerWebSocketContext(WebSocket webSocket) { WebSocket = webSocket; }
        public WebSocket WebSocket { get; }
    }
}