namespace CDP.DTOs
{
    // Representa un punto de contacto táctil
    public class TouchPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double RadiusX { get; set; } = 1.0;
        public double RadiusY { get; set; } = 1.0;
        public double RotationAngle { get; set; } = 0.0;
        public double Force { get; set; } = 1.0;
    }
}