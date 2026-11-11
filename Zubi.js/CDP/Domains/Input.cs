using Zubi.js;
using CDP.DTOs;
using System.Text.Json;

namespace CDP.Domains
{
    public sealed class Input : Plugin
    {
        private readonly Conexion _con;

        internal Input(Conexion con)
        {
            _con = con;
        }

        /// <summary>
        /// CDP.Input.Click(150, 300)
        /// - Simula un clic en las coordenadas (x, y)
        /// </summary>
        public async Task Click(int x, int y)
        {
            // Mover el cursor a la posición (necesario para :hover y algunos listeners)
            await SendAndWaitAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseMoved",
                x,
                y
            });

            // Presionar el botón izquierdo
            await SendAndWaitAsync("Input.dispatchMouseEvent", new
            {
                type = "mousePressed",
                x,
                y,
                button = "left",
                clickCount = 1
            });

            // Soltar el botón izquierdo
            await SendAndWaitAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseReleased",
                x,
                y,
                button = "left",
                clickCount = 1
            });
        }

        /// <summary>
        /// Zubi.CDP.ClickElement("#mi-boton") 
        /// - Busca el elemento y hace clic en su centro
        /// </summary>
        public async Task ClickElement(string selector)
        {

            // 1. Usamos JS para obtener las coordenadas del centro del elemento
            var js = $@"
                (() => {{
                    const el = document.querySelector({JsonSerializer.Serialize(selector)});
                    if (!el) return null;
                    const rect = el.getBoundingClientRect();
                    return {{ 
                        x: rect.left + rect.width / 2, 
                        y: rect.top + rect.height / 2 
                    }};
                }})()
            ";

            var resp = await SendAndWaitAsync("Runtime.evaluate", new
            {
                expression = js,
                generatePreview = true,
                returnByValue = false
            });

            var evalResp = resp.GetProperty("result").GetProperty("result");

            if (evalResp is JsonElement result && result.ValueKind != JsonValueKind.Null)
            {
                if (result.TryGetProperty("x", out var xProp) && result.TryGetProperty("y", out var yProp))
                {
                    int x = (int)xProp.GetDouble();
                    int y = (int)yProp.GetDouble();
                    await Click(x, y);
                    return;
                }
            }

            throw new InvalidOperationException($"The element was not found or is not visible: {selector}");
        }

        /// <summary>
        /// CDP.Input.DispatchMouseEvent("mouseMoved", 100, 200)
        /// </summary>
        public async Task DispatchMouseEvent(string type, double x, double y, string button = "none", int clickCount = 0, double deltaX = 0, double deltaY = 0)
        {
            var @params = new Dictionary<string, object>
            {
                { "type", type },
                { "x", x },
                { "y", y },
                { "button", button },
                { "clickCount", clickCount }
            };
            if (deltaX != 0 || deltaY != 0)
            {
                @params["deltaX"] = deltaX;
                @params["deltaY"] = deltaY;
            }

            await SendAndWaitAsync("Input.dispatchMouseEvent", @params);
        }

        /// <summary>
        /// CDP.Input.DispatchKeyEvent("keyDown", "a")
        /// </summary>
        public async Task DispatchKeyEvent(string type, string key, string? code = null, int windowsVirtualKeyCode = 0, string? text = null)
        {
            var @params = new Dictionary<string, object>
            {
                { "type", type },
                { "key", key },
                { "windowsVirtualKeyCode", windowsVirtualKeyCode }
            };
            if (!string.IsNullOrEmpty(code)) @params["code"] = code;
            if (!string.IsNullOrEmpty(text)) @params["text"] = text;

            await SendAndWaitAsync("Input.dispatchKeyEvent", @params);
        }

        /// <summary>
        /// const points = [ { X: 10.5, Y: 20.3 }, { X: 40.0, Y: 50.0, RadiusX: 2.5, RadiusY: 2.5, Force: 0.8, RotationAngle: 15 }] 
        /// CDP.Input.DispatchTouchEvent("touchStart", points)
        /// </summary>
        public async Task DispatchTouchEvent(string type, TouchPoint[] touchPoints)
        {
            await SendAndWaitAsync("Input.dispatchTouchEvent", new
            {
                type,
                touchPoints
            });
        }

        /// <summary>
        /// CDP.Input.InsertText("Hola mundo")
        /// </summary>
        public async Task InsertText(string text)
        {
            await SendAndWaitAsync("Input.insertText", new { text });
        }

        /// <summary>
        /// CDP.Input.SynthesizeScrollGesture(500, 500, yDistance: -300)
        /// </summary>
        public async Task SynthesizeScrollGesture(double x, double y, double? xDistance = null, double? yDistance = null, int speed = 800)
        {
            var @params = new Dictionary<string, object>
            {
                { "x", x },
                { "y", y },
                { "speed", speed }
            };
            if (xDistance.HasValue) @params["xDistance"] = xDistance.Value;
            if (yDistance.HasValue) @params["yDistance"] = yDistance.Value;

            await SendAndWaitAsync("Input.synthesizeScrollGesture", @params);
        }

        /// <summary>
        /// CDP.Input.SynthesizeTapGesture(300, 400, tapCount: 2)
        /// </summary>
        public async Task SynthesizeTapGesture(double x, double y, int tapCount = 1, int duration = 50)
        {
            await SendAndWaitAsync("Input.synthesizeTapGesture", new
            {
                x,
                y,
                tapCount,
                duration
            });
        }

        /// <summary>
        /// CDP.Input.SynthesizePinchGesture(400, 300, 2.0)
        /// </summary>
        public async Task SynthesizePinchGesture(double x, double y, double scaleFactor, int relativeSpeed = 800)
        {
            await SendAndWaitAsync("Input.synthesizePinchGesture", new
            {
                x,
                y,
                scaleFactor,
                relativeSpeed
            });
        }

        /// <summary>
        /// CDP.Input.SetInterceptDrags(true)
        /// </summary>
        public async Task SetInterceptDrags(bool enabled)
        {
            await SendAndWaitAsync("Input.setInterceptDrags", new { enabled });
        }

        /// <summary>
        /// CDP.Input.DispatchDragEvent("dragEnter", 100, 200, dragData)
        /// </summary>
        public async Task DispatchDragEvent(string type, double x, double y, JsonElement dragData)
        {
            await SendAndWaitAsync("Input.dispatchDragEvent", new
            {
                type,
                x,
                y,
                data = dragData
            });
        }

        private async Task<JsonElement> SendAndWaitAsync(string method, object @params)
        {
            return await _con.SendToPageAsync(method, @params);
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing
        }
    }
}