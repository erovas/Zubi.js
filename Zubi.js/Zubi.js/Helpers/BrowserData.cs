namespace Zubi.js
{
    public sealed class BrowserData
    {
        public string Name { get; }

        public BrowserEngine Engine { get; }

        public string Path { get; }

        public string Href { get; }

        public int DebugPort { get; }

        internal BrowserData(string name, BrowserEngine engine, string path = "", string href = "", int debugPort = 0)
        {
            Name = name;
            Engine = engine;
            Path = path;
            Href = href;
            DebugPort = debugPort;
        }
    }
}