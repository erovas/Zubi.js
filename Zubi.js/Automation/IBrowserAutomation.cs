using System.Net.WebSockets;
using System.Text.Json;
using Zubi.js;

namespace Automation
{
    internal interface IBrowserAutomation : IDisposable
    {
        /// <summary>
        /// Evalúa una expresión en el contexto de la página.
        /// </summary>
        /// <param name="expression">La expresión JavaScript a evaluar.</param>
        /// <param name="previewDepth">Profundidad de serialización para el preview (1 = propiedades de primer nivel).</param>
        /// <returns>Un objeto que representa el resultado de la evaluación, listo para ser serializado a JSON.</returns>
        Task<object?> EvaluateAsync(string expression, int previewDepth = 1);

        /// <summary>
        /// Obtiene las propiedades de un objeto para expandirlo en la consola.
        /// </summary>
        /// <param name="expression">La expresión que referencia al objeto (ej. "myObject").</param>
        /// <param name="previewDepth">Profundidad de serialización para los valores de las propiedades.</param>
        Task<object?> GetPropertiesAsync(string expression, int previewDepth = 2);

        /// <summary>
        /// Se dispara cuando la página llama a un método de `console`.
        /// </summary>
        Action<ConsoleLogEntry>? ConsoleLogAdded { get; set; }
    }

    /// <summary>
    /// Representa una entrada de log capturada de la consola del navegador.
    /// </summary>
    public sealed class ConsoleLogEntry
    {
        public string Level { get; set; } = "log";  // "log", "info", "warn", "error", "debug"
        public string Text { get; set; } = string.Empty;
        public object[] Args { get; set; } = Array.Empty<object>();
    }

    internal static class WebSocketUtils
    {
        /// <summary>
        /// Recibe un mensaje completo, ensamblando fragmentos si es necesario.
        /// Devuelve null si el socket se cierra o llega un frame binario.
        /// </summary>
        public static async Task<byte[]?> ReceiveFullMessageAsync2(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using var ms = new MemoryStream();

            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct);
                }
                catch (WebSocketException) { return null; }
                catch (OperationCanceledException) { return null; }

                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text) return null;

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return ms.ToArray();
            }
        }

        public static async Task<byte[]?> ReceiveFullMessageAsync(WebSocket ws, CancellationToken ct)
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
                    Console.WriteLine($"[Zubi BiDi] Excepción al recibir: {ex.Message}");
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // ESTO ES LO IMPORTANTE:
                    Console.WriteLine($"[Zubi BiDi] Firefox CERRÓ el socket. Código: {result.CloseStatus}, Razón: {result.CloseStatusDescription}");
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text) return null;

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return ms.ToArray();
            }
        }

        /// <summary>
        /// Extrae args y texto legible de un array de RemoteObjects (CDP/WebKit).
        /// </summary>
        public static (object[] args, string text) ExtractArgs(JsonElement argsEl)
        {
            if (argsEl.ValueKind != JsonValueKind.Array)
                return (Array.Empty<object>(), "");

            var arr = argsEl.EnumerateArray().Select(x => (object)x.Clone()).ToArray();

            var text = string.Join(" ", argsEl.EnumerateArray().Select(x =>
            {
                if (x.TryGetProperty("description", out var d))
                    return d.GetString() ?? "";

                if (x.TryGetProperty("value", out var v))
                    return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
                
                return "";
            }));

            return (arr, text);
        }

        /// <summary>
        /// 
        /// </summary>
        public static readonly Uri Uri = new Uri(App.Browser.Href);
    }
}