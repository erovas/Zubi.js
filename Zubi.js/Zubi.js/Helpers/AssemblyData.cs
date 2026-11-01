namespace Zubi.js.Helpers
{
    internal sealed class AssemblyData
    {
        public string Path { get; }
        public string Name { get; }
        public string[] Plugins { get; }

        public AssemblyData(string path, string name, string[] plugins)
        {
            Path = path;
            Name = name;
            Plugins = plugins;
        }
    }
}