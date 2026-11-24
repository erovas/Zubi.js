namespace Windows.DTOs
{
    public sealed class WindowLocation
    {
        public int X { get; set; }
        public int Y { get; set; }

        public WindowLocation(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}