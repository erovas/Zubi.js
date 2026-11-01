using Zubi.js.PluginManager;

namespace Zubi.js.Helpers
{
    internal sealed class AssemblyLoadResult
    {
        /// <summary>
        /// Ruta completa del archivo del plugin.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Nombre del plugin, si se pudo leer. Null si falló antes de identificarlo.
        /// </summary>
        public string[] Plugins { get; }

        /// <summary>
        /// True si el plugin se cargó correctamente.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Mensaje de error si Success == false. Null si Success == true.
        /// </summary>
        public string? Error { get; }

        internal AssemblyLoadResult(string path, string[] plugins, bool success, string? error)
        {
            Path = path;
            Plugins = plugins;
            Success = success;
            Error = error;
        }

        internal AssemblyLoadResult(string path, Type[] plugins, bool success, string? error)
        {
            Path = path;
            Plugins = plugins.Select(t => t.Name).ToArray();
            Success = success;
            Error = error;
        }

        public static AssemblyLoadResult Ok(string path, string[] plugins)
            => new(path, plugins, true, null);

        public static AssemblyLoadResult Ok(string path, Type[] plugins)
            => new(path, plugins, true, null);

        public static AssemblyLoadResult Ok(AssemblyLoader assemblyLoader)
            => new(assemblyLoader.Path, assemblyLoader.Types, true, null);

        public static AssemblyLoadResult Fail(string path, string error)
            => new(path, Array.Empty<string>(), false, error);

        public static AssemblyLoadResult Fail(AssemblyLoader assemblyLoader, string error)
            => new(assemblyLoader.Path, assemblyLoader.Types, false, error);
    }
}
