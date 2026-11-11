using System.Text.Json;
using CDP.DTOs;
using Zubi.js;

namespace CDP.Domains
{
    public sealed class Page : Plugin
    {
        private readonly Conexion _con;

        public event EventHandler<JavaScriptDialogEventArgs>? JavaScriptDialogOpening;
        public event EventHandler<JavaScriptDialogClosedEventArgs>? JavaScriptDialogClosed;
        public event EventHandler<string>? FrameNavigated;
        public event EventHandler<string>? WindowOpen;

        internal Page(Conexion con)
        {
            _con = con;
        }

        /// <summary>
        /// CDP.Page.Navigate("https://google.com")
        /// </summary>
        public async Task Navigate(string url)
        {
            await SendAndWaitAsync("Page.navigate", new { url });
        }

        /// <summary>
        /// CDP.Page.Reload()
        /// </summary>
        public async Task Reload()
        {
            await SendAndWaitAsync("Page.reload", new { ignoreCache = false });
        }

        /// <summary>
        /// Zubi.CDP.TakeScreenshot("jpeg", 80) 
        /// - Devuelve un string en Base64
        /// </summary>
        public async Task<string> TakeScreenshot(string format = "png", int? quality = null)
        {
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
                @params["quality"] = quality.Value;
            
            var resp = await SendAndWaitAsync("Page.captureScreenshot", @params);
            return resp.GetProperty("result").GetProperty("data").GetString() ?? "";
        }

        /// <summary>
        /// CDP.Page.PrintToPDF(true) 
        /// - Devuelve Base64 del PDF
        /// </summary>
        public async Task<string> PrintToPDF(bool printBackground = true, bool landscape = false)
        {
            var resp = await SendAndWaitAsync("Page.printToPDF", new
            {
                printBackground,
                landscape
            });
            return resp.GetProperty("result").GetProperty("data").GetString() ?? "";
        }

        /// <summary>
        /// CDP.Page.AddScriptToEvaluateOnNewDocument("console.log('Hola')")
        /// </summary>
        public async Task AddScriptToEvaluateOnNewDocument(string source)
        {
            await SendAndWaitAsync("Page.addScriptToEvaluateOnNewDocument", new { source });
        }

        /// <summary>
        /// CDP.Page.BringToFront()
        /// </summary>
        public async Task BringToFront()
        {
            await SendAndWaitAsync("Page.bringToFront", new { });
        }

        /// <summary>
        /// CDP.Page.Close()
        /// </summary>
        public async Task Close()
        {
            await SendAndWaitAsync("Page.close", new { });
        }

        /// <summary>
        /// CDP.Page.HandleJavaScriptDialog(true, "Aceptar")
        /// </summary>
        public async Task HandleJavaScriptDialog(bool accept, string? promptText = null)
        {
            var @params = new Dictionary<string, object> { { "accept", accept } };
            if (promptText != null) @params["promptText"] = promptText;

            await SendAndWaitAsync("Page.handleJavaScriptDialog", @params);
        }

        internal void OnEvent(string method, JsonElement paramsEl)
        {
            switch (method)
            {
                case "Page.javascriptDialogOpening":
                    var dialogInfo = new JavaScriptDialogEventArgs
                    {
                        Type = paramsEl.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Message = paramsEl.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                        DefaultPrompt = paramsEl.TryGetProperty("defaultPrompt", out var dp) ? dp.GetString() : null
                    };
                    JavaScriptDialogOpening?.Invoke(this, dialogInfo);
                    break;

                case "Page.javascriptDialogClosed":
                    var closedInfo = new JavaScriptDialogClosedEventArgs
                    {
                        Result = paramsEl.TryGetProperty("result", out var r) && r.GetBoolean(),
                        UserInput = paramsEl.TryGetProperty("userInput", out var u) ? u.GetString() ?? "" : ""
                    };
                    JavaScriptDialogClosed?.Invoke(this, closedInfo);
                    break;

                case "Page.frameNavigated":
                    if (paramsEl.TryGetProperty("frame", out var frame) && frame.TryGetProperty("url", out var urlProp))
                        FrameNavigated?.Invoke(this, urlProp.GetString() ?? "");
                    
                    break;

                case "Page.windowOpen":
                    if (paramsEl.TryGetProperty("url", out var openUrl))
                        WindowOpen?.Invoke(this, openUrl.GetString() ?? "");
                    
                    break;
            }
        }

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params)
        {
            return await _con.SendToPageAsync(method, @params);
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing.
        }
    }
}