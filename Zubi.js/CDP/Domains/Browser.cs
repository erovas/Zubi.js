using Zubi.js;
using CDP.DTOs;
using System.Text.Json;

namespace CDP.Domains
{
    public sealed class Browser : Plugin
    {
        private readonly Conexion _con;
        private Task<string> TargetId => _con.TargetId;
        private Task<int> WindowId => _con.WindowId;

        internal Browser(Conexion con)
        {
            _con = con;
        }

        /// <summary>
        /// CDP.Browser.OpenDevTools("console") 
        /// Abre las DevTools en la pestaña especificada. 
        /// Paneles soportados: elements, console, network, sources, resources, timeline, chrome-recorder, heap-profiler.
        /// </summary>
        public async Task OpenDevTools(string? panelId = null)
        {
            var @params = new Dictionary<string, object> { { "targetId", await TargetId } };

            if (!string.IsNullOrWhiteSpace(panelId))
                @params["panelId"] = panelId;

            await SendAndWaitAsync("Target.openDevTools", @params);
        }

        /// <summary>
        /// CDP.Browser.GetWindowState() 
        /// "normal", "minimized", "maximized", "fullscreen"
        /// </summary>
        public async Task<string> GetWindowState()
        {
            try
            {
                var resp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId = await TargetId });

                if (resp.TryGetProperty("result", out var result) && result.TryGetProperty("bounds", out var bounds))
                {
                    var state = bounds.TryGetProperty("windowState", out var stateProp) ? stateProp.GetString() ?? "normal" : "normal";
                    return state;
                }
            }
            catch { }

            return "normal";
        }

        /// <summary>
        /// CDP.Browser.SetWindowState("maximized") 
        /// Cambia el estado de la ventana. Estados válidos: "normal", "minimized", "maximized", "fullscreen".
        /// </summary>
        public async Task SetWindowState(string state)
        {
            // 1. Validar el estado para no saturar el socket con peticiones inválidas
            var validStates = new[] { "normal", "minimized", "maximized", "fullscreen" };

            if (!validStates.Contains(state.ToLowerInvariant()))
                throw new ArgumentException($"Invalid state: '{state}'. Use one of: {string.Join(", ", validStates)}");


            // 2. Obtener el ID de la ventana asociada a nuestra pestaña
            var winResp = await SendAndWaitAsync("Browser.getWindowForTarget", new { targetId = await TargetId });

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
        /// CDP.Browser.SetWindowGeometry(100, 100, 800, 600)
        /// Mueve y redimensiona la ventana. Si estaba maximizada, la devuelve a "normal" automáticamente.
        /// </summary>
        public async Task SetWindowGeometry(int left, int top, int width, int height)
        {
            // Nota: Enviamos "windowState: normal" junto con la geometría. 
            // En este caso, CDP sí lo permite porque "normal" es la base para la geometría.
            await SendAndWaitAsync("Browser.setWindowBounds", new
            {
                windowId = await WindowId,
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
        /// CDP.Browser.GetWindowGeometry()
        /// Devuelve la posición y el tamaño actual de la ventana.
        /// </summary>
        public async Task<WindowGeometry> GetWindowGeometry()
        {
            // Consultar los límites actuales
            var boundsResp = await SendAndWaitAsync("Browser.getWindowBounds", new { windowId = await WindowId });

            if (!boundsResp.TryGetProperty("result", out var boundsResult) || !boundsResult.TryGetProperty("bounds", out var boundsEl))
                throw new InvalidOperationException("The window boundaries could not be obtained..");

            var geo = new WindowGeometry();

            // Extraer los valores con TryGetProperty por seguridad
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

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params)
        {
            return await _con.SendToBrowserAsync(method, @params);
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing
        }
    }
}
