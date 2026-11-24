using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Enums;
using Windows.Win32.Structs;

namespace Windows.Win32
{
    /// <summary>
    /// Arrastre y redimensionado de ventanas externas mediante WH_MOUSE_LL + SetWindowPos.
    /// No depende de que el proceso objetivo procese WM_NCLBUTTONDOWN (Chromium, Gecko, etc.).
    /// </summary>
    internal sealed class WindowSizingController : IDisposable
    {

        // ── Constantes internas ──────────────────────────────────────
        private const int WH_MOUSE_LL = 14;
        private const int VK_LBUTTON = 0x01;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_QUIT = 0x0012;
        private const uint WM_APP_BEGIN_GESTURE = 0x8000 + 200;

        private const int MIN_WIDTH = 200;
        private const int MIN_HEIGHT = 200;


        // ── Estado del hilo (autogestionado) ─────────────────────────
        private readonly object _startLock = new();
        private Thread? _thread;
        private uint _threadId;
        private volatile bool _running;
        private readonly ManualResetEventSlim _ready = new(false);

        // ── Estado del gesto (solo hilo propio) ──────────────────────
        private IntPtr _mouseHookHandle = IntPtr.Zero;
        private User32.LowLevelMouseProc? _mouseProcKeepAlive;
        private WM_NCHITTEST _hitTest = WM_NCHITTEST.NONE;   // 0 = inactivo
        private IntPtr _targetHwnd;
        private RECT _startRect;
        private POINT _startCursor;
        private IntPtr _hwnd;

        // ── API pública ──────────────────────────────────────────────
        public void Drag()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTCAPTION);
        }

        public void ResizeTopLeft()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTTOPLEFT);
        }

        public void ResizeTopRight()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTTOPRIGHT);
        }

        public void ResizeBottomLeft()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTBOTTOMLEFT);
        }

        public void ResizeBottomRight()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTBOTTOMRIGHT);
        }

        public void ResizeLeft()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTLEFT);
        }

        public void ResizeRight()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTRIGHT);
        }

        public void ResizeTop()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTTOP);
        }

        public void ResizeBottom()
        {
            BeginGesture(_hwnd, WM_NCHITTEST.HTBOTTOM);
        }

        public WindowSizingController(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public void Dispose()
        {
            Stop();
        }

        // ── Arranque perezoso del hilo ───────────────────────────────
        private void BeginGesture(IntPtr hwnd, WM_NCHITTEST hitTest)
        {
            if (hwnd == IntPtr.Zero || hitTest == 0)
                return;

            EnsureThreadStarted();

            if (_threadId == 0)
                return;

            User32.PostThreadMessage(_threadId, WM_APP_BEGIN_GESTURE, hwnd, (IntPtr)hitTest);
        }

        private void EnsureThreadStarted()
        {
            if (_running) return;
            lock (_startLock)
            {
                if (_running) return;
                _running = true;

                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "WindowDragResizeThread"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();

                _ready.Wait(3000);
            }
        }

        private void Stop()
        {
            if (!_running) return;
            _running = false;

            if (_threadId != 0)
                User32.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _thread?.Join(2000);
            _thread = null;
            _threadId = 0;
        }

        // ── Bucle del hilo ───────────────────────────────────────────
        private void Run()
        {
            _threadId = User32.GetCurrentThreadId();
            _ready.Set();

            try
            {
                while (_running && User32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
                {
                    if (msg.message == WM_APP_BEGIN_GESTURE)
                    {
                        StartGesture(msg.wParam, (WM_NCHITTEST)msg.lParam);
                        continue;
                    }

                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
                }
            }
            finally
            {
                StopGesture();
            }
        }

        // ── Ciclo del gesto ──────────────────────────────────────────
        private void StartGesture(IntPtr hwnd, WM_NCHITTEST hitTest)
        {
            if (_hitTest != 0 || hwnd == IntPtr.Zero)
                return;

            // Si el botón izquierdo no está pulsado, no hay gesto real.
            //if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) 
            //    return;

            User32.GetCursorPos(out _startCursor);
            User32.GetWindowRect(hwnd, out _startRect);

            _targetHwnd = hwnd;
            _hitTest = hitTest;

            if (_mouseHookHandle == IntPtr.Zero)
            {
                _mouseProcKeepAlive = LowLevelMouseProcCallback;
                _mouseHookHandle = User32.SetWindowsHookEx(WH_MOUSE_LL, _mouseProcKeepAlive, IntPtr.Zero, 0);
            }
        }

        private void StopGesture()
        {
            _hitTest = 0;
            if (_mouseHookHandle != IntPtr.Zero)
            {
                User32.UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }
            _mouseProcKeepAlive = null;
        }

        // ── Callback del hook global ─────────────────────────────────
        private IntPtr LowLevelMouseProcCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _hitTest != 0)
            {
                uint msg = (uint)wParam.ToInt64();

                if (msg == WM_MOUSEMOVE && User32.GetCursorPos(out var cursor))
                    ApplyGeometry(cursor);

                else if (msg == WM_LBUTTONUP)
                    StopGesture();
            }

            return User32.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        // ── Cálculo de la nueva geometría ────────────────────────────
        private void ApplyGeometry(POINT cursor)
        {
            int dx = cursor.x - _startCursor.x;
            int dy = cursor.y - _startCursor.y;

            int x = _startRect.Left;
            int y = _startRect.Top;
            int w = _startRect.Right - _startRect.Left;
            int h = _startRect.Bottom - _startRect.Top;

            switch (_hitTest)
            {
                case WM_NCHITTEST.HTCAPTION:
                    x += dx;
                    y += dy;
                    break;

                case WM_NCHITTEST.HTLEFT:
                    x += dx;
                    w -= dx;
                    break;

                case WM_NCHITTEST.HTRIGHT:
                    w += dx;
                    break;

                case WM_NCHITTEST.HTTOP:
                    y += dy;
                    h -= dy;
                    break;

                case WM_NCHITTEST.HTBOTTOM:
                    h += dy;
                    break;

                case WM_NCHITTEST.HTTOPLEFT:
                    x += dx;
                    y += dy;
                    w -= dx;
                    h -= dy;
                    break;

                case WM_NCHITTEST.HTTOPRIGHT:
                    y += dy;
                    w += dx;
                    h -= dy;
                    break;

                case WM_NCHITTEST.HTBOTTOMLEFT:
                    x += dx;
                    w -= dx;
                    h += dy;
                    break;

                case WM_NCHITTEST.HTBOTTOMRIGHT:
                    w += dx;
                    h += dy;
                    break;
            }

            // Tamaño mínimo: anclar el borde opuesto.
            if (w < MIN_WIDTH)
            {
                if (_hitTest == WM_NCHITTEST.HTLEFT || _hitTest == WM_NCHITTEST.HTTOPLEFT || _hitTest == WM_NCHITTEST.HTBOTTOMLEFT)
                    x = _startRect.Right - MIN_WIDTH;
                w = MIN_WIDTH;
            }
            if (h < MIN_HEIGHT)
            {
                if (_hitTest == WM_NCHITTEST.HTTOP || _hitTest == WM_NCHITTEST.HTTOPLEFT || _hitTest == WM_NCHITTEST.HTTOPRIGHT)
                    y = _startRect.Bottom - MIN_HEIGHT;
                h = MIN_HEIGHT;
            }

            User32.SetWindowPos(_targetHwnd, IntPtr.Zero, x, y, w, h, (uint)(SetWinPos.SWP_NOZORDER | SetWinPos.SWP_NOACTIVATE));
        }
    }
}