using System.Collections.Concurrent;
using Zubi.js;

namespace Essentials
{
    public sealed class AppStorage : Plugin
    {
        private static ConcurrentDictionary<string, object?> Storage => Zubi.js.App.Storage;

        public int Count => Storage.Count;
        public bool IsEmpty => Storage.IsEmpty;
        public string[] Keys => Storage.Keys.ToArray();
        public object?[] Values => Storage.Values.ToArray();

        
        public object? Get(string key)
        {
            return Storage[key];
        }

        public bool Add(string key, string value)
        {
            return Storage.TryAdd(key, value);
        }

        public void Set(string key, object? value)
        {
            Storage[key] = value;
        }

        public bool Contains(string key)
        {
            return Storage.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return Storage.TryRemove(key, out _);
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing.
        }
    }
}