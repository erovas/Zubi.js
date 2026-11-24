using Zubi.js;

namespace Essentials
{
    public class PathSystem : Plugin
    {

        #region PROPS

        public string RandomFileName => Path.GetRandomFileName();

        #endregion

        #region METHODS

        public string? ChangeExtension(string? path, string? extension)
        {
            return Path.ChangeExtension(path, extension);
        }

        public bool Exists(string? path)
        {
            return Path.Exists(path);
        }

        public string? GetDirectoryName(string? path)
        {
            return Path.GetDirectoryName(path);
        }

        public string? GetExtension(string? path)
        {
            return Path.GetExtension(path);
        }

        public string? GetFileName(string? path)
        {
            return Path.GetFileName(path);
        }

        public string? GetFileNameWithoutExtension(string? path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        public bool IsPathFullyQualified(string path)
        {
            return Path.IsPathRooted(path);
        }

        public bool HasExtension(string path)
        {
            return Path.HasExtension(path);
        }

        public string Combine(params string[] paths)
        {
            return Path.Combine(paths);
        }

        public string Join(params string?[] paths)
        {
            return Path.Join(paths);
        }

        public string GetRelativePath(string relativeTo, string path)
        {
            return Path.GetRelativePath(relativeTo, path);
        }

        public string TrimEndingDirectorySeparator(string path)
        {
            
            return Path.TrimEndingDirectorySeparator(path);
        }

        public bool EndsInDirectorySeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path);
        }

        public string GetFullPath(string path, string? basePath = null)
        {
            if (basePath == null)
                return Path.GetFullPath(path);

            return Path.GetFullPath(path, basePath);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            // Do nothing.
        }
    }
}