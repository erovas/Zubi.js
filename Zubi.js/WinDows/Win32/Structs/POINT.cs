using System.Runtime.InteropServices;

namespace Windows.Win32.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }
}