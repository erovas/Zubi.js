using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace Automation
{
    internal sealed class WebKit : IBrowserAutomation
    {
        private readonly ClientWebSocket _ws;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
        private int _nextId;

        public Action<ConsoleLogEntry>? ConsoleLogAdded { get; set; }

        public WebKit(ClientWebSocket ws) => _ws = ws;

        public async Task InitializeAsync()
        {
            _ = ListenAsync();
            await SendAndWaitAsync("Runtime.enable", new { });
            await SendAndWaitAsync("Console.enable", new { });
        }

        public async Task<object?> EvaluateAsync(string expression, int previewDepth = 1)
        {
            var resp = await SendAndWaitAsync("Runtime.evaluate", new
            {
                expression,
                generatePreview = true,
                returnByValue = false
            });

            return resp.GetProperty("result").GetProperty("result");
        }

        public async Task<object?> GetPropertiesAsync(string expression, int previewDepth = 2)
        {
            var eval = await EvaluateAsync(expression, previewDepth);
            if (eval is not JsonElement obj) 
                return null;
            
            if (!obj.TryGetProperty("objectId", out var oid)) 
                return null;

            var resp = await SendAndWaitAsync("Runtime.getProperties", new
            {
                objectId = oid.GetString(),
                ownProperties = true,
                generatePreview = true
            });

            return resp.GetProperty("result");
        }

        private async Task ListenAsync()
        {
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var payload = await WebSocketUtils.ReceiveFullMessageAsync(_ws, CancellationToken.None);
                    
                    if (payload is null) 
                        break;

                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    // Respuesta a un comando pendiente
                    if (root.TryGetProperty("id", out var idEl))
                    {
                        var id = idEl.GetInt32();
                        if (_pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(root.Clone());
                        continue;
                    }

                    // Evento de consola (en WebKit es Console.messageAdded)
                    if (root.TryGetProperty("method", out var m) && m.GetString() == "Console.messageAdded")
                    {
                        var msg = root.GetProperty("params").GetProperty("message");
                        var level = msg.TryGetProperty("level", out var l) ? NormalizeLevel(l.GetString()) : "log";
                        var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

                        object[] args = Array.Empty<object>();
                        if (msg.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Array)
                            args = p.EnumerateArray().Select(x => (object)x.Clone()).ToArray();
                        
                        ConsoleLogAdded?.Invoke(new ConsoleLogEntry
                        {
                            Level = level,
                            Text = text,
                            Args = args
                        });
                    }
                }
            }
            finally
            {
                foreach (var k in _pending.Keys.ToList())
                    if (_pending.TryRemove(k, out var tcs))
                        tcs.TrySetException(new InvalidOperationException("WebSocket cerrado."));
            }
        }

        private static string NormalizeLevel(string? level) => level switch
        {
            "warning" => "warn",
            "info" => "info",
            "error" => "error",
            "debug" => "debug",
            _ => "log"
        };

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params)
        {
            var id = Interlocked.Increment(ref _nextId);
            var json = JsonSerializer.Serialize(new { id, method, @params });
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            await _sendLock.WaitAsync();
            try
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }
            finally { _sendLock.Release(); }

            return await tcs.Task;
        }

        public void Dispose()
        {
            ConsoleLogAdded = null;
            _ws.Dispose();
        }
    }
}