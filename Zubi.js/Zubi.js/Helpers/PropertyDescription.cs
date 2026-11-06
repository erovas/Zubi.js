namespace Zubi.js.Helpers
{
    internal sealed class PropertyDescription
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;          // tipo amigable: "string?", "List<int>"
        public string TypeFullName { get; set; } = string.Empty;  // tipo .NET real
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public string Accessors { get; set; } = string.Empty;     // "get;set;" | "get;" | "set;"
        public bool IsStatic { get; set; }
        public bool IsVirtual { get; set; }
    }
}