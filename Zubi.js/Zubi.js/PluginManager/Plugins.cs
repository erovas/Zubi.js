using System.Collections.Concurrent;

namespace Zubi.js.PluginManager
{
    internal sealed class Plugins
    {
        private readonly ConcurrentDictionary<string, NamePlugin> _Instances = new();
        private readonly ConcurrentDictionary<Type, NamePlugin> _SingletonInstances = new();
        private readonly Dictionary<string, PluginLoader> _Imported = new();
        private readonly List<PluginMetadata> _LoadedItems = new();


        #region Properties

        public PluginMetadata[] Loaded => _LoadedItems.ToArray();

        #endregion

        #region Methods

        public Plugin GetInstance(string UID)
        {
            return (Plugin)_Instances[UID].Value;
        }

        public PluginLoadResult Load(string pluginPath)
        {
            return LoadAux(pluginPath);
        }

        public PluginLoadResult[] LoadFrom(string? folderPath = null)
        {
            folderPath = GetFullDirectory(folderPath);

            List<PluginLoadResult> results = new();

            foreach (string pluginPath in System.IO.Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories))
                results.Add(LoadAux(pluginPath));

            return results.ToArray();
        }

        public Plugin NewInstance(string pluginName, params object[] args)
        {
            PluginLoader? pluginLoader = null;

            if (!this._Imported.TryGetValue(pluginName, out pluginLoader))
                throw new Exception($"Plugin [{pluginName}] no exists");

            Type pluginType = pluginLoader.Type;

            // Verifica si el plugin está marcado como singleton.
            if (pluginLoader != null && pluginLoader.Metadata.Singleton)
                return (Plugin)this._SingletonInstances.GetOrAdd(pluginType, type => CreateInstance(type, pluginName, args)).Value;

            return (Plugin)CreateInstance(pluginType, pluginName, args).Value;
        }

        public void Unload(string pluginName)
        {
            if (!this._Imported.ContainsKey(pluginName))
                throw new Exception($"[{pluginName}] Plugin not found.");

            var ctx = this._Imported[pluginName];

            // Obtener todas las instancias del plugin <uid, object>
            var instances = this._Instances.Where(i => ctx.Type == i.Value.GetType()).ToList();

            // Verificar si existe alguna instancia sin hacer Dispose()
            foreach (var instance in instances)
                if (instance.Value.Value is Plugin plugin && !plugin.Disposed)
                    throw new Exception($"The [{pluginName}] plugin cannot be unloaded while instances remain undisposed. {Environment.NewLine} Instance UID = {plugin.UID}");

            foreach (var instance in instances)
                this._Instances.TryRemove(instance.Key, out _);

            // Puede que sea un plugin Singleton
            this._SingletonInstances.TryRemove(ctx.Type, out NamePlugin? _);

            ctx.Unload();
            this._Imported.Remove(pluginName);
            var item = _LoadedItems.FirstOrDefault(x => x.Name == pluginName);
            if (item != null)
                _LoadedItems.Remove(item);
        }

        internal object? Invoke(string uid, string methodName, object?[] args, CancellationToken ct)
        {
            if (!_Instances.TryGetValue(uid, out NamePlugin? np))
                throw new RpcException("", "Instancia no encontrada");

            var name = np.Name;
            var plugin = np.Value;
            return Utils.Invoke(plugin, name, methodName, args, ct);
        }

        #endregion

        #region Helpers

        private PluginLoadResult LoadAux(string pluginPath)
        {
            try
            {
                pluginPath = GetFullDirectory(pluginPath);

                foreach (var md in this._Imported.Values.ToList().Select(x => x.Metadata))
                    if (md.Path == pluginPath)
                        return PluginLoadResult.Ok(md);

                var pluginLoader = new PluginLoader(pluginPath);
                var metadata = pluginLoader.Metadata;
                var name = metadata.Name;
                var path = metadata.Path;

                if (this._Imported.ContainsKey(name))
                {
                    pluginLoader.Unload();
                    return PluginLoadResult.Fail(metadata, $"A plugin named [{name}] has already been loaded from the path [{this._Imported[name].Metadata.Path}].");
                }

                this._Imported.Add(name, pluginLoader);
                this._LoadedItems.Add(metadata);
                return PluginLoadResult.Ok(metadata);
            }
            catch (Exception ex)
            {
                return PluginLoadResult.Fail(pluginPath, ex.Message);
            }
        }

        private NamePlugin CreateInstance(Type type, string pluginName, object[] args)
        {
            object? pluginInstance = Activator.CreateInstance(type, args);

            if (pluginInstance == null)
                throw new Exception($"Impossible to build plugin instance [{pluginName}]");

            var instance = (Plugin)pluginInstance;
            instance.OnDisposed = PluginOnDisposed;
            var result = new NamePlugin(pluginName, pluginInstance);
            this._Instances.TryAdd(instance.UID, result);

            return result;
        }

        private void PluginOnDisposed(string UID)
        {
            try
            {
                if (!this._Instances.TryGetValue(UID, out NamePlugin? instance))
                    return;

                this._Instances.TryRemove(UID, out _);
                this._SingletonInstances.TryRemove(instance.Value.GetType(), out _);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string GetFullDirectory(string? path)
        {
            return Utils.GetFullDirectory(path);
        }

        #endregion

        private class NamePlugin
        {
            public string Name { get; }

            public object Value { get; }

            public NamePlugin(string name, object value)
            {
                Name = name;
                Value = value;
            }
        }

    }
}