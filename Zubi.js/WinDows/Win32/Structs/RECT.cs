using System.Drawing;
using System.Runtime.InteropServices;
using Windows.DTOs;

namespace Windows.Win32.Structs
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        /// <summary>
        /// Specifies the <i>x</i>-coordinate of the upper-Left corner of the rectangle.
        /// </summary>
        public int Left;

        /// <summary>
        /// Specifies the <i>y</i>-coordinate of the upper-Left corner of the rectangle.
        /// </summary>
        public int Top;

        /// <summary>
        /// Specifies the <i>x</i>-coordinate of the lower-Right corner of the rectangle.
        /// </summary>
        public int Right;

        /// <summary>Specifies the <i>y</i>-coordinate of the lower-Right corner of the rectangle.</summary>
        public int Bottom;

        internal RECT(Rectangle value) : this(value.Left, value.Top, value.Right, value.Bottom)
        {
        }

        internal RECT(Point location, Size size) : this(location.X, location.Y, unchecked(location.X + size.Width), unchecked(location.Y + size.Height))
        {
        }

        internal RECT(int left, int top, int right, int bottom)
        {
            this.Left = left;
            this.Top = top;
            this.Right = right;
            this.Bottom = bottom;
        }

        internal readonly int Width => this.Right - this.Left;

        internal readonly int Height => this.Bottom - this.Top;

        internal readonly bool IsEmpty => this.Left == 0 && this.Top == 0 && this.Right == 0 && this.Bottom == 0;

        internal readonly int X => this.Left;

        internal readonly int Y => this.Top;

        internal readonly Size Size => new(this.Width, this.Height);

        internal readonly WindowRect Rect => new(this.X, this.Y, this.Width, this.Height);
    }
}