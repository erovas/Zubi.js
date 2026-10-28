using System.Net;
using System.Text;
using Zubi.js.Scripts;

namespace Zubi.js.Bridge
{
    /// <summary>
    /// Genera el servidor http para servir los archivos
    /// </summary>
    internal sealed class FileServer
    {
        private readonly HttpListener _listener = new();
        private readonly string _rootPath;
        private readonly SessionStore _session;

        public Func<HttpListenerContext, Task>? WebSocketHandler { get; set; }

        public FileServer(string baseUrl, string rootPath, SessionStore session)
        {
            _listener.Prefixes.Add(baseUrl);
            _rootPath = Path.GetFullPath(rootPath);
            _session = session;
        }

        public void Start()
        {
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public void Stop()
        {
            try { _listener.Stop(); }
            catch { }
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;

                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleAsync(ctx);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[http] {ex.Message}");
                        try
                        {
                            ctx.Response.StatusCode = 500;
                            ctx.Response.Close();
                        }
                        catch { }
                    }
                });
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url!.AbsolutePath;

            // Se está llamando al WebSocket
            if (req.IsWebSocketRequest && path == "/ws" && WebSocketHandler is not null)
            {
                await WebSocketHandler(ctx);
                return;
            }

            var rel = path.TrimStart('/');

            if (string.IsNullOrWhiteSpace(rel))
                rel = Utils.INDEX_HTML;

            var full = Path.GetFullPath(Path.Combine(_rootPath, rel));
            var isIndex = rel.Equals(Utils.INDEX_HTML, StringComparison.OrdinalIgnoreCase);
            var exists = File.Exists(full);

            if (!isIndex && !exists)
            {
                await WriteTextAsync(ctx, 404, "Not Found");
                return;
            }

            var contentType = GetContentType(full);

            // Se va a cargar el index.html
            // Se inyecta la magia
            if (isIndex)
            {
                var html = exists ? await File.ReadAllTextAsync(full) : ScriptResources.ErrorPage;

                if (req.HttpMethod == "GET")
                {
                    var tabToken = _session.ConsumeMasterToken(req.QueryString["token"]);
                    var meta = tabToken is not null ? $"<meta name=\"bridge-token\" content=\"{tabToken}\">" : string.Empty;
                    var script = "<script>";
                    script += Environment.NewLine;
                    script += ScriptResources.MainScript;
                    script += Environment.NewLine;
                    script += "</script>";
                    var result = Environment.NewLine + meta + Environment.NewLine + script + Environment.NewLine;
                    html = result + html;
                }

                await WriteBytesAsync(ctx, 200, Encoding.UTF8.GetBytes(html), contentType, true);
                return;
            }

            // Entregar el archivo pedido por XHR, fecth, <script>, etc.
            await WriteBytesAsync(ctx, 200, await File.ReadAllBytesAsync(full), contentType);
        }

        #region Helpers

        private static Task WriteTextAsync(HttpListenerContext ctx, int status, string text)
        {
            return WriteBytesAsync(ctx, status, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8");
        }


        private static async Task WriteBytesAsync(HttpListenerContext ctx, int status, byte[] body, string type, bool securityHeaders = false)
        {
            var res = ctx.Response;
            try
            {
                res.StatusCode = status;
                res.ContentType = type;
                res.ContentLength64 = body.Length;
                res.AddHeader("Cache-Control", "no-store");
                if (securityHeaders)
                {
                    res.AddHeader("Content-Security-Policy",
                        "frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
                    res.AddHeader("X-Content-Type-Options", "nosniff");
                }
                await res.OutputStream.WriteAsync(body);
            }
            finally
            {
                try { res.Close(); }
                catch { }
            }
        }

        private static string GetContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" or ".mjs" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".wasm" => "application/wasm",
                _ => "application/octet-stream"
            };
        }

        #endregion

    }
}