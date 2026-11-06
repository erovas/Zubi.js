namespace Zubi.js.Helpers
{
    internal sealed class ParameterDescription
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsOptional { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public bool IsOut { get; set; }
        public bool IsRef { get; set; }
    }
}