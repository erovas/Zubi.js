using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Zubi.js;

namespace CDP
{
    public class CDP : Plugin
    {
        private long _nextId;
        private readonly Uri _Uri;
        private ClientWebSocket? _ws;
        private readonly TaskCompletionSource _ready;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending;

        public event EventHandler<object>? ConsoleMessage;
        public event EventHandler<object>? ExceptionThrown;
        public event EventHandler<object>? LogEntry;
        public event EventHandler<object>? NetworkRequest;
        public event EventHandler<object>? NetworkResponse;
        public event EventHandler? PageLoad;

        public CDP() 
        {
            if (App.Browser.Engine != BrowserEngine.Chromium)
                throw new InvalidOperationException("The running browser is not based on Chromium.");

            if (App.Browser.DebugPort <= 0)
                throw new InvalidOperationException("DebugPort not configured in config.json.");

            _Uri = new Uri(App.Browser.Href);
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = new();

            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            try
            {
                // 1) Descubrir la URL del WS a partir de las primitivas de App.
                var wsUrl = await GetWebSocketUrlAsync(_Uri) ?? throw new InvalidOperationException($"Could not discover the debug WebSocket for '{App.Browser.Name}'.");

                // 2) Abrir la conexión.
                var ws = new ClientWebSocket();
                try
                {
                    await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                }
                catch
                {
                    ws.Dispose();
                    throw new InvalidOperationException($"Could not connect to the debug WebSocket at '{wsUrl}'.");
                }

                _ws = ws;
                _ = ListenAsync();

                // Habilitar dominios fundamentales
                await SendAndWaitAsync("Runtime.enable", new { });
                await SendAndWaitAsync("Page.enable", new { });
                await SendAndWaitAsync("DOM.enable", new { });
                await SendAndWaitAsync("Network.enable", new { });
                await SendAndWaitAsync("Log.enable", new { });

                _ready.TrySetResult();
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
            }
        }

        /// <summary>Zubi.CDP.Evaluate("1 + 1")</summary>
        public async Task<object?> Evaluate(string expression)
        {
            await _ready.Task;
            var resp = await SendAndWaitAsync("Runtime.evaluate", new
            {
                expression,
                generatePreview = true,
                returnByValue = false
            });
            return resp.GetProperty("result").GetProperty("result");
        }

        /// <summary>Zubi.CDP.GetProperties("miObjeto")</summary>
        public async Task<object?> GetProperties(string expression)
        {
            await _ready.Task;
            var evalResp = await SendAndWaitAsync("Runtime.evaluate", new { expression, generatePreview = true });
            var result = evalResp.GetProperty("result").GetProperty("result");

            if (!result.TryGetProperty("objectId", out var oid)) 
                return null;

            var propsResp = await SendAndWaitAsync("Runtime.getProperties", new
            {
                objectId = oid.GetString(),
                ownProperties = true,
                generatePreview = true
            });
            return propsResp.GetProperty("result");
        }

        /// <summary>Zubi.CDP.Navigate("https://google.com")</summary>
        public async Task Navigate(string url)
        {
            await _ready.Task;
            await SendAndWaitAsync("Page.navigate", new { url });
        }

        /// <summary>Zubi.CDP.Reload()</summary>
        public async Task Reload()
        {
            await _ready.Task;
            await SendAndWaitAsync("Page.reload", new { ignoreCache = false });
        }

        /// <summary>Zubi.CDP.TakeScreenshot("jpeg", 80) - Devuelve un string en Base64</summary>
        public async Task<string> TakeScreenshot(string format = "png", int? quality = null)
        {
            await _ready.Task;

            format = format?.ToLower() ?? string.Empty;

            // Validación básica del formato
            if (format != "png" && format != "jpeg" && format != "webp")
                throw new ArgumentException("Unsupported format. Use 'png', 'jpeg' or 'webp'.");

            var @params = new Dictionary<string, object>
            {
                { "format", format }
            };

            // Solo añadimos 'quality' si se proporciona (y es útil para jpeg)
            if (quality.HasValue)
            {
                @params["quality"] = quality.Value;
            }

            var resp = await SendAndWaitAsync("Page.captureScreenshot", @params);
            return resp.GetProperty("result").GetProperty("data").GetString() ?? "";
        }

        /// <summary>Zubi.CDP.QuerySelector("body > div") - Devuelve el HTML del nodo</summary>
        public async Task<string?> QuerySelector(string selector)
        {
            await _ready.Task;
            var docResp = await SendAndWaitAsync("DOM.getDocument", new { depth = 1 });
            var rootId = docResp.GetProperty("result").GetProperty("root").GetProperty("nodeId").GetInt32();

            var selResp = await SendAndWaitAsync("DOM.querySelector", new { nodeId = rootId, selector });

            if (selResp.GetProperty("result").GetProperty("nodeId").GetInt32() == 0)
                return null; // No encontrado

            var htmlResp = await SendAndWaitAsync("DOM.getOuterHTML", new { nodeId = rootId }); // Nota: En CDP, getOuterHTML requiere el nodeId real, no el root.
            return htmlResp.GetProperty("result").GetProperty("outerHTML").GetString();
        }

        /// <summary>Zubi.CDP.ClearCache()</summary>
        public async Task ClearCache()
        {
            await _ready.Task;
            await SendAndWaitAsync("Network.clearBrowserCache", new { });
        }

        /// <summary>Zubi.CDP.SetHeaders({ "Authorization": "Bearer token" })</summary>
        public async Task SetHeaders(Dictionary<string, string> headers)
        {
            await _ready.Task;
            await SendAndWaitAsync("Network.setExtraHTTPHeaders", new { headers });
        }

        protected override void Dispose(bool disposing)
        {
            _ws?.Dispose();
            _ws = null;
        }

        //=========================================//

        private async Task ListenAsync()
        {
            try
            {
                while (_ws?.State == WebSocketState.Open)
                {
                    var payload = await ReceiveFullMessageAsync(_ws, CancellationToken.None);

                    if (payload is null) 
                        break;

                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    // 1) Respuestas a comandos
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    {
                        var id = idEl.GetInt32();
                        if (_pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(root.Clone());
                        continue;
                    }

                    // 2) Eventos (Emisión a JS)
                    if (!root.TryGetProperty("method", out var methodProp)) 
                        continue;
                    
                    var method = methodProp.GetString();
                    
                    if (!root.TryGetProperty("params", out var paramsEl)) 
                        continue;

                    switch (method)
                    {
                        case "Runtime.consoleAPICalled":
                            ConsoleMessage?.Invoke(this, paramsEl.Clone());
                            break;
                        case "Runtime.exceptionThrown":
                            ExceptionThrown?.Invoke(this, paramsEl.Clone());
                            break;
                        case "Log.entryAdded":
                            LogEntry?.Invoke(this, paramsEl.Clone());
                            break;
                        case "Network.requestWillBeSent":
                            NetworkRequest?.Invoke(this, paramsEl.Clone());
                            break;
                        case "Network.responseReceived":
                            NetworkResponse?.Invoke(this, paramsEl.Clone());
                            break;
                        case "Page.loadEventFired":
                            PageLoad?.Invoke(this, EventArgs.Empty);
                            break;
                    }
                }
            }
            finally
            {
                foreach (var k in _pending.Keys.ToList())
                    if (_pending.TryRemove(k, out var tcs))
                        tcs.TrySetException(new InvalidOperationException("WebSocket closed."));
                
            }
        }

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params)
        {
            var id = Interlocked.Increment(ref _nextId);
            var json = JsonSerializer.Serialize(new { id, method, @params });
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            await _sendLock.WaitAsync();
            try
            {
                await _ws!.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }
            finally { _sendLock.Release(); }

            return await tcs.Task;
        }

        private static async Task<byte[]?> ReceiveFullMessageAsync(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var ms = new MemoryStream();

            while (true)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Zubi] Exception upon receipt: {ex.Message}");
                    return null;
                }
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"[Zubi] Chromium closed the socket. Code: {result.CloseStatus}, Reason: {result.CloseStatusDescription}");
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text) 
                    return null;

                ms.Write(buffer, 0, result.Count);
               
                if (result.EndOfMessage) 
                    return ms.ToArray();
            }
        }

        private static async Task<string?> GetWebSocketUrlAsync(Uri uri)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"{uri.Scheme}://{uri.Host}:{App.Browser.DebugPort}/json");
                using var doc = JsonDocument.Parse(json);

                // 1º preferencia: la pestaña cuya URL corresponde al puerto de la app.
                foreach (var tab in doc.RootElement.EnumerateArray())
                {
                    if (!tab.TryGetProperty("type", out var t) || t.GetString() != "page")
                        continue;

                    if (!tab.TryGetProperty("url", out var u))
                        continue;

                    if (u.GetString()?.Contains($":{uri.Port}") != true)
                        continue;

                    if (tab.TryGetProperty("webSocketDebuggerUrl", out var ws))
                        return ws.GetString();
                }

                // 2º preferencia: la primera pestaña disponible.
                foreach (var tab in doc.RootElement.EnumerateArray())
                {
                    if (!tab.TryGetProperty("type", out var t) || t.GetString() != "page")
                        continue;

                    if (tab.TryGetProperty("webSocketDebuggerUrl", out var ws))
                        return ws.GetString();
                }
            }
            catch { /* devolvemos null */ }

            return null;
        }
    
    }
}