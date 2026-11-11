namespace CDP.DTOs
{
    public class JavaScriptDialogEventArgs
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string? DefaultPrompt { get; set; }
    }
}