using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Watches what mice actually do, as against what they declare.
///
/// The only universal source left at the baseline, and the only one that reports
/// behaviour: a mouse cannot be opened for reading while Windows is driving it — the
/// refusal is measured, Win32 5 — so its traffic is unreachable through the device
/// itself. Raw Input hands the same traffic over without a handle on anything, for every
/// mouse from every maker, and marks each report with the device it came from.
///
/// It needs a window, because that is how Windows delivers the reports, so this runs a
/// message-only one on a thread of its own. A message-only window is never shown, never
/// appears in the task bar and cannot be focused; it exists to receive.
///
/// What it cannot see: a mouse's sixth button and beyond. Raw Input's mouse record has
/// room for five and a wheel, and anything past that arrives as ordinary HID usages on
/// another collection. So a count taken from here is "at least", never "exactly" — which
/// is still more than a declaration that says sixteen for a five-button mouse.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRawInputWatcher : IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmQuit = 0x0012;
    private const int WmClose = 0x0010;

    private const uint RidInput = 0x10000003;
    private const uint InputSink = 0x00000100;

    private const ushort GenericDesktop = 0x01;
    private const ushort MouseUsage = 0x02;

    private static readonly IntPtr MessageOnly = new(-3);

    private readonly Thread _pump;
    private readonly ManualResetEventSlim _ready = new(false);

    // Held so the delegate is not collected while Windows holds the pointer to it, and
    // the class registration is not garbage while its window lives.
    private readonly Native.WindowProc _proc;

    private IntPtr _window;
    private uint _thread;
    private bool _disposed;

    /// <summary>Raised for every report, on the watcher's own thread.</summary>
    public event Action<RawMouseReport>? Reported;

    public WindowsRawInputWatcher()
    {
        _proc = Handle;

        _pump = new Thread(Pump) { IsBackground = true, Name = "raw input" };
        _pump.SetApartmentState(ApartmentState.STA);
        _pump.Start();

        // Registration either succeeded or it did not, and the difference is invisible
        // afterwards — a watcher that heard nothing looks the same as a still mouse.
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Whether Windows accepted the registration. False means nothing will arrive.</summary>
    public bool IsWatching { get; private set; }

    private void Pump()
    {
        // The native thread id, not the managed one: only the first means anything to
        // the message queue this loop is about to enter.
        _thread = Native.GetCurrentThreadId();

        var wndClass = new Native.WindowClass
        {
            Size = Marshal.SizeOf<Native.WindowClass>(),
            Proc = _proc,
            Instance = Native.GetModuleHandle(null),
            ClassName = "KvazrRawInput",
        };

        ushort atom = Native.RegisterClassEx(ref wndClass);

        if (atom != 0)
        {
            _window = Native.CreateWindowEx(
                0, wndClass.ClassName, string.Empty, 0, 0, 0, 0, 0,
                MessageOnly, IntPtr.Zero, wndClass.Instance, IntPtr.Zero);
        }

        if (_window != IntPtr.Zero)
        {
            Native.RawInputDevice[] wanted =
            [
                new()
                {
                    UsagePage = GenericDesktop,
                    Usage = MouseUsage,
                    Flags = InputSink,
                    Target = _window,
                },
            ];

            IsWatching = Native.RegisterRawInputDevices(
                wanted, wanted.Length, Marshal.SizeOf<Native.RawInputDevice>());
        }

        _ready.Set();

        if (_window == IntPtr.Zero) return;

        while (Native.GetMessage(out Native.Message message, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }
    }

    private IntPtr Handle(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WmInput) return Native.DefWindowProc(window, message, wParam, lParam);

        int size = Marshal.SizeOf<Native.RawInputMouse>();
        uint given = (uint)size;

        if (Native.GetRawInputData(lParam, RidInput, out Native.RawInputMouse raw, ref given, Marshal.SizeOf<Native.RawInputHeader>())
            != unchecked((uint)-1))
        {
            Reported?.Invoke(new RawMouseReport(
                raw.Header.Device,
                raw.ButtonFlags,
                (short)raw.ButtonData,
                raw.LastX,
                raw.LastY));
        }

        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_window != IntPtr.Zero)
        {
            Native.PostMessage(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
            Native.PostThreadMessage(_thread, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _pump.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    private static class Native
    {
        internal delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WindowClass
        {
            public int Size;
            public int Style;
            [MarshalAs(UnmanagedType.FunctionPtr)] public WindowProc Proc;
            public int ExtraClass;
            public int ExtraWindow;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RawInputMouse
        {
            public RawInputHeader Header;
            public ushort Flags;
            public ushort Padding;
            public ushort ButtonFlags;
            public ushort ButtonData;
            public uint RawButtons;
            public int LastX;
            public int LastY;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            public IntPtr Window;
            public uint Value;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WindowClass wndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterRawInputDevices(
            [In] RawInputDevice[] devices, int count, int size);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetRawInputData(
            IntPtr input, uint command, out RawInputMouse data, ref uint size, int headerSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? name);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();
    }
}
