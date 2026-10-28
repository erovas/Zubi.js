using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Zubi.js.Helpers;

namespace Zubi.js.PluginManager
{
    internal sealed class Plugins
    {
        private readonly ConcurrentDictionary<string, Plugin> _Instances = new();
        private readonly ConcurrentDictionary<Type, Plugin> _SingletonInstances = new();
        private readonly Dictionary<string, PluginLoader> _Imported = new();
        private readonly List<PluginMetadata> _LoadedItems = new();


        #region Properties

        public PluginMetadata[] Loaded => _LoadedItems.ToArray();

        #endregion

        #region Methods

        public Plugin GetInstance(string UID)
        {
            return _Instances[UID];
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
                throw new InvalidOperationException($"Plugin [{pluginName}] no exists");

            Type pluginType = pluginLoader.Type;

            // Verifica si el plugin está marcado como singleton.
            if (pluginLoader != null && pluginLoader.Metadata.Singleton)
                return this._SingletonInstances.GetOrAdd(pluginType, type => CreateInstance(type, pluginName, args));

            return CreateInstance(pluginType, pluginName, args);
        }

        public void Unload(string pluginName)
        {
            if (!this._Imported.ContainsKey(pluginName))
                throw new InvalidOperationException($"[{pluginName}] Plugin not found.");

            var ctx = this._Imported[pluginName];

            // Obtener todas las instancias del plugin <uid, object>
            var instances = this._Instances.Where(i => ctx.Type == i.Value.GetType()).ToList();

            // Verificar si existe alguna instancia sin hacer Dispose()
            foreach (var instance in instances)
                if (instance.Value is Plugin plugin && !plugin.Disposed)
                    throw new InvalidOperationException($"The [{pluginName}] plugin cannot be unloaded while instances remain undisposed. {Environment.NewLine} Instance UID = {plugin.UID}");

            foreach (var instance in instances)
                this._Instances.TryRemove(instance.Key, out _);

            // Puede que sea un plugin Singleton
            this._SingletonInstances.TryRemove(ctx.Type, out Plugin? _);

            ctx.Unload();
            this._Imported.Remove(pluginName);
            var item = _LoadedItems.FirstOrDefault(x => x.Name == pluginName);
            if (item != null)
                _LoadedItems.Remove(item);
        }

        internal object? Invoke(string uid, string methodName, object?[] args, CancellationToken ct)
        {
            if (!_Instances.TryGetValue(uid, out Plugin? plugin))
                throw new InvalidOperationException("Instancia no encontrada");

            return Utils.Invoke(plugin, plugin.Name, methodName, args, ct);
        }

        public IDisposable SubscribeEvent(string pluginUid, string eventName, Action<object?> onFire)
        {
            // método ya existente en tu manager
            var instance = GetInstance(pluginUid) ?? throw new InvalidOperationException($"No existe plugin '{pluginUid}'");

            var evt = instance.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"El plugin '{instance.Name}' no expone el evento '{eventName}'");

            var add = evt.GetAddMethod(true) ?? throw new InvalidOperationException("Sin add accessor");
            var remove = evt.GetRemoveMethod(true);

            // Delegado con el tipo exacto del evento (EventHandler<T>, EventHandler, etc.)
            var bridge = CreateBridge(evt.EventHandlerType!, onFire);

            add.Invoke(instance, new object[] { bridge });

            return new EventSubscription(() =>
            {
                try { remove?.Invoke(instance, new object[] { bridge }); } 
                catch { }
            });
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

        private Plugin CreateInstance(Type type, string pluginName, object[] args)
        {
            object? pluginInstance = Activator.CreateInstance(type, args);

            if (pluginInstance == null)
                throw new Exception($"Impossible to build plugin instance [{pluginName}]");

            var instance = (Plugin)pluginInstance;
            instance.OnDisposed = PluginOnDisposed;
            instance.Name = pluginName;
            this._Instances.TryAdd(instance.UID, instance);

            return instance;
        }

        private void PluginOnDisposed(string UID)
        {
            try
            {
                if (!this._Instances.TryGetValue(UID, out Plugin? instance))
                    return;

                this._Instances.TryRemove(UID, out _);
                this._SingletonInstances.TryRemove(((object)instance).GetType(), out _);
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

        // Construye un Delegate compatible con la firma del evento.
        // Convención .NET: último parámetro = payload (args).
        private static Delegate CreateBridge(Type eventHandlerType, Action<object?> onFire)
        {
            var invoke = eventHandlerType.GetMethod("Invoke") ?? throw new InvalidOperationException("Tipo de evento inválido");
            
            var ps = invoke.GetParameters()
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            var fire = Expression.Constant(onFire);

            var body = ps.Length == 0
                ? (Expression)Expression.Invoke(fire, Expression.Constant(null, typeof(object)))
                : Expression.Invoke(fire, Expression.Convert(ps[^1], typeof(object)));

            return Expression.Lambda(eventHandlerType, body, ps).Compile();
        }

        #endregion

    }

    internal sealed class EventSubscription : IDisposable
    {
        private readonly Action _detach;
        private int _disposed;
        public EventSubscription(Action detach) => _detach = detach;
        public void Dispose() 
        { 
            if (Interlocked.Exchange(ref _disposed, 1) == 0) 
                _detach(); 
        }
    }
}