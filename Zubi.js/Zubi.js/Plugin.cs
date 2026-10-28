namespace Zubi.js
{
    public abstract class Plugin : IDisposable
    {
        #region Fields
        
        private int _disposed;

        #endregion

        internal Action<string>? OnDisposed;
        internal string Name = string.Empty;

        public string UID { get; } = Guid.NewGuid().ToString();
        public bool Disposed => Volatile.Read(ref _disposed) == 1;

        
        ~Plugin()
        {
            CleanUp(false);
        }

        public void Dispose()
        {
            CleanUp(true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);

        private void CleanUp(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try
            {
                Dispose(disposing);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                try
                {
                    OnDisposed?.Invoke(UID);
                }
                catch (Exception)
                {

                }

                OnDisposed = null;
            }
        }
    }
}