using System.Collections.Concurrent;
using Zubi.js;

namespace Essentials
{
    public sealed class FileWatcher : Plugin
    {
        
        public event EventHandler<FileWatcherStateEventArgs>? Started;
        public event EventHandler<FileWatcherStateEventArgs>? Stopped;
        public event EventHandler<FileChangedEventArgs>? Created;
        public event EventHandler<FileChangedEventArgs>? Changed;
        public event EventHandler<FileChangedEventArgs>? Deleted;
        public event EventHandler<FileRenamedEventArgs>? Renamed;
        public event EventHandler<FileWatcherErrorEventArgs>? Error;

        
        private FileSystemWatcher? _watcher;
        private readonly Timer _debounceTimer;
        private readonly ConcurrentDictionary<string, byte> _pending = new();
        private int _debounceMs = 150;
        private string _currentPath = "";
        private string _currentFilter = "*.*";
        private bool _includeSubdirectories = false;

        public FileWatcher()
        {
            _debounceTimer = new Timer(_ => FlushPending(), null,
                Timeout.Infinite, Timeout.Infinite);
        }

        public bool IsRunning => _watcher?.EnableRaisingEvents == true;
        
        public string Path
        {
            get => _currentPath;
            set 
            {
                ThrowIfRunning();
                _currentPath = value; 
            }
        }

        public string Filter
        {
            get => _currentFilter;
            set 
            {  
                ThrowIfRunning();
                _currentFilter = value;
            }
        }

        public bool IncludeSubdirectories 
        { 
            get => _includeSubdirectories;
            set 
            { 
                ThrowIfRunning();
                _includeSubdirectories = value;
            }
        }


        public void Start(string? path = null, bool? includeSubdirectories = null, string? filter = null)
        {
            ThrowIfRunning();

            path ??= _currentPath;

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);

            filter ??= _currentFilter;
            includeSubdirectories ??= _includeSubdirectories;

            _currentPath = path;
            _currentFilter = filter;
            _includeSubdirectories = includeSubdirectories.Value;

            _watcher = new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = includeSubdirectories.Value,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                // Buffer grande: evita InternalBufferOverflow en directorios movidos
                InternalBufferSize = 64 * 1024,
            };

            _watcher.Created += OnCreated;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;

            Started?.Invoke(this, new FileWatcherStateEventArgs
            {
                Path = path,
                IncludeSubdirectories = includeSubdirectories.Value,
                Filter = filter,
            });
        }

        public void Stop()
        {
            if (_watcher is null)
                return;

            try { _watcher.EnableRaisingEvents = false; } 
            catch { }

            _watcher.Created -= OnCreated;
            _watcher.Changed -= OnChanged;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;

            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _pending.Clear();

            Stopped?.Invoke(this, new FileWatcherStateEventArgs
            {
                Path = _currentPath,
                Filter = _currentFilter,
            });
        }

        public void SetDebounceMs(int ms)
        {
            if (ms < 0) 
                throw new ArgumentOutOfRangeException(nameof(ms));

            _debounceMs = ms;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
            => Created?.Invoke(this, new FileChangedEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? System.IO.Path.GetFileName(e.FullPath),
                ChangeType = "Created",
            });

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher dispara múltiples `Changed` por un solo guardado.
            // Los acumulamos y los volcamos con la ventana de debounce.
            if (e.ChangeType != WatcherChangeTypes.Changed) 
                return;

            _pending[e.FullPath] = 0;
            _debounceTimer.Change(_debounceMs, Timeout.Infinite);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
            => Deleted?.Invoke(this, new FileChangedEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? System.IO.Path.GetFileName(e.FullPath),
                ChangeType = "Deleted",
            });

        private void OnRenamed(object sender, RenamedEventArgs e)
            => Renamed?.Invoke(this, new FileRenamedEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? System.IO.Path.GetFileName(e.FullPath),
                OldFullPath = e.OldFullPath,
                OldName = e.OldName ?? System.IO.Path.GetFileName(e.OldFullPath),
            });

        private void OnError(object sender, ErrorEventArgs e)
            => Error?.Invoke(this, new FileWatcherErrorEventArgs
            {
                Message = e.GetException()?.Message ?? "Unknown error in FileSystemWatcher",
            });

        private void FlushPending()
        {
            if (_pending.IsEmpty) 
                return;

            foreach (var key in _pending.Keys.ToArray())
            {
                if (!_pending.TryRemove(key, out _)) 
                    continue;

                Changed?.Invoke(this, new FileChangedEventArgs
                {
                    FullPath = key,
                    Name = System.IO.Path.GetFileName(key),
                    ChangeType = "Changed",
                });
            }
        }

        protected override void Dispose(bool disposing)
        {
            Stop();
            _debounceTimer.Dispose();
            base.Dispose();
        }

        private void ThrowIfRunning()
        {
            if (IsRunning)
                throw new InvalidOperationException("The FileWatcher is now up and running.");
        }
    }

    public sealed class FileWatcherStateEventArgs : EventArgs
    {
        public string Path { get; init; } = "";
        public bool IncludeSubdirectories { get; init; }
        public string Filter { get; init; } = "";
    }

    public sealed class FileChangedEventArgs : EventArgs
    {
        public string FullPath { get; init; } = "";
        public string Name { get; init; } = "";
        public string ChangeType { get; init; } = "";
    }

    public sealed class FileRenamedEventArgs : EventArgs
    {
        public string FullPath { get; init; } = "";
        public string Name { get; init; } = "";
        public string OldFullPath { get; init; } = "";
        public string OldName { get; init; } = "";
    }

    public sealed class FileWatcherErrorEventArgs : EventArgs
    {
        public string Message { get; init; } = "";
    }
}
