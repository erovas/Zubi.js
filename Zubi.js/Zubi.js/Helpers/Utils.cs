using System.Net;
using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Zubi.js.Helpers
{
    internal static class Utils
    {
        public const string INDEX_HTML = "index.html";
        public const string PLUGINS = "Plugins";

        public static readonly JsonSerializerOptions JsonReadOptions = new()
        {
            // Permitir propiedades como PascalCase, camelCase, snake_case.
            PropertyNamingPolicy = null,
            // Ignora mayúsculas/minúsculas al mapear a propiedades C#
            PropertyNameCaseInsensitive = true,

            // Permite // y /* */ en el JSON
            ReadCommentHandling = JsonCommentHandling.Skip,
            // Permite {"a": 1, "b": 2,} 
            AllowTrailingCommas = true,
            // Permite leer propiedades numericas escritas como string
            NumberHandling = JsonNumberHandling.AllowReadingFromString, 

            // Permitir leer enumerados como numero
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
        };

        public static readonly JsonSerializerOptions JsonWriteStrictOptions = new()
        {
            // Mantener el nombre exacto de la propiedad C# en el JSON
            PropertyNamingPolicy = null,

            // JSON compacto (sin saltos de línea ni espacios extra)
            WriteIndented = false,

            // Omitir serializar nulos.
            //DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            Converters = 
            { 
                new JsonStringEnumConverter(),  // Forzar enumerados C# como string en JSON
                new Base64ByteArrayConverter()  // Convertir Byte[] en string base 64, mayor eficiencia
            },
        };

        public static string JsonSerialize(object value)
        {
            return JsonSerializer.Serialize(value, JsonWriteStrictOptions);
        }

        public static T Deserialize<T>(string json) where T : new()
        {
            return JsonSerializer.Deserialize<T>(json, JsonReadOptions) ?? new T();
        }

        public static object? ConvertArg(object? raw, Type target)
        {
            if (raw is JsonElement el)
                return el.Deserialize(target, JsonWriteStrictOptions);

            if (raw is null)
                return null;

            if (target.IsInstanceOfType(raw))
                return raw;

            var json = JsonSerializer.Serialize(raw, JsonWriteStrictOptions);
            return JsonSerializer.Deserialize(json, target, JsonWriteStrictOptions);
        }

        public static string GetUrlPort(int port)
        {
            return $"http://127.0.0.1:{port}";
        }

        private sealed class Base64ByteArrayConverter : JsonConverter<byte[]>
        {
            public override byte[] Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
            {
                var s = reader.GetString();
                return s is null ? Array.Empty<byte>() : Convert.FromBase64String(s);
            }

            public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions _)
            {
                writer.WriteStringValue(Convert.ToBase64String(value));
            }

        }
    
        public static object? Invoke(object source, string name, string methodName, object?[] args, CancellationToken ct)
        {
            var type = source.GetType();
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

            // Puede ser un getter o setter
            if (method is null)
            {
                var prop = type.GetProperty(methodName, BindingFlags.Public | BindingFlags.Instance);

                if (prop is null)
                    throw new InvalidOperationException($"'{methodName}' no exists in '{name}'");

                // Llamando al getter [let value = await myPlugin.Msg]
                if (prop.CanRead && args.Length == 0)
                    return prop.GetValue(source);

                // Llamando al setter [await myPlugin.Msg("Hello")]
                else if (prop.CanWrite && args.Length == 1)
                {
                    prop.SetValue(source, Utils.ConvertArg(args[0], prop.PropertyType));
                    return null;
                }

                throw new InvalidOperationException($"Too many arguments for '{methodName}' in '{name}'");
            }

            var ps = method.GetParameters();
            var converted = new object?[ps.Length];

            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.ParameterType == typeof(CancellationToken)) 
                { 
                    converted[i] = ct; 
                    continue; 
                }
                if (i >= args.Length)
                {
                    if (p.HasDefaultValue)
                    {
                        converted[i] = p.DefaultValue;
                        continue;
                    }
                    throw new CodeException("EINVAL", $"Argument '{p.Name}' is missing");
                }
                try
                {
                    converted[i] = ConvertArg(args[i], p.ParameterType);
                }
                catch (Exception ex)
                {
                    throw new CodeException("EARG", $"'{p.Name}': {ex.Message}");
                }
            }

            try
            {
                return method.Invoke(source, converted);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }
        }

        public static string GetFullDirectory(string? path, bool throwEx = true)
        {
            string basePath = Path.GetFullPath(App.BaseDirectory);

            if (string.IsNullOrWhiteSpace(path))
                path = basePath;

            path = Path.GetFullPath(path.TrimStart('/', '\\'), basePath);

            if (!string.IsNullOrWhiteSpace(Path.GetExtension(path)))
                if (!File.Exists(path) && throwEx)
                    throw new FileNotFoundException($"The file [{path}] does not exist");
                else
                    return path;

            else if (!Directory.Exists(path) && throwEx)
                throw new Exception($"The directory [{path}] does not exist");

            return path;
        }

        public static int FindFreePort(int excludedPort = 0)
        {
            while (true)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);

                try
                {
                    listener.Start();

                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                    if (port != excludedPort)
                        return port;
                }
                finally
                {
                    listener.Stop();
                }
            }
        }

        public static (int Width, int Height) GetWidthHeight(int width, int height)
        {
            return (width < 0 ? 800 : width, height < 0 ? 600 : height);
        }

        public static (int Width, int Height) GetPrimaryScreenSize()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return GetWindowsScreenSize();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return GetMacScreenSize();
                return GetLinuxScreenSize();
            }
            catch { }

            // Fallback conservador si todo falla
            return (1920, 1080);
        }

        // ---------- Windows: P/Invoke puro a user32.dll ----------

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int SM_CXSCREEN = 0;  // ancho de la pantalla primaria
        private const int SM_CYSCREEN = 1;  // alto de la pantalla primaria
        private const uint SPI_GETWORKAREA = 0x0030;

        private static (int Width, int Height) GetWindowsScreenSize()
        {
            // Área de trabajo: excluye la barra de tareas y respeta la posición
            var rect = new RECT();
            if (SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0))
                return (rect.Right - rect.Left, rect.Bottom - rect.Top);

            // Fallback: pantalla completa
            return (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        }

        // ---------- macOS: osascript (sin dependencias .NET) ----------

        private static (int Width, int Height) GetMacScreenSize()
        {
            string? output = RunAndRead("/usr/bin/osascript", new[]
            {
                "-e", "tell application \"Finder\" to get bounds of window of desktop"
            });

            // Salida típica: "0, 0, 1920, 1080"
            if (!string.IsNullOrEmpty(output))
            {
                var parts = output.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 4 &&
                    int.TryParse(parts[2], out int w) &&
                    int.TryParse(parts[3], out int h))
                    return (w, h);
            }
            return (1920, 1080);
        }

        // ---------- Linux: xrandr → xdpyinfo → /sys/class/drm ----------

        private static (int Width, int Height) GetLinuxScreenSize()
        {
            // 1) xrandr (X11). Buscamos la línea del modo actual, marcada con '*'
            string? xrandr = RunAndRead("/usr/bin/xrandr", new[] { "--current" });
            if (!string.IsNullOrEmpty(xrandr))
            {
                foreach (var line in xrandr.Split('\n'))
                {
                    if (!line.Contains('*')) continue;
                    string trimmed = line.Trim();
                    int plus = trimmed.IndexOf('+');
                    string sizePart = plus > 0
                        ? trimmed.Substring(0, plus)
                        : trimmed.Split(' ')[0];
                    var dims = sizePart.Split('x');
                    if (dims.Length == 2 &&
                        int.TryParse(dims[0], out int w) &&
                        int.TryParse(dims[1], out int h))
                        return (w, h);
                }
            }

            // 2) xdpyinfo (X11 alternativo)
            string? xdpy = RunAndRead("/usr/bin/xdpyinfo", Array.Empty<string>());
            if (!string.IsNullOrEmpty(xdpy))
            {
                foreach (var line in xdpy.Split('\n'))
                {
                    if (!line.Contains("dimensions:")) continue;
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var dims = part.Split('x');
                        if (dims.Length == 2 &&
                            int.TryParse(dims[0], out int w) &&
                            int.TryParse(dims[1], out int h))
                            return (w, h);
                    }
                }
            }

            // 3) Fallback vía DRM: cubre Wayland y sistemas sin xrandr/xdpyinfo
            try
            {
                foreach (var dir in Directory.GetDirectories("/sys/class/drm", "card*-*"))
                {
                    string statusPath = Path.Combine(dir, "status");
                    if (!File.Exists(statusPath)) continue;
                    if (File.ReadAllText(statusPath).Trim() != "connected") continue;

                    string modesPath = Path.Combine(dir, "modes");
                    if (!File.Exists(modesPath)) continue;

                    string? firstMode = File.ReadLines(modesPath).FirstOrDefault();
                    if (string.IsNullOrEmpty(firstMode)) continue;

                    var dims = firstMode.Split('x');
                    if (dims.Length == 2 &&
                        int.TryParse(dims[0], out int w) &&
                        int.TryParse(dims[1], out int h))
                        return (w, h);
                }
            }
            catch { }

            return (1920, 1080);
        }

        /// <summary>
        /// Ejecuta un proceso, lee su stdout y devuelve el texto (trim). Devuelve null si falla,
        /// tarda más de <paramref name="timeoutMs"/> o no se puede lanzar.
        /// </summary>
        private static string? RunAndRead(string fileName, string[] args, int timeoutMs = 3000)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc is null) return null;

                // Lanzamos las lecturas async antes de esperar al proceso.
                // Así evitamos el deadlock típico cuando el buffer del pipe se llena.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignorar */ }
                    return null;
                }

                // El WaitForExit(int) NO drena los streams async. Hay que llamar
                // al WaitForExit() sin timeout para garantizar que se completen.
                proc.WaitForExit();

                string output = stdoutTask.GetAwaiter().GetResult().Trim();
                _ = stderrTask.GetAwaiter().GetResult(); // drenar stderr, no nos interesa su contenido
                return output;
            }
            catch
            {
                return null;
            }
        }
    }
}