using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.DTOs;
using Windows.Enums;
using Windows.Win32.Enums;
using Windows.Win32.Structs;
using Zubi.js;

namespace Windows.Win32
{
    internal static class Utils32
    {

        public static WindowState GetWindowState(IntPtr hwnd)
        {
            if (User32.IsIconic(hwnd))
                return WindowState.Minimized;

            if (User32.IsZoomed(hwnd))
                return WindowState.Maximized;

            return WindowState.Normalized;
        }

        public static bool SetWindowState(IntPtr hwnd, WindowState state)
        {
            if (GetWindowState(hwnd) == state)
                return true;

            ShowWinCmd action = state switch
            {
                WindowState.Minimized => ShowWinCmd.SW_MINIMIZE,
                WindowState.Normalized => ShowWinCmd.SW_NORMAL,   // SW_SHOWNORMAL
                WindowState.Maximized => ShowWinCmd.SW_MAXIMIZE,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };

            User32.ShowWindow(hwnd, action);
            return GetWindowState(hwnd) == state;
        }

        public static bool GetWindowTopMost(IntPtr hwnd)
        {
            int ex = User32.GetWindowLong(hwnd, -20 /* GWL_EXSTYLE */);
            return (ex & (int)WinStyles.WS_EX_TOPMOST) != 0;
        }

        public static void SetWindowTopMost(IntPtr hwnd, bool topmost)
        {
            User32.SetWindowPos(
                    hwnd,
                    topmost ? new nint(-1) : new nint(-2),
                    0, 0, //posX, posY, 
                    0, 0, //Width, Height, 
                    (uint)(SetWinPos.SWP_NOMOVE | SetWinPos.SWP_NOSIZE | SetWinPos.SWP_NOACTIVATE)
                );
        }

        public static bool WindowRestore(IntPtr hwnd)
        {
            // Si no está minimizada, ya está restaurada.
            if (!User32.IsIconic(hwnd))
                return true;

            // SW_RESTORE restaura al estado anterior (normal o maximizada)
            // usando internamente WPF_RESTORETOMAXIMIZED. Además activa la ventana.
            User32.ShowWindow(hwnd, ShowWinCmd.SW_RESTORE);

            return !User32.IsIconic(hwnd);
        }

        public static bool WindowRestore(IntPtr hwnd, WindowState lastState)
        {
            if (!User32.IsIconic(hwnd))
                return true;

            ShowWinCmd action = lastState switch
            {
                WindowState.Maximized => ShowWinCmd.SW_MAXIMIZE,
                WindowState.Normalized => ShowWinCmd.SW_NORMAL,
                _ => ShowWinCmd.SW_RESTORE
            };

            User32.ShowWindow(hwnd, action);
            return !User32.IsIconic(hwnd);
        }

        public static bool WindowIsActive(IntPtr hwnd)
        {
            return User32.GetForegroundWindow() == hwnd;
        }

        public static bool GetWindowVisible(IntPtr hwnd)
        {
            return User32.IsWindowVisible(hwnd);
        }

        public static void SetWindowVisible(IntPtr hwnd, bool visible)
        {
            if (GetWindowVisible(hwnd) == visible)
                return;

            User32.ShowWindow(hwnd, visible ? ShowWinCmd.SW_SHOWNOACTIVATE : ShowWinCmd.SW_HIDE);
        }

        public static bool WindowActivate(IntPtr hwnd)
        {
            // Solo activamos si ya está visible (WS_VISIBLE).
            if (!GetWindowVisible(hwnd))
                return false;

            if (User32.SetForegroundWindow(hwnd))
                return true;

            // Fallback: SW_SHOW sobre una ventana ya visible la activa.
            User32.ShowWindow(hwnd, ShowWinCmd.SW_SHOW);
            return User32.GetForegroundWindow() == hwnd;
        }

        public static WindowRect GetWindowRect(IntPtr hwnd)
        {
            // La ventana está actualmente Minimizada
            if (GetWindowState(hwnd) == WindowState.Minimized)
            {
                WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
                placement.length = Marshal.SizeOf(placement);
                User32.GetWindowPlacement(hwnd, ref placement);

                // Estaba Normalizada.
                if (!((placement.flags & 0x0002 /* WPF_RESTORETOMAXIMIZED */) != 0))
                    return placement.rcNormalPosition.Rect;

                // Estaba maximizada: Encontrar el monitor asociado a ptMaxPosition
                IntPtr hMonitor = User32.MonitorFromWindow(hwnd, 0x00000002 /* MONITOR_DEFAULTTONEAREST */);
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
                User32.GetMonitorInfo(hMonitor, ref monitorInfo);
                return monitorInfo.rcWork.Rect;
            }

            // La ventana esta Normalizada o Maximizada
            User32.GetWindowRect(hwnd, out RECT rect);

            return rect.Rect;
        }

        public static bool SetWindowRect(IntPtr hwnd, int x, int y, int width, int height)
        {
            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);

            // 1. Leer la configuración actual (incluye el estado actual y la geometría de restauración)
            if (!User32.GetWindowPlacement(hwnd, ref placement))
                return false;

            // 2. Modificar SOLO el rectángulo de restauración (la geometría para cuando NO esté minimizada/maximizada)
            placement.rcNormalPosition.Left = x;
            placement.rcNormalPosition.Top = y;
            placement.rcNormalPosition.Right = x + width;
            placement.rcNormalPosition.Bottom = y + height;

            // 3. Aplicar de nuevo la configuración SIN tocar placement.showCmd.
            //    Si estaba minimizada, showCmd sigue siendo SW_SHOWMINIMIZED, por lo que permanecerá minimizada.
            return User32.SetWindowPlacement(hwnd, ref placement);
        }

        public static bool SetWindowRect(IntPtr hwnd, WindowRect rect)
        {
            return SetWindowRect(hwnd, rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static bool GetWindowEnabled(IntPtr hwnd)
        {
            return User32.IsWindowEnabled(hwnd);
        }

        public static bool SetWindowEnabled(IntPtr hwnd, bool enabled)
        {
            if (GetWindowEnabled(hwnd) == enabled)
                return true;

            User32.EnableWindow(hwnd, enabled);
            return User32.IsWindowEnabled(hwnd) == enabled;
        }

        public static bool WindowToCenter(IntPtr hwnd)
        {
            var wp = new WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf<WINDOWPLACEMENT>();

            if (!User32.GetWindowPlacement(hwnd, ref wp))
                return false;

            // Tamaño de restauración (aunque esté minimizada o maximizada)
            int width = wp.rcNormalPosition.Width;
            int height = wp.rcNormalPosition.Height;

            // Monitor: como WinForms → visible = monitor de la ventana; oculta = monitor del cursor
            IntPtr hMonitor;
            if (User32.IsWindowVisible(hwnd))
            {
                hMonitor = User32.MonitorFromWindow(hwnd, 0x00000002 /* MONITOR_DEFAULTTONEAREST */);
            }
            else
            {
                User32.GetCursorPos(out POINT pt);
                hMonitor = User32.MonitorFromPoint(pt, 0x00000002 /* MONITOR_DEFAULTTONEAREST */);
            }
            if (hMonitor == IntPtr.Zero)
                return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!User32.GetMonitorInfo(hMonitor, ref mi))
                return false;

            // Si la ventana es más grande que el área de trabajo, centrar sobre los bounds completos
            int workW = mi.rcWork.Width;
            int workH = mi.rcWork.Height;
            RECT area = (width > workW || height > workH) ? mi.rcMonitor : mi.rcWork;

            int x = area.Left + (area.Right - area.Left - width) / 2;
            int y = area.Top + (area.Bottom - area.Top - height) / 2;

            if (wp.showCmd == (int)ShowWinCmd.SW_SHOWNORMAL)
            {
                // Ventana Normal: mover visiblemente sin tocar tamaño, z-order ni activar
                return User32.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                    (uint)(SetWinPos.SWP_NOSIZE | SetWinPos.SWP_NOZORDER | SetWinPos.SWP_NOACTIVATE));
            }
            else
            {
                // Minimizada/Maximizada: solo actualizar la posición de restauración.
                // No tocamos showCmd → el estado actual se conserva.
                wp.rcNormalPosition.Left = x;
                wp.rcNormalPosition.Top = y;
                wp.rcNormalPosition.Right = x + width;
                wp.rcNormalPosition.Bottom = y + height;
                return User32.SetWindowPlacement(hwnd, ref wp);
            }
        }

        public static void WindowDrag(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTCAPTION, 0);
        }

        public static void WindowResizeTopLeft(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTTOPLEFT, 0);

        }

        public static void WindowResizeTopRight(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTTOPRIGHT, 0);
        }

        public static void WindowResizeBottomLeft(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTBOTTOMLEFT, 0);
        }

        public static void WindowResizeBottomRight(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTBOTTOMRIGHT, 0);
        }

        public static void WindowResizeLeft(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTLEFT, 0);

        }

        public static void WindowResizeRight(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTRIGHT, 0);

        }

        public static void WindowResizeTop(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTTOP, 0);

        }

        public static void WindowResizeBottom(IntPtr hwnd)
        {
            User32.ReleaseCapture();
            _ = User32.SendMessage(hwnd, (uint)WinMsg.WM_NCLBUTTONDOWN, (int)WM_NCHITTEST.HTBOTTOM, 0);
        }

        public static RECT GetWorkArea(IntPtr hwnd)
        {
            IntPtr hMonitor = User32.MonitorFromWindow(hwnd, 0x00000000 /* MONITOR_DEFAULTTONULL */);
            if (hMonitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (User32.GetMonitorInfo(hMonitor, ref mi))
                    return mi.rcWork;
            }

            // Fallback: área de trabajo del primario
            var workArea = new RECT();
            User32.SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref workArea, 0);
            return workArea;
        }

        /// <summary>
        /// Intenta resolver el HWND principal del proceso.
        /// </summary>
        /// <param name="process"></param>
        /// <param name="browser"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static IntPtr GetHandle(Process process, BrowserData browser)
        {
            if (_Handle != IntPtr.Zero)
                return _Handle;

            IntPtr hwnd = Utils32.GetMainWindow(process);

            if (hwnd == IntPtr.Zero)
                hwnd = Utils32.GetMainWindowByExe(browser.Path, timeoutMs: 3000);

            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Handler not found.");

            _Handle = hwnd;
            return hwnd;
        }

        #region Helpers

        private static IntPtr _Handle;

        /// <summary>
        /// Intenta resolver el HWND principal del proceso. Devuelve IntPtr.Zero si no lo encuentra.
        /// No lanza. Pensado para invocarse ya con la ventana abierta (plugin cargado post-launch).
        /// </summary>
        private static IntPtr GetMainWindow(Process? proc, int timeoutMs = 3000)
        {
            if (proc is null) return IntPtr.Zero;

            var sw = Stopwatch.StartNew();

            // 1) Camino rápido: MainWindowHandle (hay que Refresh() para que no devuelva 0 cacheado).
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (proc.HasExited)
                    break;

                try { proc.Refresh(); }
                catch { break; }

                if (proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;

                Thread.Sleep(80);
            }

            // 2) Fallback por PID (por si la ventana la creó un hilo/hijo distinto o HasExited cambió).
            var remaining = Math.Max(0, timeoutMs - (int)sw.ElapsedMilliseconds);
            var byPid = FindTopLevelWindowByPid(proc.Id, remaining);

            if (byPid != IntPtr.Zero)
                return byPid;

            // 3) Fallback por nombre de ejecutable (Arc/Store, Chromium Shared con hand-off,
            //    launcher que ya terminó, etc.).
            var exeName = TryGetProcessName(proc);
            if (!string.IsNullOrEmpty(exeName))
            {
                remaining = Math.Max(0, timeoutMs - (int)sw.ElapsedMilliseconds);
                return FindTopLevelWindowByProcessName(exeName!, remaining);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Igual que <see cref="GetMainWindow"/> pero para cuando sólo tienes la ruta del ejecutable
        /// (p. ej. <c>App.Browser.ExecutablePath</c>) y el proceso original ya ha terminado.
        /// </summary>
        private static IntPtr GetMainWindowByExe(string executablePath, int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                return IntPtr.Zero;

            var name = Path.GetFileNameWithoutExtension(executablePath);
            return FindTopLevelWindowByProcessName(name, timeoutMs);
        }

        private static IntPtr FindTopLevelWindowByProcessName(string processName, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            do
            {
                IntPtr best = IntPtr.Zero;
                int bestArea = 0;

                Process[] procs;
                try { procs = Process.GetProcessesByName(processName); }
                catch { procs = Array.Empty<Process>(); }

                foreach (var p in procs)
                {
                    try
                    {
                        var h = FindTopLevelWindowByPid(p.Id, timeoutMs: 0);
                        if (h == IntPtr.Zero)
                            continue;

                        if (!User32.GetWindowRect(h, out var r))
                            continue;

                        int area = (r.Right - r.Left) * (r.Bottom - r.Top);
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = h;
                        }
                    }
                    finally { p.Dispose(); }
                }

                if (best != IntPtr.Zero) return best;
                Thread.Sleep(120);
            } while (sw.ElapsedMilliseconds < timeoutMs);

            return IntPtr.Zero;
        }

        private static IntPtr FindTopLevelWindowByPid(int pid, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            do
            {
                IntPtr best = IntPtr.Zero;
                int bestArea = 0;

                User32.EnumWindows((hwnd, _) =>
                {
                    if (!User32.IsWindowVisible(hwnd))
                        return true;

                    User32.GetWindowThreadProcessId(hwnd, out var wndPid);

                    if (wndPid != (uint)pid)
                        return true;

                    // Ventanas con título y tamaño razonable (descarta tooltips, overlays, etc.).
                    if (User32.GetWindowTextLength(hwnd) == 0)
                        return true;

                    if (!User32.GetWindowRect(hwnd, out var r))
                        return true;

                    int area = (r.Right - r.Left) * (r.Bottom - r.Top);

                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = hwnd;
                    }

                    return true;
                }, IntPtr.Zero);

                if (best != IntPtr.Zero)
                    return best;

                Thread.Sleep(100);
            } while (sw.ElapsedMilliseconds < timeoutMs);

            return IntPtr.Zero;
        }

        private static string? TryGetProcessName(Process proc)
        {
            try { return proc.ProcessName; }
            catch { return null; }
        }

        public static void CheckWindowHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
                throw new ArgumentException("Invalid window handle.", nameof(hwnd));
        }

        #endregion

    }
}