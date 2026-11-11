using System.Text.Json;
using Zubi.js;

namespace CDP.Domains
{
    public sealed class Runtime : Plugin
    {
        private readonly Conexion _con;

        public event EventHandler<object>? ConsoleMessage;
        public event EventHandler<object>? ExceptionThrown;

        internal Runtime(Conexion con) 
        {
            _con = con;
        }

        /// <summary>
        /// CDP.Runtime.Evaluate("1 + 1")
        /// </summary>
        public async Task<object?> Evaluate(string expression)
        {
            var resp = await SendAndWaitAsync("Runtime.evaluate", new
            {
                expression,
                generatePreview = true,
                returnByValue = false
            });
            return resp.GetProperty("result").GetProperty("result");
        }

        /// <summary>
        /// CDP.Runtime.GetProperties("miObjeto")
        /// </summary>
        public async Task<object?> GetProperties(string expression)
        {
            var evalResp = await SendAndWaitAsync("Runtime.evaluate", new { expression, generatePreview = true });
            var result = evalResp.GetProperty("result").GetProperty("result");

            if (!result.TryGetProperty("objectId", out var oid))
                return null;

            var propsResp = await SendAndWaitAsync("Runtime.getProperties", new
            {
                objectId = oid.GetString(),
                ownProperties = true,
                generatePreview = true
            });
            return propsResp.GetProperty("result");
        }

        internal void OnEvent(string method, JsonElement paramsEl)
        {
            if (method == "Runtime.consoleAPICalled")
                ConsoleMessage?.Invoke(this, paramsEl);

            else if (method == "Runtime.exceptionThrown")
                ExceptionThrown?.Invoke(this, paramsEl.Clone());
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
