namespace Zubi.js.Bridge
{
    internal sealed class ConnectionTracker
    {
        private readonly object _lock = new();
        private readonly TimeSpan _grace;
        private int _active;
        private CancellationTokenSource? _pending;

        public event Action? ShutdownRequested;

        public ConnectionTracker(int grace) 
        { 
            _grace = TimeSpan.FromSeconds(grace < 3 ? 3 : grace); 
        }

        public void OnConnected()
        {
            lock (_lock)
            {
                _active++;
                _pending?.Cancel();
                _pending = null;
            }
        }

        public void OnDisconnected()
        {
            CancellationToken token;
            lock (_lock)
            {
                _active--;
                if (_active > 0)
                    return;

                _pending?.Cancel();
                _pending = new CancellationTokenSource();
                token = _pending.Token;
            }

            _ = Task.Run(async () =>
            {
                try { await Task.Delay(_grace, token); }
                catch (TaskCanceledException) { return; }

                lock (_lock)
                    if (_active > 0)
                        return;

                ShutdownRequested?.Invoke();
            });
        }
    }
}