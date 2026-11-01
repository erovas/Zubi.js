using System.Net;
using System.Text.Json;
using System.Reflection;
using System.Net.Sockets;
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

    }
}