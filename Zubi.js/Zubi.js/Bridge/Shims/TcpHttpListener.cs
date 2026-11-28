using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Zubi.js.Bridge.Shims
{
    /// <summary>
    /// Reemplazo de <see cref="System.Net.HttpListener"/> sobre <see cref="TcpListener"/>.
    /// </summary>
    internal sealed class TcpHttpListener : IDisposable
    {
        private readonly List<string> _prefixes = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Channel<HttpListenerContext>? _queue;
        private volatile bool _listening;

        public IList<string> Prefixes => _prefixes;

        public bool IsListening => _listening;

        public void Start()
        {
            if (_listening) throw new InvalidOperationException("Listener already started.");
            if (_prefixes.Count == 0) throw new InvalidOperationException("No prefixes configured.");

            var (address, port) = ParsePrefix(_prefixes[0]);
            _queue = Channel.CreateUnbounded<HttpListenerContext>();
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(address, port);
            _listener.Start();
            _listening = true;
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _listening = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _queue?.Writer.TryComplete(); } catch { }
        }

        public void Dispose() => Stop();

        public Task<HttpListenerContext> GetContextAsync()
        {
            return _queue!.Reader.ReadAsync().AsTask();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { break; }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stream = client.GetStream();
                        var request = await ParseRequestAsync(stream, ct);
                        var response = new HttpListenerResponse(stream);
                        var context = new HttpListenerContext(client, request, response, stream);
                        await _queue!.Writer.WriteAsync(context, ct);
                    }
                    catch
                    {
                        try { client.Close(); } catch { }
                    }
                });
            }
        }

        private static (IPAddress, int) ParsePrefix(string prefix)
        {
            const string scheme = "http://";
            if (!prefix.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Only 'http://' prefixes are supported: {prefix}");

            // Extraer "host:port" descartando la ruta final ("/")
            var rest = prefix.Substring(scheme.Length);
            var slash = rest.IndexOf('/');
            if (slash >= 0)
                rest = rest.Substring(0, slash);

            string host;
            int port;

            var lastColon = rest.LastIndexOf(':');
            if (lastColon >= 0)
            {
                host = rest.Substring(0, lastColon);
                if (!int.TryParse(rest.AsSpan(lastColon + 1), out port))
                    throw new UriFormatException($"Invalid port in prefix: {prefix}");
            }
            else
            {
                host = rest;
                port = 80;
            }

            IPAddress addr;
            if (string.IsNullOrEmpty(host)
                || host == "+"
                || host == "*"
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                // Semántica HttpListener: '+' y '*' = todas las interfaces
                addr = IPAddress.Any;
            }
            else if (!IPAddress.TryParse(host, out addr!))
            {
                addr = Dns.GetHostAddresses(host)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? IPAddress.Loopback;
            }

            return (addr, port);
        }

        // Lee exactamente hasta CRLFCRLF, byte a byte (para no romper WebSockets con over-read).
        private static async Task<HttpListenerRequest> ParseRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new List<byte>(512);
            var one = new byte[1];

            while (true)
            {
                int n = await stream.ReadAsync(one, 0, 1, ct);
                if (n == 0) throw new EndOfStreamException("Connection closed while reading headers.");
                buffer.Add(one[0]);

                int c = buffer.Count;
                if (c >= 4 &&
                    buffer[c - 4] == (byte)'\r' && buffer[c - 3] == (byte)'\n' &&
                    buffer[c - 2] == (byte)'\r' && buffer[c - 1] == (byte)'\n')
                    break;

                if (c > 65536) throw new InvalidOperationException("Request headers too large.");
            }

            var raw = new byte[buffer.Count - 4];
            buffer.CopyTo(0, raw, 0, raw.Length);
            var text = Encoding.ASCII.GetString(raw);
            var lines = text.Split("\r\n");
            if (lines.Length == 0) throw new InvalidOperationException("Empty request.");

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 3) throw new InvalidOperationException("Malformed request line.");

            var method = requestLine[0];
            var target = requestLine[1];

            var headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                headers.Add(name, value);
            }

            Uri url;
            if (Uri.TryCreate(target, UriKind.Absolute, out var abs) &&
                (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            {
                url = abs;
            }
            else
            {
                var host = headers["Host"] ?? "localhost";
                url = new Uri($"http://{host}{target}");
            }

            var queryString = ParseQueryString(url.Query);

            return new HttpListenerRequest(method, url, headers, queryString);
        }

        private static NameValueCollection ParseQueryString(string? query)
        {
            var nvc = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return nvc;
            if (query[0] == '?') query = query.Substring(1);
            if (query.Length == 0) return nvc;

            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key, value;
                if (eq < 0) { key = pair; value = ""; }
                else { key = pair.Substring(0, eq); value = pair.Substring(eq + 1); }
                key = Uri.UnescapeDataString(key.Replace('+', ' '));
                value = Uri.UnescapeDataString(value.Replace('+', ' '));
                nvc.Add(key, value);
            }

            return nvc;
        }
    }
}