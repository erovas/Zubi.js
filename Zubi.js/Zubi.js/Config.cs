using System.Text.Json;

namespace Zubi.js
{
    internal sealed class Config
    {
        public int DebuggingPort { get; init; }
        public string[] Plugins { get; init; } = [];
        public string[] AllowedOrigins { get; init; } = [];
        public int ShutdownGraceSeconds { get; init; } = 3;

        public static Config Load(string path)
        {
            if (!File.Exists(path)) 
                return new Config();

            try
            {
                return JsonSerializer.Deserialize<Config>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) ?? new Config();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"config.json inválido ({ex.Message}). Usando defaults.");
                return new Config();
            }
        }
    }
}