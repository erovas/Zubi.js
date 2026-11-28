using Zubi.js.Bridge;
using Zubi.js.Helpers;
using Zubi.js.PluginManager;

namespace Zubi.js
{
    internal class Program
    {
        
        private static void Main(string[] args)
        {
            BrowserLauncher.CleanupOrphanTempProfiles();
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            var config = Config.Load(Path.Combine(App.BaseDirectory, "config.json"));
            var configBrowser = config.Browser;
            var port = Utils.FindFreePort(configBrowser.DebugPort);
            var localOrigin = Utils.GetUrlPort(port);
            var srcPath = Utils.ResolveDirectory(configBrowser.Directory);
            var plugins = new Plugins(config.Plugins);
            var session = new SessionStore();
            var tracker = new ConnectionTracker(config.ShutdownSeconds);
            var shutdownTcs = new TaskCompletionSource<bool>();

            tracker.ShutdownRequested += delegate
            {
                shutdownTcs.TrySetResult(true);
            };

            var server = new FileServer($"{localOrigin}/", srcPath, session, plugins);
            var bridge = new WSocket(
                session,
                plugins,
                tracker,
                new[] { localOrigin }.Concat(config.AllowedOrigins).ToArray());

            server.WebSocketHandler = bridge.HandleAsync;

            LiveReloadWatcher? liveReload = null;

            if (configBrowser.LiveReload)
            {
                liveReload = new LiveReloadWatcher(App.BaseDirectory);
                Console.WriteLine("LiveReload activado.");
            }

            server.Start();

            var url = $"{localOrigin}/?token={session.MasterToken}";
            Console.WriteLine($"Servidor:    {localOrigin}");
            Console.WriteLine($"Master:      {session.MasterToken.Substring(0, 12)}…");
            Console.WriteLine($"Abriendo:    {url}");

            BrowserLauncher.Launch(url, configBrowser);

            Console.WriteLine("Debug port ready");

            Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shutdownTcs.TrySetResult(true);
            };

            await shutdownTcs.Task;

            Console.WriteLine("Cerrando…");
            liveReload?.Dispose();
            server.Stop();
            return 0;
        }
    }
}