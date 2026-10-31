using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Zubi.js
{
    public static class App
    {
        /// <summary>
        /// 
        /// </summary>
        public const string Name = "Zubi.js";

        /// <summary>
        /// 
        /// </summary>
        public static readonly string BaseDirectory;

        /// <summary>
        /// 
        /// </summary>
        public static readonly ConcurrentDictionary<string, object?> Storage;

        /// <summary>
        /// 
        /// </summary>
        public static readonly Platform Platform;

        /// <summary>
        /// 
        /// </summary>
        public static readonly bool IsDebugging;

        /// <summary>
        /// 
        /// </summary>
        public static BrowserData Browser { get; internal set; }

        static App()
        {
            BaseDirectory = AppContext.BaseDirectory;
            Storage = new();

            var platform = Platform.Windows;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                platform = Platform.FreeBSD;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                platform = Platform.Linux;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                platform = Platform.OSX;

            Platform = platform;
            IsDebugging = System.Diagnostics.Debugger.IsAttached;
            Browser = new BrowserData(BrowserEngine.Unknown.ToString(), BrowserEngine.Unknown, string.Empty);
        }
    }
}
