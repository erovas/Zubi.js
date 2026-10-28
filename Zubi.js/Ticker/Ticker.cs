using Zubi.js;

namespace Ticker
{
    public class Ticker : Plugin
    {
        public event EventHandler<TickEventArgs>? Tick;
        public event EventHandler? Started;
        public event EventHandler? Stopped;

        private readonly System.Timers.Timer _timer;
        private int _count;

        public Ticker()
        {
            _timer = new System.Timers.Timer(1000) { AutoReset = true };
            _timer.Elapsed += (_, _) =>
                Tick?.Invoke(this, new TickEventArgs(
                    Interlocked.Increment(ref _count), DateTime.UtcNow));
        }

        public Task Start(double intervalMs)
        {
            _count = 0;
            _timer.Interval = intervalMs;
            _timer.Start();
            Started?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task Stop()
        {
            _timer.Stop();
            Stopped?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public int Count => _count;

        protected override void Dispose(bool disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }

    public sealed record TickEventArgs(int Count, DateTime Timestamp);
}