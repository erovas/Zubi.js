using System.Reflection;

namespace Zubi.js.PluginManager
{
    internal sealed class PluginLoader
    {
        #region Fields

        private PluginLoadContext? _context;
        private PluginMetadata? _metadata;
        private Type? _type;

        #endregion

        private PluginLoadContext Context => _context!;

        public PluginMetadata Metadata => _metadata!;

        public Type Type => _type!;


        public PluginLoader(string pluginPath)
        {
            (_metadata, _context, _type) = Load(pluginPath);
        }

        private static (PluginMetadata md, PluginLoadContext ctx, Type tp) Load(string pluginPath)
        {
            FileStream streamDll = new FileStream(pluginPath, FileMode.Open, FileAccess.Read);
            FileStream? streamPdb = null;

            // Si se esta en un entorno de DEBUG, intentar cargar el PDB.
            if (App.IsDebugging)
                try { streamPdb = new FileStream(Path.ChangeExtension(pluginPath, ".pdb"), FileMode.Open, FileAccess.Read); }
                catch (Exception) { }

            var ctx = new PluginLoadContext(pluginPath);

            try
            {
                var asm = ctx.LoadFromStream(streamDll, streamPdb);
                var metadata = FromAssembly(asm, pluginPath);
                var type = ResolveType(asm, metadata);
                return (metadata, ctx, type);
            }
            catch (Exception)
            {
                // Descargar el Contexto
                ctx.Unload();
                ctx = null;
                // Ayuda a liberar recursos
                GC.Collect();
                throw;
            }
            finally
            {
                streamDll.Dispose();
                streamPdb?.Dispose();
            }
        }

        public void Unload()
        {
            _type = null;
            _context?.Unload();
            _context = null;
            _metadata = null;
            //GC.Collect();
            //GC.WaitForPendingFinalizers();
        }


        public static PluginMetadata FromAssembly(Assembly assembly, string pluginPath)
        {
            var attr = assembly.GetCustomAttribute<PluginAttribute>() ?? throw new Exception("The DLL is not a plugin.");

            return new PluginMetadata
            {
                Path = pluginPath,
                Name = attr.Name,
                Type = attr.Type,
                Version = attr.Version,
                Singleton = attr.Singleton,
                Author = attr.Author,
                Description = attr.Description
            };
        }

        private static Type ResolveType(Assembly assembly, PluginMetadata metadata)
        {
            var type = assembly.GetType(metadata.Type, throwOnError: false);
            if (type != null && typeof(Plugin).IsAssignableFrom(type) && !type.IsAbstract)
                return type;

            throw new Exception("There are no plugin defined in the assembly");
        }

    }
}
