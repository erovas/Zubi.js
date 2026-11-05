using System.IO;

namespace Zubi.js.Bridge
{
    internal sealed class LiveReloadWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _debounce;
        private readonly object _lock = new();
        private bool _pending;

        public LiveReloadWatcher(string rootPath)
        {
            _watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                Filter = "*.*" 
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;

            _debounce = new System.Timers.Timer(200) { AutoReset = false };
            _debounce.Elapsed += (_, _) =>
            {
                lock (_lock)
                {
                    if (!_pending) return;
                    _pending = false;
                }
                _ = WSocket.BroadcastReloadAsync();
            };

            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            lock (_lock) { _pending = true; }
            _debounce.Stop();
            _debounce.Start();
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounce.Dispose();
        }
    }
}