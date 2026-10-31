namespace Zubi.js
{
    public sealed class BrowserData
    {
        public string Name { get; }

        public BrowserEngine Engine { get; }

        public string Path { get; }

        internal BrowserData(string name, BrowserEngine engine, string path)
        {
            Name = name;
            Engine = engine;
            Path = path;
        }
    }
}