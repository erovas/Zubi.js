using Windows.Enums;

namespace Windows.DTOs
{
    // --- Bounds: se usa para SizeChanged y Moved ---
    public sealed class WindowBoundsEventArgs : EventArgs
    {
        public WindowState State { get; init; }
        public uint Timestamp { get; init; }
        public WindowRect Bounds { get; init; } = new();
        public WindowRect PreviousBounds { get; init; } = new();
        public int WidthDelta => Bounds.Width - PreviousBounds.Width;
        public int HeightDelta => Bounds.Height - PreviousBounds.Height;
    }

    // --- State: normal / maximizada / minimizada ---
    public sealed class WindowStateChangedEventArgs : EventArgs
    {
        public uint Timestamp { get; init; }
        public WindowState OldState { get; init; }
        public WindowState NewState { get; init; }
    }

    // --- Active: foco / primer plano ---
    public sealed class WindowActiveEventArgs : EventArgs
    {
        public uint Timestamp { get; init; }
        public bool IsActive { get; init; }
    }

    // --- Enabled: habilitada / deshabilitada ---
    public sealed class WindowEnabledEventArgs : EventArgs
    {
        public uint Timestamp { get; init; }
        public bool IsEnabled { get; init; }
    }

    // --- Visible: mostrar / ocultar ---
    public sealed class WindowVisibilityEventArgs : EventArgs
    {
        public uint Timestamp { get; init; }
        public bool IsVisible { get; init; }
    }
}