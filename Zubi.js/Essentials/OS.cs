using Zubi.js;
using MyApp = Zubi.js.App;
using Plat = Zubi.js.Platform;
using System.Runtime.InteropServices;

namespace Essentials
{
    public sealed class OS : Plugin
    {
        public string Platform
        {
            get 
            {
                switch (MyApp.Platform)
                {
                    case Plat.Windows:
                        return "win32";
                    case Plat.Linux:
                        return "linux";
                    case Plat.OSX:
                        return "darwin";
                    default:
                        return "unknown";
                }
            }
        }
        public string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        public int ProcessorCount => Environment.ProcessorCount;
        public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public string TempDirectory => Path.GetTempPath();
        public string CurrentDirectory => Environment.CurrentDirectory;
        public string UserName => Environment.UserName;
        public string MachineName => Environment.MachineName;
        public string OsVersion => Environment.OSVersion.VersionString;
        public string RuntimeVersion => Environment.Version.ToString();


        public string? GetEnvironmentVariable(string name) 
        {  
            return Environment.GetEnvironmentVariable(name); 
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        public Dictionary<string, string> GetAllEnvironmentVariables()
        {
            var d = new Dictionary<string, string>();
            foreach (var k in Environment.GetEnvironmentVariables().Keys)
            {
                var key = k?.ToString();
                if (key is not null) d[key] = Environment.GetEnvironmentVariable(key) ?? "";
            }
            return d;
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing
        }
    }
}