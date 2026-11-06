namespace Zubi.js.Helpers
{
    internal sealed class TypeDescription
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public bool IsAbstract { get; set; } 
        public bool IsSealed { get; set; }
        public string BaseType { get; set; } = string.Empty;
        public List<string> Interfaces { get; set; } = new();
        public List<PropertyDescription> Properties { get; set; } = new();
        public List<MethodDescription> Methods { get; set; } = new();
        public List<ConstructorDescription> Constructors { get; set; } = new();
    }
}