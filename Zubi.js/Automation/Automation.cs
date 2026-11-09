using System.Text.RegularExpressions;
using System.Net.WebSockets;
using System.Text.Json;
using Zubi.js;

namespace Automation
{
    public class Automation : Plugin
    {

        private IBrowserAutomation? _automation;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ConsoleLogEntry>? ConsoleLog;

        public Automation() 
        {
            if (App.Browser.DebugPort <= 0)
                throw new InvalidOperationException("DebugPort not configured in config.json.");

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // 1) Descubrir la URL del WS a partir de las primitivas de App.
                var wsUrl = await DiscoverDebugWebSocketUrlAsync() ?? throw new InvalidOperationException($"Could not discover the debug WebSocket for '{App.Browser.Name}'.");

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

                // 3) Elegir la implementación según el navegador detectado.
                switch (App.Browser.Engine)
                {
                    case BrowserEngine.Chromium:
                        var cdp = new CDP(ws);
                        await cdp.InitializeAsync();
                        _automation = cdp;
                        break;
                    case BrowserEngine.Gecko:
                        var bidi = new BiDi(ws);
                        await bidi.InitializeAsync();
                        _automation = bidi;
                        break;
                    case BrowserEngine.WebKit:
                        var wk = new WebKit(ws);
                        await wk.InitializeAsync();
                        _automation = wk;
                        break;
                    default:
                        ws.Dispose();
                        throw new NotSupportedException($"Unsupported browser: '{App.Browser.Name}'.");
                }

                // 4) Reenviar los logs del navegador al JS como evento.
                _automation.ConsoleLogAdded += entry => ConsoleLog?.Invoke(this, entry);

                _ready.TrySetResult();
            }
            catch (Exception ex)
            {
                _ready.TrySetException(ex);
            }
        }

        

        /// <summary>Zubi.Console.Evaluate(expr)</summary>
        public async Task<object?> Evaluate(string expression)
        {
            await _ready.Task;
            return await _automation!.EvaluateAsync(expression, previewDepth: 1);
        }

        /// <summary>Zubi.Console.GetProperties(expr)</summary>
        public async Task<object?> GetProperties(string expression)
        {
            await _ready.Task;
            return await _automation!.GetPropertiesAsync(expression, previewDepth: 2);
        }
        


        private static async Task<string?> DiscoverDebugWebSocketUrlAsync()
        {
            switch (App.Browser.Engine)
            {
                case BrowserEngine.Chromium:
                    return await DiscoverChromiumAsync();
                case BrowserEngine.Gecko:
                    return $"ws://{WebSocketUtils.Uri.Host}:{App.Browser.DebugPort}/session";
                case BrowserEngine.WebKit:
                    return await DiscoverWebKitAsync();
            }

            return null;
        }

        private static async Task<string?> DiscoverChromiumAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"{WebSocketUtils.Uri.Scheme}://{WebSocketUtils.Uri.Host}:{App.Browser.DebugPort}/json");
                using var doc = JsonDocument.Parse(json);

                // 1º preferencia: la pestaña cuya URL corresponde al puerto de la app.
                foreach (var tab in doc.RootElement.EnumerateArray())
                {
                    if (!tab.TryGetProperty("type", out var t) || t.GetString() != "page") 
                        continue;
                    
                    if (!tab.TryGetProperty("url", out var u)) 
                        continue;

                    if (u.GetString()?.Contains($":{WebSocketUtils.Uri.Port}") != true) 
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

        protected override void Dispose(bool disposing)
        {
            _automation?.Dispose();
            _automation = null;
        }

        private static async Task<string?> DiscoverWebKitAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                // WebKitGTK expone una página HTML con la lista de targets.
                var html = await http.GetStringAsync($"{WebSocketUtils.Uri.Scheme}://{WebSocketUtils.Uri.Host}:{App.Browser.DebugPort}/");

                // Enlaces con forma: href="/socket/{conn}/{target}/web-page"
                var match = Regex.Match(html, "href=\"(/socket/[^\"]*/web-page[^\"]*)\"", RegexOptions.IgnoreCase);

                if (match.Success)
                    return $"ws://{WebSocketUtils.Uri.Host}:{App.Browser.DebugPort}{match.Groups[1].Value}";
            }
            catch { /* devolvemos null */ }

            return null;
        }
    }
}