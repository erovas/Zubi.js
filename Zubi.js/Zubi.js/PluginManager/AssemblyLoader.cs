namespace Zubi.js.PluginManager
{
    internal sealed class AssemblyLoader
    {
        #region Fields

        private AssemblyContext? _context;
        private readonly List<Type> _Types;

        #endregion


        public string Path { get; }
        public string Name { get; }
        public string[] Plugins => _Types.Select(t => t.Name).ToArray();
        internal Type[] Types => _Types.ToArray();


        public AssemblyLoader(string pluginPath)
        {
            Path = pluginPath;
            Name = System.IO.Path.GetFileNameWithoutExtension(pluginPath);
            (_context, _Types) = Load(pluginPath);
        }

        private static (AssemblyContext ctx, List<Type> tps) Load(string pluginPath)
        {
            FileStream streamDll = new FileStream(pluginPath, FileMode.Open, FileAccess.Read);
            FileStream? streamPdb = null;

            // Si se esta en un entorno de DEBUG, intentar cargar el PDB.
            if (App.IsDebugging)
                try { streamPdb = new FileStream(System.IO.Path.ChangeExtension(pluginPath, ".pdb"), FileMode.Open, FileAccess.Read); }
                catch (Exception) { }

            var ctx = new AssemblyContext(pluginPath);

            try
            {
                var asm = ctx.LoadFromStream(streamDll, streamPdb);
                var types = asm.GetTypes().Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && typeof(Plugin).IsAssignableFrom(t)).ToList();
                return (ctx, types);
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
            _Types.Clear();
            _context?.Unload();
            _context = null;

            //GC.Collect();
            //GC.WaitForPendingFinalizers();
        }

    }
}
