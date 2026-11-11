using Zubi.js;
using MyApp = Zubi.js.App;

namespace Essentials
{
    public sealed class App : Plugin
    {
        public string Name { get; }
        public string BaseDirectory { get; }
        public AppStorage Storage { get; }
        public Platform Platform { get; }
        public bool IsDebugging { get; }
        public BrowserData Browser { get; }

        public App()
        {
            Name = MyApp.Name;
            BaseDirectory = MyApp.BaseDirectory;
            Storage = new(MyApp.Storage);
            Platform = MyApp.Platform;
            IsDebugging = MyApp.IsDebugging;
            Browser = MyApp.Browser;
        }

        public void Close()
        {
            MyApp.Close();
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing
        }
    }
}