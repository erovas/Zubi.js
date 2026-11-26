using System.Runtime.InteropServices;

namespace Windows.Win32.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }
}