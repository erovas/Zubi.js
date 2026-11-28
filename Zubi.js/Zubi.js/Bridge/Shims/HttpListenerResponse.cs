using System.Net.Sockets;
using System.Text;

namespace Zubi.js.Bridge.Shims
{
    internal sealed class HttpListenerResponse
    {
        private readonly NetworkStream _stream;
        private readonly Dictionary<string, List<string>> _headers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ResponseStream _outputStream;
        private bool _headersSent;
        private bool _suppressed;
        private bool _closed;

        public HttpListenerResponse(NetworkStream stream)
        {
            _stream = stream;
            _outputStream = new ResponseStream(this);
        }

        public int StatusCode { get; set; } = 200;
        public string? ContentType { get; set; }
        public long ContentLength64 { get; set; } = -1;
        public Stream OutputStream => _outputStream;

        public void AddHeader(string name, string value)
        {
            if (!_headers.TryGetValue(name, out var list))
            {
                list = new List<string>();
                _headers[name] = list;
            }
            list.Add(value);
        }

        internal void Suppress()
        {
            _suppressed = true;
            _headersSent = true;
        }

        internal async Task SendHeadersAsync()
        {
            if (_headersSent || _suppressed) return;
            _headersSent = true;
            var bytes = BuildHeaders();
            await _stream.WriteAsync(bytes, CancellationToken.None);
            await _stream.FlushAsync();
        }

        internal void SendHeadersSync()
        {
            if (_headersSent || _suppressed) return;
            _headersSent = true;
            var bytes = BuildHeaders();
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        internal async Task WriteBodyAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await SendHeadersAsync();
            if (count > 0) await _stream.WriteAsync(buffer, offset, count, ct);
        }

        internal async ValueTask WriteBodyAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
        {
            await SendHeadersAsync();
            if (buffer.Length > 0) await _stream.WriteAsync(buffer, ct);
        }

        internal void WriteBodySync(byte[] buffer, int offset, int count)
        {
            SendHeadersSync();
            if (count > 0) _stream.Write(buffer, offset, count);
        }

        internal void FlushSync() { try { _stream.Flush(); } catch { } }
        internal Task FlushAsync() => _stream.FlushAsync();

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try
            {
                if (!_headersSent && !_suppressed)
                    SendHeadersSync();
                _stream.Flush();
            }
            catch { }
        }

        private byte[] BuildHeaders()
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(StatusCode).Append(' ').Append(GetReason(StatusCode)).Append("\r\n");

            if (!string.IsNullOrEmpty(ContentType))
                sb.Append("Content-Type: ").Append(ContentType).Append("\r\n");

            long len = ContentLength64 >= 0 ? ContentLength64 : 0;
            sb.Append("Content-Length: ").Append(len).Append("\r\n");

            foreach (var kvp in _headers)
                foreach (var value in kvp.Value)
                    sb.Append(kvp.Key).Append(": ").Append(value).Append("\r\n");

            sb.Append("\r\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static string GetReason(int status) => status switch
        {
            200 => "OK",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            304 => "Not Modified",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => ""
        };
    }
}