namespace Zubi.js.Helpers
{
    internal sealed class MethodDescription
    {
        public string Name { get; set; } = string.Empty;
        public string ReturnType { get; set; } = string.Empty;
        public bool ReturnsVoid { get; set; }
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsAsync { get; set; }
        public List<ParameterDescription> Parameters { get; set; } = new();
    }
}