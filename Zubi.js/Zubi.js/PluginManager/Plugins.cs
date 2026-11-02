using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Zubi.js.Helpers;

namespace Zubi.js.PluginManager
{
    internal sealed class Plugins
    {
        private readonly object _DisposeLock = new();
        private readonly ConcurrentDictionary<string, List<IDisposable>> _EventSubscriptions = new();

        /// <summary>
        /// { "UID", Instnace }
        /// </summary>
        private readonly ConcurrentDictionary<string, Plugin> _Instances = new();

        /// <summary>
        /// { "AsemmblyName", PluginLoader }
        /// </summary>
        private readonly ConcurrentDictionary<string, AssemblyLoader> _Imported = new();

        /// <summary>
        /// { "PluginName", Type }
        /// </summary>
        private readonly ConcurrentDictionary<string, Type> _Types = new();


        #region Properties

        public AssemblyData[] Loaded
        {
            get
            {
                var list = new List<AssemblyData>();

                foreach (var item in _Imported.Values)
                    list.Add(new AssemblyData(item.Path, item.Name, item.Plugins));
                
                return list.ToArray();
            }
        }

        #endregion

        #region Methods

        public Plugin GetInstance(string UID)
        {
            return _Instances[UID];
        }

        public AssemblyLoadResult Load(string pluginPath)
        {
            return LoadAux(pluginPath);
        }

        public AssemblyLoadResult[] LoadFrom(string? folderPath = null)
        {
            folderPath = GetFullDirectory(folderPath);

            List<AssemblyLoadResult> results = new();

            foreach (string pluginPath in System.IO.Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories))
                results.Add(LoadAux(pluginPath));

            return results.ToArray();
        }

        public Plugin NewInstance(string pluginName, params object[] args)
        {
            Type? plugin = null;

            if (!this._Types.TryGetValue(pluginName, out plugin))
                throw new InvalidOperationException($"Plugin [{pluginName}] no exists");

            return CreateInstance(plugin, pluginName, args);
        }

        public void Unload(string assemblyName)
        {
            if (!this._Imported.ContainsKey(assemblyName))
                throw new InvalidOperationException($"[{assemblyName}] Assembly not loaded.");

            var ctx = this._Imported[assemblyName];

            // Obtener todas las instancias de los plugins de la dll { UID, Object }
            var instances = _Instances.Where(kv => ctx.Types.Any(t => t.IsInstanceOfType(kv.Value))).ToDictionary();

            // Verificar si existe alguna instancia sin hacer Dispose()
            foreach (var instance in instances.Values)
                if (!instance.Disposed)
                    throw new InvalidOperationException($"The assembly [{assemblyName}] cannot be unloaded because instances of its plugins exist. {Environment.NewLine} Plugin name [{instance.Name}] with UID [{instance.UID}]");

            // Quitar referencias por si hay alguna
            foreach (var instance in instances)
                this._Instances.TryRemove(instance.Key, out _);

            ctx.Unload();
            this._Imported.TryRemove(assemblyName, out _);
        }

        public object? Invoke(string uid, string methodName, object?[] args, CancellationToken ct)
        {
            if (!_Instances.TryGetValue(uid, out Plugin? plugin))
                throw new ObjectDisposedException("Instance disposed");
            
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

            var subscription = new EventSubscription(() =>
            {
                try { remove?.Invoke(instance, new object[] { bridge }); } 
                catch { }
            });

            _EventSubscriptions.AddOrUpdate(
                pluginUid, _ => new List<IDisposable> { subscription },
                (_, list) => 
                { 
                    list.Add(subscription); 
                    return list; 
                }
            );

            return subscription;
        }

        public object? ConvertValue(object? obj)
        {
            if (obj is null)
                return null;

            if (typeof(Plugin).IsAssignableFrom(obj.GetType()))
            {
                var plugin = (Plugin)obj;
                var uid = plugin.UID;

                // Si la instancia se ha creado desde otra parte, se registra.
                if (!_Instances.ContainsKey(uid))
                {
                    plugin.OnDisposed = PluginOnDisposed;
                    _Instances.TryAdd(uid, plugin);
                }

                return new { ___UID___ = uid };
            }

            return obj;
        }

        public void DisposeAll()
        {
            lock (_DisposeLock)
            {
                foreach (var kvp in _Instances)
                {
                    // Dispose de los eventos de los plugins registrados desde JS
                    if (_EventSubscriptions.TryRemove(kvp.Key, out var subs))
                        foreach (var sub in subs)
                            try { sub.Dispose(); } 
                            catch { }

                    // Dispose de las instancias de los plugins
                    try { kvp.Value.Dispose(); } 
                    catch { }
                }

                _Instances.Clear();
                _Types.Clear();

                foreach (var val in _Imported.Values)
                    val.Unload();

                _Imported.Clear();
            }

            _ = Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }

        #endregion

        #region Helpers

        private AssemblyLoadResult LoadAux(string assemblyPath)
        {
            try
            {
                assemblyPath = GetFullDirectory(assemblyPath);

                // Verificar si ya estaba cargada la dll
                foreach (var val in this._Imported.Values)
                    if (val.Path == assemblyPath)
                        return AssemblyLoadResult.Ok(val);

                var asmLoader = new AssemblyLoader(assemblyPath);

                // Verificar si al menos se ha cargado algun plugin
                if (asmLoader.Types.Length == 0)
                {
                    asmLoader.Unload();
                    return AssemblyLoadResult.Fail(asmLoader, "There are no plugin defined in the assembly");
                }
                
                foreach (var item in this._Imported.Values)
                {
                    // Verificar si existe otra dll con el mismo nombre
                    if(item.Name == asmLoader.Name)
                    {
                        var retVal = AssemblyLoadResult.Fail(asmLoader, $"An assembly with the same name is already loaded at [{item.Path}].");
                        asmLoader.Unload();
                        return retVal;
                    }

                    // Verificar si otra dll tiene cargado un plugin con el mismo nombre
                    string? pluginName = item.Types.FirstOrDefault(p1 => asmLoader.Types.Any(p2 => p1.Name == p2.Name))?.Name;
                    if (pluginName != null)
                    {
                        var retVal = AssemblyLoadResult.Fail(asmLoader, $"The plugin named [{pluginName}] already exists in the assembly [{item.Path}].");
                        asmLoader.Unload();
                        return retVal;
                    }
                }

                foreach (var item in asmLoader.Types)
                    this._Types.TryAdd(item.Name, item);
                
                this._Imported.TryAdd(asmLoader.Name, asmLoader);
                return AssemblyLoadResult.Ok(asmLoader);
            }
            catch (Exception ex)
            {
                return AssemblyLoadResult.Fail(assemblyPath, ex.Message);
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
                if (!this._Instances.ContainsKey(UID))
                    return;

                this._Instances.TryRemove(UID, out _);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static string GetFullDirectory(string? path)
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