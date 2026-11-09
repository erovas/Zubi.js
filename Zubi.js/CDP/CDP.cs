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
        private int _windowId;
        private string? _targetId;
        private readonly Uri _Uri;
        private string? _sessionId;
        private ClientWebSocket? _browserWs;
        private readonly TaskCompletionSource _ready;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending;

        #region Events

        public event EventHandler<object>? ConsoleMessage;
        public event EventHandler<object>? ExceptionThrown;
        public event EventHandler<object>? LogEntry;
        public event EventHandler<object>? NetworkRequest;
        public event EventHandler<object>? NetworkResponse;
        public event EventHandler? PageLoad;

        #endregion

        #region Properties

        /// <summary>
        /// "normal", "minimized", "maximized", "fullscreen"
        /// </summary>
        public Task<string> WindowState
        {
            get => GetWindowState();
        }

        #endregion

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
                // 1. Descubrir el WebSocket del navegador
                var browserWsUrl = await DiscoverBrowserWebSocketAsync(_Uri);

                if (browserWsUrl == null)
                    throw new InvalidOperationException($"Could not discover the debug WebSocket for '{App.Browser.Name}'.");

                try
                {
                    _browserWs = new ClientWebSocket();
                    await _browserWs.ConnectAsync(new Uri(browserWsUrl), CancellationToken.None);
                }
                catch
                {
                    _browserWs?.Dispose();
                    throw new InvalidOperationException($"Could not connect to the debug WebSocket at '{browserWsUrl}'.");
                }

                _ = ListenAsync(_browserWs); // Escuchamos todo por aquí

                // 2. Descubrir el targetId de la pestaña (usando /json, como ya hacías)
                (var pageWsUrl, _targetId) = await GetWebSocketUrlAsync(_Uri);

                if (string.IsNullOrEmpty(_targetId)) 
                    throw new InvalidOperationException("The targetId was not found.");

                // 3. CREAR LA SESIÓN PLANA (Attach)
                var attachResp = await SendAndWaitAsync("Target.attachToTarget", new
                {
                    targetId = _targetId,
                    flatten = true // CLAVE: Usa sesiones planas
                });

                _sessionId = attachResp.GetProperty("result").GetProperty("sessionId").GetString();

                // Habilitar dominios fundamentales
                await SendAndWaitAsync("Runtime.enable", new { }, _sessionId);
                await SendAndWaitAsync("Page.enable", new { }, _sessionId);
                await SendAndWaitAsync("DOM.enable", new { }, _sessionId);
                await SendAndWaitAsync("Network.enable", new { }, _sessionId);
                await SendAndWaitAsync("Log.enable", new { }, _sessionId);

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
            }, _sessionId);
            return resp.GetProperty("result").GetProperty("result");
        }

        /// <summary>Zubi.CDP.GetProperties("miObjeto")</summary>
        public async Task<object?> GetProperties(string expression)
        {
            await _ready.Task;
            var evalResp = await SendAndWaitAsync("Runtime.evaluate", new { expression, generatePreview = true }, _sessionId);
            var result = evalResp.GetProperty("result").GetProperty("result");

            if (!result.TryGetProperty("objectId", out var oid)) 
                return null;

            var propsResp = await SendAndWaitAsync("Runtime.getProperties", new
            {
                objectId = oid.GetString(),
                ownProperties = true,
                generatePreview = true
            }, _sessionId);
            return propsResp.GetProperty("result");
        }

        /// <summary>Zubi.CDP.Navigate("https://google.com")</summary>
        public async Task Navigate(string url)
        {
            await _ready.Task;
            await SendAndWaitAsync("Page.navigate", new { url }, _sessionId);
        }

        /// <summary>Zubi.CDP.Reload()</summary>
        public async Task Reload()
        {
            await _ready.Task;
            await SendAndWaitAsync("Page.reload", new { ignoreCache = false }, _sessionId);
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
            var docResp = await SendAndWaitAsync("DOM.getDocument", new { depth = 1 }, _sessionId);
            var rootId = docResp.GetProperty("result").GetProperty("root").GetProperty("nodeId").GetInt32();

            var selResp = await SendAndWaitAsync("DOM.querySelector", new { nodeId = rootId, selector }, _sessionId);

            if (selResp.GetProperty("result").GetProperty("nodeId").GetInt32() == 0)
                return null; // No encontrado

            var htmlResp = await SendAndWaitAsync("DOM.getOuterHTML", new { nodeId = rootId }, _sessionId); // Nota: En CDP, getOuterHTML requiere el nodeId real, no el root.
            return htmlResp.GetProperty("result").GetProperty("outerHTML").GetString();
        }

        /// <summary>Zubi.CDP.ClearCache()</summary>
        public async Task ClearCache()
        {
            await _ready.Task;
            await SendAndWaitAsync("Network.clearBrowserCache", new { }, _sessionId);
        }

        /// <summary>Zubi.CDP.SetHeaders({ "Authorization": "Bearer token" })</summary>
        public async Task SetHeaders(Dictionary<string, string> headers)
        {
            await _ready.Task;
            await SendAndWaitAsync("Network.setExtraHTTPHeaders", new { headers }, _sessionId);
        }

        /// <summary>
        /// Zubi.CDP.OpenDevTools("console") 
        /// Abre las DevTools en la pestaña especificada. 
        /// Paneles soportados: elements, console, network, sources, resources, timeline, chrome-recorder, heap-profiler.
        /// </summary>
        public async Task OpenDevTools(string? panelId = null)
        {
            await _ready.Task;
            TabNoDetected();

            var @params = new Dictionary<string, object> { { "targetId", _targetId! } };

            if (!string.IsNullOrWhiteSpace(panelId))
                @params["panelId"] = panelId;

            await SendAndWaitAsync("Target.openDevTools", @params);
        }

        /// <summary>
        /// Zubi.CDP.SetWindowState("maximized") 
        /// Cambia el estado de la ventana. Estados válidos: "normal", "minimized", "maximized", "fullscreen".
        /// </summary>
        public async Task SetWindowState(string state)
        {
            await _ready.Task;
            TabNoDetected();

            // 1. Validar el estado para no saturar el socket con peticiones inválidas
            var validStates = new[] { "normal", "minimized", "maximized", "fullscreen" };

            if (!validStates.Contains(state.ToLowerInvariant()))
                throw new ArgumentException($"Invalid state: '{state}'. Use one of: {string.Join(", ", validStates)}");
            

            // 2. Obtener el ID de la ventana asociada a nuestra pestaña
            var winResp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId = _targetId });

            if (!winResp.TryGetProperty("result", out var result) || !result.TryGetProperty("windowId", out var windowIdProp))
                throw new InvalidOperationException("Could not obtain the windowId for the window.");

            var windowId = windowIdProp.GetInt32();

            // 3. Aplicar el estado
            // IMPORTANTE: CDP rechaza la petición si envías windowState junto con left, top, width o height.
            await SendAndWaitAsync("Browser.setWindowBounds", new
            {
                windowId,
                bounds = new { windowState = state.ToLowerInvariant() }
            });
        }

        /// <summary>
        /// Zubi.CDP.SetWindowGeometry(100, 100, 800, 600)
        /// Mueve y redimensiona la ventana. Si estaba maximizada, la devuelve a "normal" automáticamente.
        /// </summary>
        public async Task SetWindowGeometry(int left, int top, int width, int height)
        {
            await _ready.Task;
            TabNoDetected();

            var windowId = await GetWindowId();

            // Nota: Enviamos "windowState: normal" junto con la geometría. 
            // En este caso, CDP sí lo permite porque "normal" es la base para la geometría.
            await SendAndWaitAsync("Browser.setWindowBounds", new
            {
                windowId,
                bounds = new
                {
                    left,
                    top,
                    width,
                    height
                }
            });
        }

        /// <summary>
        /// Zubi.CDP.GetWindowGeometry()
        /// Devuelve la posición y el tamaño actual de la ventana.
        /// </summary>
        public async Task<WindowGeometry> GetWindowGeometry()
        {
            await _ready.Task;
            TabNoDetected();

            var windowId = await GetWindowId();

            // 2. Consultamos los límites actuales
            var boundsResp = await SendAndWaitAsync("Browser.getWindowBounds", new { windowId });

            if (!boundsResp.TryGetProperty("result", out var boundsResult) || !boundsResult.TryGetProperty("bounds", out var boundsEl))
                throw new InvalidOperationException("The window boundaries could not be obtained..");
            
            var geo = new WindowGeometry();

            // 3. Extraemos los valores con TryGetProperty por seguridad
            if (boundsEl.TryGetProperty("windowState", out var stateProp))
                geo.WindowState = stateProp.GetString() ?? "normal";

            // Nota: Si la ventana está minimizada, algunos sistemas operativos 
            // no devuelven left/top/width/height. Usamos TryGetProperty para evitar crashes.
            if (boundsEl.TryGetProperty("left", out var leftProp))
                geo.Left = leftProp.GetInt32();

            if (boundsEl.TryGetProperty("top", out var topProp))
                geo.Top = topProp.GetInt32();

            if (boundsEl.TryGetProperty("width", out var widthProp))
                geo.Width = widthProp.GetInt32();

            if (boundsEl.TryGetProperty("height", out var heightProp))
                geo.Height = heightProp.GetInt32();

            return geo;
        }

        protected override void Dispose(bool disposing)
        {
            _browserWs?.Dispose();
            _browserWs = null;
        }

        //=========================================//

        private async Task ListenAsync(ClientWebSocket ws)
        {
            try
            {
                while (ws?.State == WebSocketState.Open)
                {
                    var payload = await ReceiveFullMessageAsync(ws, CancellationToken.None);

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

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params, string? sessionId = null)
        {
            var id = Interlocked.Increment(ref _nextId);

            // Si hay sessionId, lo inyectamos en el mensaje
            var payload = new Dictionary<string, object>
            {
                { "id", id },
                { "method", method },
                { "params", @params }
            };

            if (sessionId != null) 
                payload["sessionId"] = sessionId;

            var json = JsonSerializer.Serialize(payload);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            await _sendLock.WaitAsync();
            try
            {
                await _browserWs!.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch 
            { 
                _pending.TryRemove(id, out _); 
                throw; 
            }
            finally { _sendLock.Release(); }

            return await tcs.Task;
        }

        private async Task<string> GetWindowState()
        {
            await _ready.Task;
            
            if (string.IsNullOrEmpty(_targetId)) 
                return "normal";

            try
            {
                var resp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId = _targetId });

                if (resp.TryGetProperty("result", out var result) && result.TryGetProperty("bounds", out var bounds))
                {
                    // Misma lógica: si no hay windowState, es "normal"
                    var state = bounds.TryGetProperty("windowState", out var stateProp) ? stateProp.GetString() ?? "normal" : "normal";
                    return state;
                }
            }
            catch { }

            return "normal";
        }

        private void TabNoDetected()
        {
            if (string.IsNullOrEmpty(_targetId))
                throw new InvalidOperationException("The tab's targetId has not been detected.");
        }

        private async Task<int> GetWindowId()
        {
            if (_windowId == 0)
            {
                var winResp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId = _targetId });

                if (!winResp.TryGetProperty("result", out var result) || !result.TryGetProperty("windowId", out var idProp))
                    throw new InvalidOperationException("Could not obtain the WindowId for the window.");
                
                _windowId = idProp.GetInt32();
            }

            return _windowId;
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

        private static async Task<(string?, string?)> GetWebSocketUrlAsync(Uri uri)
        {
            string? targetId = null;
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
                    {
                        if (tab.TryGetProperty("id", out var idProp))
                            targetId = idProp.GetString();
                        
                        return (ws.GetString(), targetId);
                    }
                        
                }

                // 2º preferencia: la primera pestaña disponible.
                foreach (var tab in doc.RootElement.EnumerateArray())
                {
                    if (!tab.TryGetProperty("type", out var t) || t.GetString() != "page")
                        continue;

                    if (tab.TryGetProperty("webSocketDebuggerUrl", out var ws))
                    {
                        if (tab.TryGetProperty("id", out var idProp))
                            targetId = idProp.GetString();
                        
                        return (ws.GetString(), targetId);
                    }
                        
                }
            }
            catch { /* devolvemos null */ }

            return (null, null);
        }

        private static async Task<string?> DiscoverBrowserWebSocketAsync(Uri uri)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"{uri.Scheme}://{uri.Host}:{App.Browser.DebugPort}/json/version");
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var ws))
                    return ws.GetString();
            }
            catch { }
            return null;
        }
    
    }
}