namespace Windows.DTOs
{
    public sealed class WindowRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        internal WindowSize Size => new WindowSize(Width, Height);
        internal WindowLocation Location => new WindowLocation(X, Y);

        public WindowRect() { }

        public WindowRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        internal bool SizeEquals(WindowRect other)
        {
            return this.Width == other.Width && this.Height == other.Height;
        }

        internal bool LocationEquals(WindowRect other) 
        {
            return this.X == other.X && this.Y == other.Y;
        }
    }
}