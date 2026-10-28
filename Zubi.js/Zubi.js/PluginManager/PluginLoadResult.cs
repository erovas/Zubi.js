using System;
using System.Collections.Generic;
using System.Text;

namespace Zubi.js.PluginManager
{
    internal sealed class PluginLoadResult
    {
        /// <summary>
        /// Ruta completa del archivo del plugin.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Nombre del plugin, si se pudo leer. Null si falló antes de identificarlo.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// True si el plugin se cargó correctamente.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Mensaje de error si Success == false. Null si Success == true.
        /// </summary>
        public string? Error { get; }

        internal PluginLoadResult(string path, string? name, bool success, string? error)
        {
            Path = path;
            Name = name;
            Success = success;
            Error = error;
        }

        public static PluginLoadResult Ok(string path, string name)
            => new(path, name, true, null);

        public static PluginLoadResult Ok(PluginMetadata metadata)
            => new(metadata.Path, metadata.Name, true, null);

        public static PluginLoadResult Fail(string path, string error, string? name = null)
            => new(path, name, false, error);

        public static PluginLoadResult Fail(PluginMetadata metadata, string error)
            => new(metadata.Path, metadata.Name, false, error);
    }
}
