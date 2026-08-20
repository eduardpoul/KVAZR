using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.App.Platform;
using Maxwell.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Maxwell.App.Platform.Windows;

/// <summary>A global shortcut and what it triggers.</summary>
public sealed record HotkeyBinding(string ActionId, string Gesture);

/// <summary>Registers system-wide shortcuts that work while the window is hidden.</summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler<string>? Triggered;

    /// <summary>Replaces every registration. Returns the actions that could not be bound.</summary>
    IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings);
}

/// <summary>
/// RegisterHotKey needs a window to deliver WM_HOTKEY to, and Avalonia does not expose
/// its window procedure. Rather than subclass one, this owns a message-only window on
/// its own thread — self-contained, and it keeps working while every window is hidden,
/// which is the entire point of a global shortcut in a tray app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHotkeys : IHotkeyService
{
    private const int WmHotkey = 0x0312;

    /// <summary>
    /// Private message used to hand a registration request to the pump thread.
    /// RegisterHotKey fails with ERROR_WINDOW_OF_OTHER_THREAD (1408) unless it runs on
    /// the thread that owns the target window, so the work has to be posted there.
    /// </summary>
    private const uint WmApplyBindings = 0x8000 + 1;
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>Stops the shortcut firing repeatedly while the key is held down.</summary>
    private const uint ModNoRepeat = 0x4000;

    private readonly ILogger<WindowsHotkeys> _logger;
    private readonly Dictionary<int, string> _actionsById = [];
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _applied = new(false);

    private IReadOnlyList<HotkeyBinding> _pending = [];
    private List<string> _failed = [];

    private IntPtr _window;
    private int _lastError;
    private uint _threadId;
    private Thread? _pump;
    private WndProc? _procedure;
    private bool _disposed;

    public event EventHandler<string>? Triggered;

    public WindowsHotkeys(ILogger<WindowsHotkeys> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        _pump = new Thread(PumpMessages)
        {
            IsBackground = true,
            Name = "Maxwell hotkeys",
        };

        _pump.SetApartmentState(ApartmentState.STA);
        _pump.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public IReadOnlyList<string> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window == IntPtr.Zero)
        {
            _logger.HotkeyWindowFailed(_lastError);
            return bindings.Select(b => b.ActionId).ToList();
        }

        _pending = bindings;
        _applied.Reset();

        // Hand the work to the pump thread and wait for it; registration is fast, and
        // the caller needs the list of combinations that were refused.
        PostMessage(_window, WmApplyBindings, IntPtr.Zero, IntPtr.Zero);
        _applied.Wait(TimeSpan.FromSeconds(5));

        return _failed;
    }

    /// <summary>Runs on the pump thread, which is the only thread allowed to register.</summary>
    private void ApplyOnPumpThread()
    {
        var failed = new List<string>();

        foreach (int existing in _actionsById.Keys) UnregisterHotKey(_window, existing);
        _actionsById.Clear();

        int next = 1;
        foreach (HotkeyBinding binding in _pending)
        {
            if (string.IsNullOrWhiteSpace(binding.Gesture)) continue;

            if (!TryResolve(binding.Gesture, out uint modifiers, out uint key))
            {
                failed.Add(binding.ActionId);
                continue;
            }

            int id = next++;
            if (RegisterHotKey(_window, id, modifiers | ModNoRepeat, key))
            {
                _actionsById[id] = binding.ActionId;
                _logger.HotkeyRegistered(binding.Gesture, id);
            }
            else
            {
                // Usually means another application already owns the combination.
                _logger.HotkeyRejected(binding.Gesture, Marshal.GetLastWin32Error());
                failed.Add(binding.ActionId);
            }
        }

        _failed = failed;
        _applied.Set();
    }

    /// <summary>Core parses the gesture; this turns its key token into a virtual key code.</summary>
    private static bool TryResolve(string gesture, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        if (!HotkeyGesture.TryParse(gesture, out HotkeyModifiers parsed, out string token)) return false;
        if (!TryParseKey(token, out key)) return false;

        if (parsed.HasFlag(HotkeyModifiers.Control)) modifiers |= ModControl;
        if (parsed.HasFlag(HotkeyModifiers.Alt)) modifiers |= ModAlt;
        if (parsed.HasFlag(HotkeyModifiers.Shift)) modifiers |= ModShift;
        if (parsed.HasFlag(HotkeyModifiers.Win)) modifiers |= ModWin;

        return true;
    }

    private static bool TryParseKey(string part, out uint key)
    {
        key = part.ToUpperInvariant() switch
        {
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "HOME" => 0x24,
            "END" => 0x23,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            _ => 0,
        };

        if (key != 0) return true;

        if (part.Length == 1)
        {
            char c = char.ToUpperInvariant(part[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = c;
                return true;
            }
        }

        // F1 to F24.
        if (part.Length >= 2 && (part[0] is 'F' or 'f')
            && int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number is >= 1 and <= 24)
        {
            key = (uint)(0x70 + number - 1);
            return true;
        }

        return false;
    }

    private void PumpMessages()
    {
        _threadId = GetCurrentThreadId();
        _procedure = WindowProcedure;

        var windowClass = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_procedure),
            lpszClassName = "MaxwellHotkeyWindow",
            hInstance = GetModuleHandle(null),
        };

        if (RegisterClass(ref windowClass) == 0)
        {
            _lastError = Marshal.GetLastWin32Error();
            _ready.Set();
            return;
        }

        // HWND_MESSAGE: invisible, never activated, and not enumerated by other apps.
        _window = CreateWindowEx(0, windowClass.lpszClassName, "", 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);

        if (_window == IntPtr.Zero) _lastError = Marshal.GetLastWin32Error();

        _ready.Set();
        if (_window == IntPtr.Zero) return;

        while (GetMessage(out Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WmApplyBindings)
            {
                ApplyOnPumpThread();
                continue;
            }

            // The id is one we assigned ourselves (1..N), so the narrowing cannot overflow.
            if (message.message != WmHotkey) continue;
            if (_actionsById.TryGetValue(checked((int)message.wParam), out string? action))
                Triggered?.Invoke(this, action);
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message is WmClose or WmDestroy) PostQuitMessage(0);
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_window != IntPtr.Zero)
        {
            foreach (int id in _actionsById.Keys) UnregisterHotKey(_window, id);
            _actionsById.Clear();
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);

        _pump?.Join(TimeSpan.FromSeconds(2));
        _pump = null;
        _ready.Dispose();
        _applied.Dispose();
    }

    // ---------- Win32 ----------

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
