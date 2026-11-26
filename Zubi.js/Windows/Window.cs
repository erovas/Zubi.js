using Windows.DTOs;
using Windows.Enums;
using Windows.Win32;
using Zubi.js;

namespace Windows
{
    public sealed class Window : Plugin
    {
        private static bool Initialized;
        private readonly IntPtr Handle;
        private static WindowEventHook? hook;
        private static WindowSizingController? dragResize;

        #region Events

        public event EventHandler<WindowBoundsEventArgs>? SizeChanged;
        public event EventHandler<WindowBoundsEventArgs>? Moved;
        public event EventHandler<WindowStateChangedEventArgs>? StateChanged;
        public event EventHandler<WindowActiveEventArgs>? ActiveChanged;
        public event EventHandler<WindowEnabledEventArgs>? EnabledChanged;
        public event EventHandler<WindowVisibilityEventArgs>? VisibilityChanged;

        #endregion

        public Window()
        {
            // Comprobar que es la plataforma correcta
            if (App.Platform != Platform.Windows)
                throw new InvalidOperationException("Unsupported platform.");

            if (Initialized)
                throw new InvalidOperationException("An instance of Window already exists.");

            Handle = Handle == IntPtr.Zero ? Utils32.GetHandle(App.Process, App.Browser) : Handle;
            Utils32.CheckWindowHandle(Handle);
            hook = new(Handle)
            {
                SizeChanged = e => SizeChanged?.Invoke(this, e),
                Moved = e => Moved?.Invoke(this, e),
                StateChanged = e => StateChanged?.Invoke(this, e),
                ActiveChanged = e => ActiveChanged?.Invoke(this, e),
                VisibilityChanged = e => VisibilityChanged?.Invoke(this, e),
            };

            dragResize = new WindowSizingController(Handle);
            Initialized = true;
            hook.Start();
        }

        #region Properties

        public WindowState State
        {
            get => Utils32.GetWindowState(Handle);
            set => Utils32.SetWindowState(Handle, value);
        }

        public WindowRect Rect
        {
            get => Utils32.GetWindowRect(Handle);
            set => Utils32.SetWindowRect(Handle, value);
        }

        public WindowSize Size
        {
            get => Rect.Size;
            set => SetSize(value.Width, value.Height);
        }

        public WindowLocation Location
        {
            get => Rect.Location;
            set => SetLocation(value.X, value.Y);
        }

        public int Width
        {
            get => Rect.Width;
            set => SetSize(value, Height);
        }

        public int Height
        {
            get => Rect.Height;
            set => SetSize(Width, value);
        }

        public int X
        {
            get => Rect.X;
            set => SetLocation(value, Y);
        }

        public int Y
        {
            get => Rect.Y;
            set => SetLocation(X, value);
        }

        public bool TopMost
        {
            get => Utils32.GetWindowTopMost(Handle);
            set => Utils32.SetWindowTopMost(Handle, value);
        }

        public bool Enabled
        {
            get => Utils32.GetWindowEnabled(Handle);
            set => Utils32.SetWindowEnabled(Handle, value);
        }

        public bool Visible
        {
            get => Utils32.GetWindowVisible(Handle);
            set => Utils32.SetWindowVisible(Handle, value);
        }

        public bool IsActive => Utils32.WindowIsActive(Handle);

        #endregion

        #region Methods

        public bool ToCenter()
        {
            return Utils32.WindowToCenter(Handle);
        }

        public bool Activate()
        {
            return Utils32.WindowActivate(Handle);
        }

        public void Drag()
        {
            dragResize?.Drag();
        }

        public void ResizeTopLeft()
        {
            dragResize?.ResizeTopLeft();
        }

        public void ResizeTopRight()
        {
            dragResize?.ResizeTopRight();
        }

        public void ResizeBottomLeft()
        {
            dragResize?.ResizeBottomLeft();
        }

        public void ResizeBottomRight()
        {
            dragResize?.ResizeBottomRight();
        }

        public void ResizeLeft()
        {
            dragResize?.ResizeLeft();
        }

        public void ResizeRight()
        {
            dragResize?.ResizeRight();
        }

        public void ResizeTop()
        {
            dragResize?.ResizeTop();
        }

        public void ResizeBottom()
        {
            dragResize?.ResizeBottom();
        }

        public bool Minimize()
        {
            return Utils32.SetWindowState(Handle, WindowState.Minimized);
        }

        public void Restore()
        {
            Utils32.WindowRestore(Handle);
        }

        public bool Maximize()
        {
            return Utils32.SetWindowState(Handle, WindowState.Maximized);
        }

        public bool Normalize()
        {
            return Utils32.SetWindowState(Handle, WindowState.Normalized);
        }

        public bool Fullscreen()
        {
            return Utils32.SetWindowState(Handle, WindowState.Fullscreen);
        }

        public void SetRect(int x, int y, int width, int height)
        {
            var rect = new WindowRect(x, y, width, height);
            Utils32.SetWindowRect(Handle, rect);
        }

        public void SetSize(int width, int height)
        {
            var rect = this.Rect;
            rect.Width = width;
            rect.Height = height;
            Utils32.SetWindowRect(Handle, rect);
        }

        public void SetLocation(int x, int y)
        {
            var rect = this.Rect;
            rect.X = x;
            rect.Y = y;
            Utils32.SetWindowRect(Handle, rect);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            dragResize?.Dispose();
            dragResize = null;
            Initialized = false;
            hook?.Dispose();
            hook = null;
        }

    }
}