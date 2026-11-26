using System.Runtime.InteropServices;

namespace Windows.Win32.Structs
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
}