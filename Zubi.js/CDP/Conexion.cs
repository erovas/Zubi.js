using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using Zubi.js;

namespace CDP
{
    internal class Conexion : IDisposable
    {
        private ClientWebSocket? _browserWs;
        private readonly TaskCompletionSource _ready;
        private readonly SemaphoreSlim _sendLock;
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending;
        private long _nextId;

        private string? _targetId;
        private string? _sessionId;
        private int _windowId;

        public Task<string> TargetId => GetTargetId();
        public Task<string> SessionId => GetSessionId();
        public Task<int> WindowId => GetWindowId();

        public event Action<string, JsonElement>? EventReceived;

        public Conexion()
        {
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _sendLock = new(1, 1);
            _pending = new();
        }

        private async Task<string> GetTargetId()
        {
            await _ready.Task;
            return _targetId!;
        }

        private async Task<string> GetSessionId()
        {
            await _ready.Task;
            return _sessionId!;
        }

        private async Task<int> GetWindowId()
        {
            await _ready.Task;
            return _windowId;
        }

        public async Task Initialize(Uri uri, int debugPort)
        {
            try
            {
                // 1. Descubrir el WebSocket del navegador
                var browserWsUrl = await DiscoverBrowserWebSocketAsync(uri);

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

                // Escuchamos todo por aquí
                _ = ListenAsync(_browserWs);

                // 2. Descubrir el targetId de la pestaña (usando /json, como ya hacías)
                (var pageWsUrl, var targetId) = await GetWebSocketUrlAsync(uri, debugPort);

                if (string.IsNullOrEmpty(targetId))
                    throw new InvalidOperationException("The targetId was not found.");

                _targetId = targetId;

                // 3. CREAR LA SESIÓN PLANA (Attach)
                var attachResp = await SendAndWaitAsync("Target.attachToTarget", new
                {
                    targetId,
                    flatten = true // CLAVE: Usa sesiones planas
                });

                var sessionId = attachResp.GetProperty("result").GetProperty("sessionId").GetString();

                if (string.IsNullOrEmpty(sessionId))
                    throw new InvalidOperationException("The sessionId was not found.");

                _sessionId = sessionId;

                var winResp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId });

                if (!winResp.TryGetProperty("result", out var result) || !result.TryGetProperty("windowId", out var idProp))
                    throw new InvalidOperationException("Could not obtain the WindowId for the window.");

                _windowId = idProp.GetInt32();

                _ready.TrySetResult();
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
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

            var resp = await tcs.Task;

            if (resp.TryGetProperty("error", out var err))
                throw new Exception($"CDP Error: {err.GetProperty("message").GetString()}");

            return resp;
        }

        public async Task<JsonElement> SendToPageAsync(string method, object @params)
        {
            await _ready.Task;
            return await SendAndWaitAsync(method, @params, await SessionId);
        }

        public async Task<JsonElement> SendToBrowserAsync(string method, object @params)
        {
            await _ready.Task;
            return await SendAndWaitAsync(method, @params, null);
        }

        public static async Task<Conexion> NewInstance(Uri uri, int debugPort)
        {
            var conexion = new Conexion();
            await conexion.Initialize(uri, debugPort);
            return conexion;
        }

        private async Task ListenAsync(ClientWebSocket ws)
        {
            try
            {
                while (ws.State == WebSocketState.Open)
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
                    if (root.TryGetProperty("method", out var methodProp) && root.TryGetProperty("params", out var paramsEl))
                        EventReceived?.Invoke(methodProp.GetString()!, paramsEl.Clone());
                }
            }
            finally
            {
                foreach (var k in _pending.Keys.ToList())
                    if (_pending.TryRemove(k, out var tcs))
                        tcs.TrySetException(new InvalidOperationException("WebSocket closed."));

            }
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
                    Console.WriteLine($"Exception upon receipt: {ex.Message}");
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"Chromium closed the socket. Code: {result.CloseStatus}, Reason: {result.CloseStatusDescription}");
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                    return null;

                ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    return ms.ToArray();
            }
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

        private static async Task<(string?, string?)> GetWebSocketUrlAsync(Uri uri, int debugPort)
        {
            string? targetId = null;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"{uri.Scheme}://{uri.Host}:{debugPort}/json");
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

        public void Dispose()
        {
            _browserWs?.Dispose();
            _browserWs = null;
        }
    }
}