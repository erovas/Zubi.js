using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Zubi.js.Bridge.Shims
{
    internal sealed class HttpListenerContext : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private bool _disposed;

        public HttpListenerContext(TcpClient client, HttpListenerRequest request, HttpListenerResponse response, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
            Request = request;
            Response = response;
        }

        public HttpListenerRequest Request { get; }
        public HttpListenerResponse Response { get; }
        public bool WebSocketUpgraded { get; private set; }

        public async Task<HttpListenerWebSocketContext> AcceptWebSocketAsync(string? subProtocol)
        {
            var key = Request.Headers["Sec-WebSocket-Key"];
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Missing 'Sec-WebSocket-Key' header.");

            var accept = ComputeAccept(key);
            Response.Suppress(); // desactiva el pipeline HTTP normal

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 101 Switching Protocols\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Accept: ").Append(accept).Append("\r\n");
            if (!string.IsNullOrEmpty(subProtocol))
                sb.Append("Sec-WebSocket-Protocol: ").Append(subProtocol).Append("\r\n");
            sb.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            await _stream.WriteAsync(bytes, CancellationToken.None);
            await _stream.FlushAsync();

            WebSocketUpgraded = true;

            var ws = WebSocket.CreateFromStream(
                _stream,
                isServer: true,
                subProtocol: subProtocol,
                keepAliveInterval: TimeSpan.FromSeconds(30));

            return new HttpListenerWebSocketContext(ws);
        }

        private static string ComputeAccept(string key)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
            return Convert.ToBase64String(hash);
        }

        public void Dispose()
        {
            if (_disposed) 
                return;
            
            _disposed = true;
            try { _client.Close(); } 
            catch { }
        }
    }
}