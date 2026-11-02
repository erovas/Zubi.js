namespace Zubi.js
{
    internal enum BrowserState
    {
        Normal,     // Estado por defecto
        Maximized,  // Ventana maximizada (con bordes)
        Fullscreen, // Pantalla completa (con F11 para salir)
        Kiosk       // Pantalla completa bloqueada (sin salida fácil)
    }
}