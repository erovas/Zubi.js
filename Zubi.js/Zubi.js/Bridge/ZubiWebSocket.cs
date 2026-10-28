using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Zubi.js.PluginManager;

namespace Zubi.js.Bridge
{
    internal class ZubiWebSocket
    {
        private readonly SessionStore _session;
        private readonly Plugins _registry;
        private readonly ConnectionTracker _tracker;
        private readonly HashSet<string> _allowedOrigins;

        public ZubiWebSocket(SessionStore session, Plugins registry, ConnectionTracker tracker, string[] allowedOrigins)
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

                        var uid = uidEl.GetString() ?? "";
                        var method = mEl.GetString() ?? "";
                        var args = root.TryGetProperty("params", out var pEl)
                            && pEl.ValueKind == JsonValueKind.Array
                            ? pEl.EnumerateArray().Select(e => (object?)e.Clone()).ToArray()
                            : Array.Empty<object?>();

                        var cts = new CancellationTokenSource();
                        pending[id] = cts;
                        _ = ProcessCallAsync(ws, sendLock, pending, id, uid, method, args, cts.Token);
                    }
                }
            }
            finally
            {
                if (ws.State == WebSocketState.Open)
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default); }
                    catch { }

                try { ws.Dispose(); }
                catch { }

                foreach (var kv in pending)
                    try { kv.Value.Cancel(); kv.Value.Dispose(); }
                    catch { }
            }
        }

        private async Task ProcessCallAsync(
            WebSocket ws, SemaphoreSlim sendLock,
            ConcurrentDictionary<long, CancellationTokenSource> pending,
            long id, string uid, string method, object?[] args, CancellationToken ct)
        {
            await Task.Yield(); // Cede el hilo actual; la continuación va al thread pool

            try
            {
                object? result;

                if (uid.Equals("Plugins", StringComparison.OrdinalIgnoreCase))
                    result = Utils.Invoke(_registry, "Plugins", method, args, ct);
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
                var actual = ex is TargetInvocationException tie && tie.InnerException is not null
                    ? tie.InnerException : ex;

                var (code, msg) = actual switch
                {
                    RpcException rex => (rex.Code, rex.Message),
                    OperationCanceledException => ("EABORT", "Cancelado"),
                    _ => ("EINTERNAL", actual.Message)
                };
                await SendErrorAsync(ws, sendLock, id, code, msg);
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
    }
}