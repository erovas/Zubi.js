using System.Net;
using System.Text;
using Zubi.js.Helpers;
using System.Text.Json;
using System.Net.WebSockets;
using Zubi.js.PluginManager;
using System.Collections.Concurrent;

namespace Zubi.js.Bridge
{
    internal class WSocket
    {
        private readonly SessionStore _session;
        private readonly Plugins _registry;
        private readonly ConnectionTracker _tracker;
        private readonly HashSet<string> _allowedOrigins;

        public WSocket(SessionStore session, Plugins registry, ConnectionTracker tracker, string[] allowedOrigins)
        {
            _session = session;
            _registry = registry;
            _tracker = tracker;
            _allowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
        }

        public async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var origin = req.Headers["Origin"];

            // Comprobar que la petición es de un origen permitido
            if (origin is null || !_allowedOrigins.Contains(origin))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            // Comprobar que el token es valido
            if (!_session.IsValidTabToken(req.QueryString["token"]))
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Close();
                return;
            }

            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsCtx.WebSocket;

            _tracker.OnConnected();
            try { await ServeAsync(ws); }
            finally { _tracker.OnDisconnected(); }
        }

        private async Task ServeAsync(WebSocket ws)
        {
            var sendLock = new SemaphoreSlim(1, 1);
            var pending = new ConcurrentDictionary<long, CancellationTokenSource>();
            var subscriptions = new Dictionary<string, IDisposable>(StringComparer.Ordinal);
            var subscriptionsLock = new object();
            var buffer = new byte[16 * 1024];

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    byte[]? message;

                    try { message = await ReceiveFullAsync(ws, buffer); }
                    catch (WebSocketException) { break; }
                    catch (OperationCanceledException) { break; }

                    if (message is null) 
                        break;

                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(message); }
                    catch { continue; }

                    using (doc)
                    {
                        var root = doc.RootElement;

                        if (root.TryGetProperty("cancel", out var c) && c.ValueKind == JsonValueKind.True)
                        {
                            if (root.TryGetProperty("id", out var ci) && ci.TryGetInt64(out var cid) && pending.TryRemove(cid, out var icts))
                                try
                                {
                                    icts.Cancel();
                                    icts.Dispose();
                                }
                                catch { }

                            continue;
                        }

                        if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id))
                            continue;

                        if (!root.TryGetProperty("uid", out var uidEl))
                            continue;

                        if (!root.TryGetProperty("method", out var mEl))
                        {
                            await SendErrorAsync(ws, sendLock, id, "EINVAL", "Falta 'method'");
                            continue;
                        }

                        var uid = uidEl.GetString() ?? string.Empty;
                        var method = mEl.GetString() ?? string.Empty;
                        var args = root.TryGetProperty("params", out var pEl)
                            && pEl.ValueKind == JsonValueKind.Array
                            ? pEl.EnumerateArray().Select(e => (object?)e.Clone()).ToArray()
                            : Array.Empty<object?>();

                        var cts = new CancellationTokenSource();
                        pending[id] = cts;
                        _ = ProcessCallAsync(
                                ws, sendLock, pending, 
                                subscriptions, subscriptionsLock, 
                                id, uid, method, args, cts.Token);
                    }
                }
            }
            finally
            {
                // Quitar los handlers de eventos en C#
                lock (subscriptionsLock)
                {
                    foreach (var sub in subscriptions.Values)
                        try { sub.Dispose(); } 
                        catch { }
                    
                    subscriptions.Clear();
                }

                // Cierre socket
                if (ws.State == WebSocketState.Open)
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, default); }
                    catch { }

                try { ws.Dispose(); }
                catch { }

                // Cancelar operaciones pendientes
                foreach (var kv in pending)
                    try { kv.Value.Cancel(); kv.Value.Dispose(); }
                    catch { }
            }
        }

        private async Task ProcessCallAsync(
            WebSocket ws, SemaphoreSlim sendLock,
            ConcurrentDictionary<long, CancellationTokenSource> pending,
            Dictionary<string, IDisposable> subscriptions, object subscriptionsLock,
            long id, string uid, string method, object?[] args, CancellationToken ct)
        {
            await Task.Yield(); // Cede el hilo actual; la continuación va al thread pool

            try
            {
                if (uid.Equals(Utils.PLUGINS, StringComparison.OrdinalIgnoreCase))
                {
                    if (method.Equals("SubscribeEvents", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleSubscribeAsync(ws, sendLock, subscriptions, subscriptionsLock, id, args, ct);
                        return;
                    }
                    if (method.Equals("UnsubscribeEvents", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleUnsubscribeAsync(ws, sendLock, subscriptions, subscriptionsLock, id, args, ct);
                        return;
                    }
                }

                object? result;

                if (uid.Equals(Utils.PLUGINS, StringComparison.OrdinalIgnoreCase))
                    result = Utils.Invoke(_registry, Utils.PLUGINS, method, args, ct);
                else
                    result = _registry.Invoke(uid, method, args, ct);

                if (result is Task task)
                {
                    await task.WaitAsync(ct);
                    result = task.GetType().IsGenericType
                        ? task.GetType().GetProperty("Result")?.GetValue(task)
                        : null;
                }

                if (result is IAsyncEnumerable<byte[]> stream)
                {
                    long total = 0;
                    await foreach (var chunk in stream.WithCancellation(ct))
                    {
                        await SendJsonAsync(ws, sendLock,
                            new { id, chunk = Convert.ToBase64String(chunk) }, ct);
                        total += chunk.Length;
                    }
                    await SendJsonAsync(ws, sendLock, new { id, end = true, result = total }, ct);
                    return;
                }

                await SendJsonAsync(ws, sendLock, new { id, result }, ct);
            }
            catch (Exception ex)
            {
                var rpc = CodeException.FromException(ex);
                await SendErrorAsync(ws, sendLock, id, rpc.Code, rpc.Message);
            }
            finally
            {
                if (pending.TryRemove(id, out var cts))
                    try { cts.Dispose(); } catch { }
            }
        }

        private static async Task<byte[]?> ReceiveFullAsync(WebSocket ws, byte[] buffer)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), default);
                
                if (result.MessageType == WebSocketMessageType.Close) 
                    return null;
                
                ms.Write(buffer, 0, result.Count);
            } 
            while (!result.EndOfMessage);
            return ms.ToArray();
        }

        private static Task SendJsonAsync(WebSocket ws, SemaphoreSlim sendLock, object payload, CancellationToken ct = default)
        {
            return SendRawAsync(ws, sendLock, Utils.JsonSerialize(payload), ct);
        }

        private static Task SendErrorAsync(WebSocket ws, SemaphoreSlim sendLock, long id, string code, string message)
        {
            return SendJsonAsync(ws, sendLock, new { id, error = new { code, message } });
        }

        private static async Task SendRawAsync(WebSocket ws, SemaphoreSlim sendLock, string json, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync(ct);
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { sendLock.Release(); }
        }

        // ─────────────────────────────────────────────────────────────
        //  SubscribeEvents:  params = [ "pluginUid", "eventName" ]
        // ─────────────────────────────────────────────────────────────
        private async Task HandleSubscribeAsync(
            WebSocket ws, SemaphoreSlim sendLock,
            Dictionary<string, IDisposable> subscriptions, object subscriptionsLock,
            long id, object?[] args, CancellationToken ct)
        {
            var pluginUid = args.Length > 0 ? ExtractString(args[0]) : null;
            var eventName = args.Length > 1 ? ExtractString(args[1]) : null;

            if (string.IsNullOrEmpty(pluginUid) || string.IsNullOrEmpty(eventName))
                throw new InvalidOperationException("Se requiere [pluginUid, eventName]");

            var key = $"{pluginUid}:{eventName}";

            // El callback se ejecuta desde el hilo donde el plugin dispare el evento.
            // SendJsonAsync serializa con sendLock → seguro desde cualquier hilo.
            var newSub = _registry.SubscribeEvent(pluginUid, eventName, data =>
            {
                _ = SendJsonAsync(ws, sendLock, new
                {
                    @event = eventName,
                    uid = pluginUid,
                    data
                });
            });

            // Si mientras creábamos la suscripción la conexión murió, descartarla.
            if (ws.State != WebSocketState.Open)
            {
                try { newSub.Dispose(); } catch { }
                throw new CodeException("EABORT", "Conexión cerrada");
            }

            lock (subscriptionsLock)
            {
                if (subscriptions.TryGetValue(key, out var old))
                    try { old.Dispose(); } catch { }
                subscriptions[key] = newSub;
            }

            await SendJsonAsync(ws, sendLock, new { id, result = true }, ct);
        }

        // ─────────────────────────────────────────────────────────────
        //  UnsubscribeEvents:  params = [ "pluginUid", "eventName" ]
        // ─────────────────────────────────────────────────────────────
        private async Task HandleUnsubscribeAsync(
            WebSocket ws, SemaphoreSlim sendLock,
            Dictionary<string, IDisposable> subscriptions, object subscriptionsLock,
            long id, object?[] args, CancellationToken ct)
        {
            var pluginUid = args.Length > 0 ? ExtractString(args[0]) : null;
            var eventName = args.Length > 1 ? ExtractString(args[1]) : null;

            if (string.IsNullOrEmpty(pluginUid) || string.IsNullOrEmpty(eventName))
                throw new InvalidOperationException("Se requiere [pluginUid, eventName]");

            var key = $"{pluginUid}:{eventName}";

            lock (subscriptionsLock)
            {
                if (subscriptions.TryGetValue(key, out var sub))
                {
                    try { sub.Dispose(); } 
                    catch { }
                    subscriptions.Remove(key);
                }
            }

            await SendJsonAsync(ws, sendLock, new { id, result = true }, ct);
        }

        // Extrae un string de un argumento que puede venir como JsonElement o string.
        private static string? ExtractString(object? arg) => arg switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => null
        };

    }
}