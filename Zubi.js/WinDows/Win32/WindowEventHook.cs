using System.Runtime.InteropServices;
using Windows.DTOs;
using Windows.Enums;
using static Windows.Win32.User32;

namespace Windows.Win32
{
    internal sealed class WindowEventHook : IDisposable
    {
        // ── Constantes WinEvent ────────────────────────────────────────────
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_OBJECT_HIDE = 0x8003;
        private const uint EVENT_OBJECT_STATECHANGE = 0x800A;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

        private const uint EVENT_MIN = 0x00000001;
        private const uint EVENT_MAX = 0x7FFFFFFF;

        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private const int OBJID_WINDOW = 0;
        private const int CHILDID_SELF = 0;

        private const uint WM_QUIT = 0x0012;

        // ── Estado del hook ────────────────────────────────────────────────
        private readonly IntPtr _targetHwnd;
        private IntPtr _hookHandle = IntPtr.Zero;
        private WinEventDelegate? _delegateKeepAlive;

        // Caché de estado para detectar cambios reales
        private WindowRect _lastBounds;
        private WindowState _lastState;
        private bool _lastVisible;
        private bool _lastEnabled;
        private bool _lastActive;

        private Thread? _thread;
        private uint _threadId;
        private volatile bool _running;
        private readonly ManualResetEventSlim _ready = new(false);

        public Action<WindowBoundsEventArgs>? SizeChanged;
        public Action<WindowBoundsEventArgs>? Moved;
        public Action<WindowStateChangedEventArgs>? StateChanged;
        public Action<WindowActiveEventArgs>? ActiveChanged;
        public Action<WindowEnabledEventArgs>? EnabledChanged;
        public Action<WindowVisibilityEventArgs>? VisibilityChanged;

        public WindowEventHook(IntPtr targetHwnd) 
        {
            _targetHwnd = targetHwnd;
            _lastBounds = new();
           
        }

        public void Start()
        {
            if (_running) 
                return;

            _running = true;

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "WindowEventHookThread"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _ready.Wait(3000);
        }

        private void Run()
        {
            _threadId = User32.GetCurrentThreadId();

            try
            {
                _lastBounds = Utils32.GetWindowRect(_targetHwnd);
                _lastState = Utils32.GetWindowState(_targetHwnd);
                _lastVisible = User32.IsWindowVisible(_targetHwnd);
                _lastEnabled = User32.IsWindowEnabled(_targetHwnd);
                _lastActive = User32.GetForegroundWindow() == _targetHwnd;

                _delegateKeepAlive = new WinEventDelegate(WinEventProc);
                _hookHandle = User32.SetWinEventHook(
                    EVENT_MIN, EVENT_MAX,
                    IntPtr.Zero, _delegateKeepAlive,
                    0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

                if (_hookHandle == IntPtr.Zero)
                    throw new InvalidOperationException("The WinEventHook could not be installed.. Error: " + Marshal.GetLastWin32Error());

                _ready.Set();

                while (_running && User32.GetMessage(out var msg, IntPtr.Zero, 0, 0))
                {
                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
                }
            }
            finally
            {
                // 4) Desinstalar en el MISMO hilo que instaló.
                if (_hookHandle != IntPtr.Zero)
                {
                    User32.UnhookWinEvent(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
                _delegateKeepAlive = null;

                _ready.Set(); // por si falló antes de instalar
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            if (_threadId != 0)
                User32.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _thread?.Join(2000);
            _thread = null;
            _threadId = 0;
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // EVENT_SYSTEM_FOREGROUND llega con el hwnd de la ventana que GANA el foco.
            // Debemos comprobar el estado de activación SIEMPRE, aunque el hwnd no sea el nuestro,
            // para detectar también la desactivación.
            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                CheckActive(dwmsEventTime);
                if (hwnd == _targetHwnd)
                    CheckState(dwmsEventTime);
                return;
            }

            // Solo ventana objetivo y solo objetos “ventana” (no hijos)
            if (hwnd != _targetHwnd) 
                return;
            
            if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF) 
                return;

            switch (eventType)
            {
                case EVENT_SYSTEM_MOVESIZESTART:
                case EVENT_SYSTEM_MOVESIZEEND:
                    CheckBounds(dwmsEventTime);
                    CheckState(dwmsEventTime);
                    break;

                case EVENT_SYSTEM_MINIMIZESTART:
                case EVENT_SYSTEM_MINIMIZEEND:
                    CheckState(dwmsEventTime);
                    break;

                case EVENT_SYSTEM_FOREGROUND:
                    CheckActive(dwmsEventTime);
                    CheckState(dwmsEventTime);   // por si venimos de minimizada
                    break;

                case EVENT_OBJECT_LOCATIONCHANGE:
                    CheckBounds(dwmsEventTime);
                    CheckState(dwmsEventTime);
                    break;

                case EVENT_OBJECT_SHOW:
                case EVENT_OBJECT_HIDE:
                    CheckVisibility(dwmsEventTime);
                    break;

                case EVENT_OBJECT_STATECHANGE:
                    CheckEnabled(dwmsEventTime);
                    break;
            }
        }

        // ── Detección de cambios (solo dispara si realmente cambió) ────────
        private void CheckBounds(uint timestamp)
        {
            var bounds = Utils32.GetWindowRect(_targetHwnd);

            var previous = _lastBounds;

            if (!bounds.SizeEquals(previous))
                SizeChanged?.Invoke(new WindowBoundsEventArgs
                {
                    Timestamp = timestamp,
                    State = _lastState,
                    Bounds = bounds,
                    PreviousBounds = previous
                });

            if (!bounds.LocationEquals(previous))
                Moved?.Invoke(new WindowBoundsEventArgs
                {
                    Timestamp = timestamp,
                    State = _lastState,
                    Bounds = bounds,
                    PreviousBounds = previous
                });

            _lastBounds = bounds;
        }

        private void CheckState(uint timestamp)
        {
            var state = Utils32.GetWindowState(_targetHwnd);
            
            if (state == _lastState) 
                return;

            StateChanged?.Invoke(new WindowStateChangedEventArgs
            {
                Timestamp = timestamp,
                OldState = _lastState,
                NewState = state
            });

            _lastState = state;
        }

        private void CheckActive(uint timestamp)
        {
            bool active = User32.GetForegroundWindow() == _targetHwnd;
            
            if (active == _lastActive) 
                return;

            ActiveChanged?.Invoke(new WindowActiveEventArgs
            {   
                Timestamp = timestamp,
                IsActive = active
            });

            _lastActive = active;
        }

        private void CheckEnabled(uint timestamp)
        {
            bool enabled = User32.IsWindowEnabled(_targetHwnd);
            
            if (enabled == _lastEnabled) 
                return;

            EnabledChanged?.Invoke(new WindowEnabledEventArgs
            {
                Timestamp = timestamp,
                IsEnabled = enabled
            });

            _lastEnabled = enabled;
        }

        private void CheckVisibility(uint timestamp)
        {
            bool visible = User32.IsWindowVisible(_targetHwnd);
            
            if (visible == _lastVisible) 
                return;

            VisibilityChanged?.Invoke(new WindowVisibilityEventArgs
            {
                Timestamp = timestamp,
                IsVisible = visible
            });

            _lastVisible = visible;
        }

        public void Dispose()
        {
            Stop();

            SizeChanged = null;
            Moved = null;
            StateChanged = null;
            ActiveChanged = null;
            EnabledChanged = null;
            VisibilityChanged = null;

            if (_hookHandle != IntPtr.Zero)
            {
                User32.UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _delegateKeepAlive = null;
        }
    }
}