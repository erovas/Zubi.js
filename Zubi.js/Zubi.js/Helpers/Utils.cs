using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zubi.js.Helpers
{
    internal static class Utils
    {
        public const string INDEX_HTML = "index.html";

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new Base64ByteArrayConverter() }
        };

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

        public static string ReplaceFirst(string text, string search, string replace)
        {
            int pos = text.IndexOf(search);

            if (pos < 0) 
                return text; // No encontrado

            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        public static string JsonSerialize(object value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        public static object? ConvertArg(object? raw, Type target)
        {
            if (raw is JsonElement el)
                return el.Deserialize(target, Options);

            if (raw is null)
                return null;

            if (target.IsInstanceOfType(raw))
                return raw;

            var json = JsonSerializer.Serialize(raw, Options);
            return JsonSerializer.Deserialize(json, target, Options);
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
                    throw new InvalidOperationException($"'{methodName}' no existe en '{name}'");

                if (prop.CanRead && args.Length == 0)
                    return prop.GetValue(source);

                else if (prop.CanWrite && args.Length == 1)
                {
                    prop.SetValue(source, Utils.ConvertArg(args[0], prop.PropertyType));
                    return null;
                }

                throw new InvalidOperationException($"Demasiados argumentos para '{methodName}' en '{name}'");
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
                    throw new CodeException("EINVAL", $"Falta el argumento '{p.Name}'");
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
    }
}
