namespace Zubi.js.Helpers
{
    internal class BrowserCandidate
    {
        public string Name { get; init; } = "";
        public BrowserEngine Engine { get; init; }
        public string[] WindowsPaths { get; init; } = Array.Empty<string>();
        public string[] MacPaths { get; init; } = Array.Empty<string>();
        public string[] LinuxCommands { get; init; } = Array.Empty<string>();

        public string? ResolveExecutable(Platform platform)
        {
            switch (platform)
            {
                case Platform.Windows:
                    return ResolveWindows();
                case Platform.OSX:
                    return ResolveMac();
                case Platform.Linux:
                    return ResolveLinux();
                default:
                    return null;
            }
        }

        private string? ResolveWindows()
        {
            if (WindowsPaths.Length == 0) return null;
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            foreach (var rel in WindowsPaths)
            {
                foreach (var root in new[] { pf, pf86, localApp })
                {
                    string full = Path.Combine(root, rel);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }

        private string? ResolveMac()
        {
            if (MacPaths.Length == 0) return null;
            return MacPaths.FirstOrDefault(File.Exists);
        }

        private string? ResolveLinux()
        {
            if (LinuxCommands.Length == 0) return null;
            string? pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) return null;

            var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cmd in LinuxCommands)
            {
                foreach (var dir in dirs)
                {
                    string full = Path.Combine(dir, cmd);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }
    }
}