using CDP.Domains;
using Zubi.js;

namespace CDP
{
    public class CDP : Plugin
    {
        private readonly Conexion _con;
        
        public Runtime Runtime { get; }
        public Page Page { get; }
        public Input Input { get; }
        public Browser Browser { get; }

        public CDP() 
        {
            if (App.Browser.Engine != BrowserEngine.Chromium)
                throw new InvalidOperationException("The running browser is not based on Chromium.");

            if (App.Browser.DebugPort <= 0)
                throw new InvalidOperationException("DebugPort not configured in config.json.");

            _con = new Conexion();
            Runtime = new Runtime(_con);
            Page = new Page(_con);
            Input = new Input(_con);
            Browser = new Browser(_con);

            _ = InitializeAsync(new Uri(App.Browser.Href), App.Browser.DebugPort);
        }

        public async Task InitializeAsync(Uri uri, int debugPort)
        {
            await _con.Initialize(uri, debugPort);

            // Conectar eventos de CDP a eventos de los dominios
            _con.EventReceived += (method, paramsEl) =>
            {
                Runtime.OnEvent(method, paramsEl);
                Page.OnEvent(method, paramsEl);
                // ....
            };

            // Habilitar dominios
            await _con.SendToPageAsync("Runtime.enable", new { });
            await _con.SendToPageAsync("Page.enable", new { });
            //...
        }

        protected override void Dispose(bool disposing)
        {
            _con.Dispose();
        }

    }
}