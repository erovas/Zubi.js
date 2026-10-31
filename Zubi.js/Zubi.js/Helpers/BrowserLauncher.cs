using System.Diagnostics;

namespace Zubi.js.Helpers
{
    internal static class BrowserLauncher
    {
        #region Candidates

        private static readonly List<BrowserCandidate> Candidates = new()
        {
            // ---------- Chromium (Blink) ----------
            new BrowserCandidate
            {
                Name = "Chromium",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[] { @"Chromium\Application\chrome.exe" },
                MacPaths = new[] { "/Applications/Chromium.app/Contents/MacOS/Chromium" },
                LinuxCommands = new[] { "chromium", "chromium-browser", "chromium-freeworld" }
            },
            new BrowserCandidate
            {
                Name = "Brave",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[]
                {
                    @"BraveSoftware\Brave-Browser\Application\brave.exe",
                    @"BraveSoftware\Brave-Browser-Beta\Application\brave.exe",
                    @"BraveSoftware\Brave-Browser-Dev\Application\brave.exe",
                    @"BraveSoftware\Brave-Browser-Nightly\Application\brave.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
                    "/Applications/Brave Browser Beta.app/Contents/MacOS/Brave Browser Beta",
                    "/Applications/Brave Browser Dev.app/Contents/MacOS/Brave Browser Dev",
                    "/Applications/Brave Browser Nightly.app/Contents/MacOS/Brave Browser Nightly"
                },
                LinuxCommands = new[] { "brave-browser", "brave-browser-beta", "brave-browser-dev", "brave-browser-nightly" }
            },
            new BrowserCandidate
            {
                Name = "Vivaldi",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[] { @"Vivaldi\Application\vivaldi.exe" },
                MacPaths = new[] { "/Applications/Vivaldi.app/Contents/MacOS/Vivaldi" },
                LinuxCommands = new[] { "vivaldi", "vivaldi-stable", "vivaldi-snapshot" }
            },
            new BrowserCandidate
            {
                Name = "Arc",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[]
                {
                    // Arc se instala como una aplicación de la Microsoft Store / AppX.
                    // La ruta puede variar, pero esta es la ubicación estándar del ejecutable.
                    @"Packages\TheBrowserCompany.Arc_ttt1ap7aakyb4\LocalCache\Local\Arc\Arc.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Arc.app/Contents/MacOS/Arc"
                },
                LinuxCommands = Array.Empty<string>() // Arc no tiene versión nativa para Linux
            },
            new BrowserCandidate
            {
                Name = "Edge",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[]
                {
                    @"Microsoft\Edge\Application\msedge.exe",
                    @"Microsoft\Edge Beta\Application\msedge.exe",
                    @"Microsoft\Edge Dev\Application\msedge.exe",
                    @"Microsoft\Edge SxS\Application\msedge.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                    "/Applications/Microsoft Edge Beta.app/Contents/MacOS/Microsoft Edge Beta",
                    "/Applications/Microsoft Edge Dev.app/Contents/MacOS/Microsoft Edge Dev",
                    "/Applications/Microsoft Edge Canary.app/Contents/MacOS/Microsoft Edge Canary"
                },
                LinuxCommands = new[] { "microsoft-edge", "microsoft-edge-stable", "microsoft-edge-beta", "microsoft-edge-dev" }
            },
            new BrowserCandidate
            {
                Name = "Opera",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[]
                {
                    @"Programs\Opera\opera.exe",
                    @"Programs\Opera beta\opera.exe",
                    @"Programs\Opera developer\opera.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Opera.app/Contents/MacOS/Opera",
                    "/Applications/Opera Beta.app/Contents/MacOS/Opera Beta",
                    "/Applications/Opera Developer.app/Contents/MacOS/Opera Developer"
                },
                LinuxCommands = new[] { "opera", "opera-beta", "opera-developer" }
            },
            new BrowserCandidate
            {
                Name = "Chrome",
                Engine = BrowserEngine.Chromium,
                WindowsPaths = new[]
                {
                    @"Google\Chrome\Application\chrome.exe",
                    @"Google\Chrome Beta\Application\chrome.exe",
                    @"Google\Chrome Dev\Application\chrome.exe",
                    @"Google\Chrome SxS\Application\chrome.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Applications/Google Chrome Beta.app/Contents/MacOS/Google Chrome Beta",
                    "/Applications/Google Chrome Dev.app/Contents/MacOS/Google Chrome Dev",
                    "/Applications/Google Chrome Canary.app/Contents/MacOS/Google Chrome Canary"
                },
                LinuxCommands = new[] { "google-chrome", "google-chrome-stable", "google-chrome-beta", "google-chrome-unstable" }
            },
            
            // ---------- Gecko ----------
            new BrowserCandidate
            {
                Name = "LibreWolf",
                Engine = BrowserEngine.Gecko,
                WindowsPaths = new[] { @"LibreWolf\librewolf.exe" },
                MacPaths = new[] { "/Applications/LibreWolf.app/Contents/MacOS/librewolf" },
                LinuxCommands = new[] { "librewolf" }
            },
            new BrowserCandidate
            {
                Name = "Firefox",
                Engine = BrowserEngine.Gecko,
                WindowsPaths = new[]
                {
                    @"Mozilla Firefox\firefox.exe",
                    @"Firefox Nightly\firefox.exe",
                    @"Firefox Developer Edition\firefox.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Firefox.app/Contents/MacOS/firefox-bin",
                    "/Applications/Firefox Nightly.app/Contents/MacOS/firefox-bin",
                    "/Applications/Firefox Developer Edition.app/Contents/MacOS/firefox-bin"
                },
                LinuxCommands = new[] { "firefox", "firefox-esr", "firefox-nightly", "firefox-developer-edition" }
            },
            new BrowserCandidate
            {
                Name = "Zen",
                Engine = BrowserEngine.Gecko,
                WindowsPaths = new[]
                {
                    @"Program Files\Zen Browser\zen.exe",
                    @"Program Files\Zen Twilight\zen.exe" // Canal de desarrollo (Twilight)
                },
                MacPaths = new[]
                {
                    "/Applications/Zen.app/Contents/MacOS/zen",
                    "/Applications/Zen Twilight.app/Contents/MacOS/zen"
                },
                LinuxCommands = new[] { "zen-browser", "zen", "zen-bin" } // zen-browser es el comando común en repositorios
            },

            new BrowserCandidate
            {
                Name = "Floorp",
                Engine = BrowserEngine.Gecko,
                WindowsPaths = new[]
                {
                    @"Program Files\Ablaze Floorp\floorp.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Floorp.app/Contents/MacOS/floorp"
                },
                LinuxCommands = new[] { "floorp" }
            },

            new BrowserCandidate
            {
                Name = "Waterfox",
                Engine = BrowserEngine.Gecko,
                WindowsPaths = new[]
                {
                    @"Waterfox\waterfox.exe", // Se resuelve contra %LOCALAPPDATA%, %PROGRAMFILES%, etc.
                    @"Waterfox Current\waterfox.exe",
                    @"Waterfox Classic\waterfox.exe"
                },
                MacPaths = new[]
                {
                    "/Applications/Waterfox.app/Contents/MacOS/waterfox",
                    "/Applications/Waterfox Current.app/Contents/MacOS/waterfox",
                    "/Applications/Waterfox Classic.app/Contents/MacOS/waterfox"
                },
                LinuxCommands = new[] { "waterfox", "waterfox-current", "waterfox-classic" }
            },

            // ---------- WebKit ----------
            new BrowserCandidate
            {
                Name = "Safari",
                Engine = BrowserEngine.WebKit,
                WindowsPaths = Array.Empty<string>(), // solo macOS
                MacPaths = new[] { "/Applications/Safari.app/Contents/MacOS/Safari" },
                LinuxCommands = Array.Empty<string>()
            },
            new BrowserCandidate
            {
                Name = "Epiphany",
                Engine = BrowserEngine.WebKit,
                WindowsPaths = Array.Empty<string>(),
                MacPaths = Array.Empty<string>(),
                LinuxCommands = new[] { "epiphany", "gnome-web" }
            }
        };

        #endregion

        #region Properties

        private readonly static string AppProfile;

        private readonly static string AppName;

        private readonly static Platform Platform;

        #endregion

        static BrowserLauncher()
        {
            AppName = App.Name;
            AppProfile = AppName + "_Profiles";
            Platform = App.Platform;
        }

        public static void Launch(string url, ConfigBrowser config, string profileName = "Default")
        {
            ProfileMode mode = config.Mode;

            var (exe, candidate) = FindBrowser(config.Name);

            if (exe is null || candidate is null)
            {
                LaunchDefaultBrowser(url);
                return;
            }

            // 1. Registrar el navegador lanzado
            App.Browser = new BrowserData(candidate.Name, candidate.Engine, exe);

            // 2. Adaptar argumentos según el motor
            var psi = BuildProcessStartInfo(exe, candidate.Engine, url, mode, profileName, out string? profileDir);

            var proc = Process.Start(psi);

            // 3. Limpieza de perfil temporal solo para motores que la soportan
            if (mode == ProfileMode.Temporary && proc is not null && profileDir is not null &&
                (candidate.Engine == BrowserEngine.Chromium || candidate.Engine == BrowserEngine.Gecko))
            {
                ScheduleTempProfileCleanup(proc, profileDir);
            }
        }

        private static ProcessStartInfo BuildProcessStartInfo(string exe, BrowserEngine engine, string url, ProfileMode mode, string profileName, out string? profileDir)
        {
            profileDir = null;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };

            switch (engine)
            {
                case BrowserEngine.Chromium:
                    psi.ArgumentList.Add($"--app={url}");
                    psi.ArgumentList.Add("--no-first-run");
                    psi.ArgumentList.Add("--no-default-browser-check");
                    profileDir = ResolveProfileDirectory(mode, profileName);
                    if (profileDir is not null)
                    {
                        Directory.CreateDirectory(profileDir);
                        psi.ArgumentList.Add($"--user-data-dir={profileDir}");
                    }
                    break;

                case BrowserEngine.Gecko:
                    // Firefox no soporta --app; se abre la URL directamente
                    psi.ArgumentList.Add(url);
                    if (mode != ProfileMode.Shared)
                    {
                        profileDir = ResolveProfileDirectory(mode, profileName);
                        if (profileDir is not null)
                        {
                            Directory.CreateDirectory(profileDir);
                            psi.ArgumentList.Add("-profile");
                            psi.ArgumentList.Add(profileDir);
                        }
                    }
                    break;

                case BrowserEngine.WebKit:
                    // Safari no acepta argumentos de línea de comandos para URL en macOS.
                    // La forma fiable es usar `open` con la URL y luego seleccionar la app.
                    // Como ya tenemos la ruta del ejecutable, podemos usar `open -a`.
                    if (Platform == Platform.OSX)
                    {
                        psi = new ProcessStartInfo("open") { UseShellExecute = false };
                        psi.ArgumentList.Add("-a");
                        psi.ArgumentList.Add(exe);
                        psi.ArgumentList.Add(url);
                    }
                    else
                    {
                        // Para Epiphany en Linux, se puede pasar la URL directamente
                        psi.ArgumentList.Add(url);
                    }
                    break;

                default:
                    psi.ArgumentList.Add(url);
                    break;
            }

            return psi;
        }

        // --- Búsqueda del navegador ---

        private static (string? exe, BrowserCandidate? candidate) FindBrowser(string name)
        {
            var index = Candidates.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (index > 0)
            {
                var item = Candidates[index];
                Candidates.RemoveAt(index);
                Candidates.Insert(0, item);
            }

            foreach (var candidate in Candidates)
            {
                string? path = candidate.ResolveExecutable(Platform);
                if (path is not null)
                    return (path, candidate);
            }
            return (null, null);
        }

        // --- Resolución de carpeta de perfil (sin cambios) ---
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

        private static string GetAppDataRoot() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "BrowserProfiles");

        private static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            return string.IsNullOrEmpty(clean) ? "Default" : clean;
        }

        // --- Limpieza de perfiles temporales (sin cambios) ---
        private static void ScheduleTempProfileCleanup(Process proc, string profileDir)
        {
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                Task.Delay(2000).ContinueWith(_ =>
                {
                    try { Directory.Delete(profileDir, recursive: true); }
                    catch { /* ignorar */ }
                });
            };
        }

        public static void CleanupOrphanTempProfiles()
        {
            string root = Path.Combine(Path.GetTempPath(), AppProfile);
            
            if (!Directory.Exists(root)) 
                return;

            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    if (Directory.GetCreationTime(dir) < DateTime.Now.AddHours(-24))
                        Directory.Delete(dir, recursive: true);
                }
                catch { /* ignorar */ }
            }
        }

        // --- Lanzamiento del navegador por defecto ---
        private static void LaunchDefaultBrowser(string url)
        {
            ProcessStartInfo psi;

            if (Platform == Platform.Windows)
                psi = new ProcessStartInfo(url) { UseShellExecute = true };

            else if (Platform == Platform.OSX)
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