using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zubi.js
{
    internal enum ProfileMode
    {
        Shared,
        Named,
        Temporary
    }

    internal static class BrowserLauncher
    {
        #region Fields

        private static readonly string[] MacCandidates =
        {
            "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
            "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi",
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        };

        private static readonly string[] LinuxCommands =
        {
            "google-chrome", "google-chrome-stable", "chromium", "chromium-browser",
            "microsoft-edge", "microsoft-edge-stable", "brave-browser", "vivaldi"
        };

        private static readonly string[] WindowsCandidates;

        #endregion

        #region Properties

        private static string AppProfile => AppName + "_Profiles";

        public readonly static string AppName = App.Name;

        #endregion

        static BrowserLauncher()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string brave = @"BraveSoftware\Brave-Browser\Application\brave.exe";
            string vivaldi = @"Vivaldi\Application\vivaldi.exe";
            string edge = @"Microsoft\Edge\Application\msedge.exe";
            string chrome = @"Google\Chrome\Application\chrome.exe";

            WindowsCandidates =
            [
                Path.Combine(pf,       brave),
                Path.Combine(pf86,     brave),
                Path.Combine(localApp, brave),
                Path.Combine(localApp, vivaldi),
                Path.Combine(pf,       vivaldi),
                Path.Combine(pf86,     edge),
                Path.Combine(pf,       edge),
                Path.Combine(pf,       chrome),
                Path.Combine(pf86,     chrome),
                Path.Combine(localApp, chrome),
            ];
        }

        public static void Launch(string url, ProfileMode mode = ProfileMode.Named, string profileName = "Default")
        {
            string? exe = FindChromiumBrowser();

            if (exe is null)
            {
                LaunchDefaultBrowser(url);
                return;
            }

            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add($"--app={url}");
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");

            string? profileDir = ResolveProfileDirectory(mode, profileName);
            if (profileDir is not null)
            {
                Directory.CreateDirectory(profileDir);
                psi.ArgumentList.Add($"--user-data-dir={profileDir}");
            }

            var proc = Process.Start(psi);

            // En modo temporal, intentamos limpiar el perfil al cerrar
            if (mode == ProfileMode.Temporary && proc is not null && profileDir is not null)
                ScheduleTempProfileCleanup(proc, profileDir);
        }

        // --- Resolución de carpeta de perfil ---

        private static string? ResolveProfileDirectory(ProfileMode mode, string profileName)
        {
            switch (mode)
            {
                case ProfileMode.Shared:
                    return null;

                case ProfileMode.Named:
                    return Path.Combine(GetAppDataRoot(), "Profiles", Sanitize(profileName));

                case ProfileMode.Temporary:
                    return Path.Combine(Path.GetTempPath(), AppProfile, Guid.NewGuid().ToString("N"));

                default:
                    return null;
            }
        }

        private static string GetAppDataRoot()
        {
            // Windows: %LOCALAPPDATA% | macOS/Linux: ~/.local/share (o equivalente)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "BrowserProfiles");
        }

        private static string Sanitize(string name)
        {
            // Evita caracteres inválidos en rutas
            char[] invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrEmpty(clean) ? "Default" : clean;
        }

        // --- Limpieza de perfiles temporales ---

        private static void ScheduleTempProfileCleanup(Process proc, string profileDir)
        {
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                // Chromium suele relanzarse como proceso hijo; damos margen
                Task.Delay(2000).ContinueWith(_ =>
                {
                    try { Directory.Delete(profileDir, recursive: true); }
                    catch { /* Si falla, se limpiará en el próximo arranque */ }
                });
            };
        }

        /// <summary>
        /// Elimina perfiles temporales huérfanos (por ejemplo, si la app se cerró antes
        /// de que el navegador terminara). Llama a esto al iniciar tu aplicación.
        /// </summary>
        public static void CleanupOrphanTempProfiles()
        {
            string root = Path.Combine(Path.GetTempPath(), AppProfile);

            if (!Directory.Exists(root))
                return;

            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    // Solo borramos carpetas con más de 24h para evitar colisiones
                    if (Directory.GetCreationTime(dir) < DateTime.Now.AddHours(-24))
                        Directory.Delete(dir, recursive: true);
                }
                catch { /* Ignorar */ }
            }
        }

        private static string? FindChromiumBrowser()
        {
            switch (App.Platform)
            {
                case Platform.Windows:
                    return WindowsCandidates.FirstOrDefault(File.Exists);
                case Platform.Linux:
                    return FindOnPath(LinuxCommands);
                case Platform.OSX:
                    return MacCandidates.FirstOrDefault(File.Exists);
                default:
                    return null;
            }
        }

        private static string? FindOnPath(IEnumerable<string> commands)
        {
            string? pathVar = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(pathVar))
                return null;

            var dirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (string cmd in commands)
            {
                foreach (string dir in dirs)
                {
                    string full = Path.Combine(dir, cmd);
                    if (File.Exists(full))
                        return full;
                }
            }

            return null;
        }

        private static void LaunchDefaultBrowser(string url)
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo(url) { UseShellExecute = true };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
            }
            else
            {
                psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
            }

            Process.Start(psi);
        }
    }

}