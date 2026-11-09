using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Zubi.js;

namespace Automation
{
    internal sealed class BiDi : IBrowserAutomation
    {
        private readonly ClientWebSocket _ws;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
        private long _nextId; // Nuevo contador para generar IDs numéricos
        private string? _contextId;

        public Action<ConsoleLogEntry>? ConsoleLogAdded { get; set; }

        public BiDi(ClientWebSocket ws)
        {
            _ws = ws;
        }

        public async Task InitializeAsync()
        {
            _ = ListenAsync();

            bool sessionCreated = false;
            int retries = 0;

            while (!sessionCreated && retries < 3)
            {
                try
                {
                    await SendAndWaitAsync("session.new", new
                    {
                        capabilities = new { alwaysMatch = new { } }
                    });
                    sessionCreated = true;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Maximum number of active sessions"))
                {
                    // Si falla por límite de sesiones, esperamos 500ms y reintentamos
                    retries++;
                    await Task.Delay(500);
                }
                catch (InvalidOperationException ex)
                {
                    // Si es otro error (como JSON inválido), abortamos de inmediato
                    throw new InvalidOperationException("No se pudo crear la sesión BiDi. " + ex.Message, ex);
                }
            }

            if (!sessionCreated)
            {
                throw new InvalidOperationException("No se pudo crear la sesión BiDi después de varios intentos (posible sesión zombie en Firefox).");
            }

            var tree = await SendAndWaitAsync("browsingContext.getTree", new { maxDepth = 1 });

            if (!tree.TryGetProperty("result", out var result) || !result.TryGetProperty("contexts", out var contexts))
                throw new InvalidOperationException("BiDi: Respuesta inesperada de 'browsingContext.getTree'.");

            if (contexts.GetArrayLength() == 0)
                throw new InvalidOperationException("BiDi: No se encontró ninguna pestaña.");

            // Buscar la pestaña que coincida con el puerto de tu app
            string? targetContextId = null;
            foreach (var ctx in contexts.EnumerateArray())
            {
                if (ctx.TryGetProperty("context", out var idProp))
                {
                    var id = idProp.GetString();
                    if (ctx.TryGetProperty("url", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (url != null && url.Contains($":{WebSocketUtils.Uri.Port}"))
                        {
                            targetContextId = id;
                            break;
                        }
                    }
                    // Fallback: guardar el primer contexto válido que encontremos
                    if (targetContextId == null) targetContextId = id;
                }
            }

            _contextId = targetContextId ?? throw new InvalidOperationException("BiDi: No se pudo determinar el contexto.");

            // Suscripción SIN el filtro `contexts` para evitar que Firefox cierre el socket
            await SendAndWaitAsync("session.subscribe", new
            {
                events = new[] { "log.entryAdded" }
            });

        }

        public async Task<object?> EvaluateAsync(string expression, int previewDepth = 1)
        {
            if (_contextId is null)
                throw new InvalidOperationException("BiDi: no inicializado.");

            var resp = await SendAndWaitAsync("script.evaluate", new
            {
                expression,
                target = new { context = _contextId },
                awaitPromise = false,
                serializationOptions = new
                {
                    maxObjectDepth = previewDepth,
                    maxDomDepth = 0
                }
            });

            var r = resp.GetProperty("result");
            var kind = r.TryGetProperty("type", out var k) ? k.GetString() : null;

            if (kind == "success")
                return r.GetProperty("result");

            if (kind == "exception")
            {
                var details = r.GetProperty("exceptionDetails");
                var text = details.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "Error";
                return JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    subtype = "error",
                    description = text
                });
            }

            return null;
        }

        public Task<object?> GetPropertiesAsync(string expression, int previewDepth = 2)
        {
            // BiDi no tiene "getProperties": reevaluamos con mayor profundidad.
            return EvaluateAsync(expression, previewDepth + 1);
        }

        private async Task ListenAsync()
        {
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var payload = await WebSocketUtils.ReceiveFullMessageAsync(_ws, CancellationToken.None);
                    if (payload is null) break;

                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;

                    // 1) ¿Es una respuesta a un comando pendiente? (Ej. session.new)
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    {
                        var id = idEl.GetInt64();
                        if (_pending.TryRemove(id, out var tcs))
                            tcs.TrySetResult(root.Clone());
                        continue;
                    }

                    // 2) ¿Es el evento de log?
                    if (root.TryGetProperty("method", out var m) && m.GetString() == "log.entryAdded")
                    {
                        // Usamos TryGetProperty para no explotar si falta "params" o "entry"
                        if (!root.TryGetProperty("params", out var paramsEl)) continue;
                        if (!paramsEl.TryGetProperty("entry", out var entry)) continue;

                        // Verificar que pertenezca a nuestra pestaña
                        if (entry.TryGetProperty("source", out var source) &&
                            source.TryGetProperty("context", out var ctxProp))
                        {
                            var ctx = ctxProp.GetString();
                            if (ctx != _contextId) continue;
                        }

                        var level = entry.TryGetProperty("level", out var l)
                            ? NormalizeLevel(l.GetString())
                            : "log";

                        var text = entry.TryGetProperty("text", out var txt)
                            ? txt.GetString() ?? ""
                            : "";

                        object[] args = Array.Empty<object>();
                        if (entry.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                        {
                            args = a.EnumerateArray().Select(x => (object)x.Clone()).ToArray();
                        }

                        ConsoleLogAdded?.Invoke(new ConsoleLogEntry
                        {
                            Level = level,
                            Text = text,
                            Args = args
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zubi BiDi] Excepción en ListenAsync: {ex.Message}");
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
            "warn" => "warn",
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


            //return await tcs.Task;

            var resp = await tcs.Task;

            // AÑADIDO: Verificamos si WebDriver BiDi devolvió un error
            if (resp.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "error")
            {
                var error = resp.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
                var message = resp.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "No details";
                throw new InvalidOperationException($"BiDi Error en '{method}': {error} - {message}");
            }

            return resp;
        }

        public void Dispose()
        {
            if (_ws is { State: WebSocketState.Open })
            {
                try
                {
                    // Enviamos session.end de forma síncrona para asegurar que se envíe antes de cerrar
                    var id = Interlocked.Increment(ref _nextId);
                    var json = JsonSerializer.Serialize(new { id, method = "session.end", @params = new { } });
                    var bytes = Encoding.UTF8.GetBytes(json);

                    // Damos un margen máximo de 250ms para que se envíe el paquete
                    _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                       .Wait(250);
                }
                catch { /* Ignorar errores al cerrar */ }
            }

            ConsoleLogAdded = null;
            _ws.Dispose();
        }
    }

}