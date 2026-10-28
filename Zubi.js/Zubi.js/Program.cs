using Zubi.js.Bridge;
using Zubi.js.PluginManager;

namespace Zubi.js
{
    internal class Program
    {
        
        static void Main(string[] args)
        {
            BrowserLauncher.CleanupOrphanTempProfiles();
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            var config = Config.Load(Utils.GetFullDirectory("config.json", false));
            var port = Utils.FindFreePort(config.DebuggingPort);
            var localOrigin = $"http://127.0.0.1:{port}";
            var srcPath = App.BaseDirectory;

            var session = new SessionStore();
            var tracker = new ConnectionTracker(TimeSpan.FromSeconds(config.ShutdownGraceSeconds));
            var registry = new Plugins();
            var shutdownTcs = new TaskCompletionSource<bool>();

            tracker.ShutdownRequested += delegate
            {
                shutdownTcs.TrySetResult(true);
            };

            var server = new FileServer($"{localOrigin}/", srcPath, session);
            var bridge = new ZubiWebSocket(
                session,
                registry,
                tracker,
                new[] { localOrigin }.Concat(config.AllowedOrigins).ToArray());

            server.WebSocketHandler = bridge.HandleAsync;
            server.Start();

            var url = $"{localOrigin}/?token={session.MasterToken}";
            Console.WriteLine($"Servidor:    {localOrigin}");
            Console.WriteLine($"Master:      {session.MasterToken.Substring(0, 12)}…");
            Console.WriteLine($"Abriendo:    {url}");

            BrowserLauncher.Launch(url, ProfileMode.Shared);

            Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shutdownTcs.TrySetResult(true);
            };

            await shutdownTcs.Task;

            Console.WriteLine("Cerrando…");
            server.Stop();
            return 0;
        }
    }
}