using System.Text.Json;
using Zubi.js.Helpers;

namespace Zubi.js
{
    internal sealed class Config
    {
        public int Port { get; init; }
        public string[] AllowedOrigins { get; init; } = [];
        public int ShutdownSeconds { get; init; } = 3;
        public ConfigBrowser Browser { get; init; } = new();

        public static Config Load(string path)
        {
            if (!File.Exists(path)) 
                return new Config();

            try
            {
                return Utils.Deserialize<Config>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"config.json inválido ({ex.Message}). Usando defaults.");
                return new Config();
            }
        }
    }

    internal sealed class ConfigBrowser
    {
        public string Name { get; init; } = string.Empty;
        public ProfileMode Mode { get; init; } = ProfileMode.Named;
        public BrowserState State { get; init; } = BrowserState.Normal;
        public BrowserTheme Theme { get; init; } = BrowserTheme.System;
        public int Width { get; init; } = -1;
        public int Height { get; init; } = -1;
    }
}