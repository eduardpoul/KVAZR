using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Maxwell.Race.Abstractions;

namespace Maxwell.Race.Windows;

/// <summary>
/// Core Audio, which is the widest door in this application.
///
/// Every audio device on Windows — USB, Bluetooth, a dongle pretending to be a sound
/// card — presents volume, mute and a level meter here, with no vendor protocol and no
/// catalogue entry. One implementation is therefore the entire baseline page for
/// headsets, microphones and speakers at once, and it works on models nobody has ever
/// heard of.
///
/// The COM objects are agile and shared rather than exclusive, so holding one open costs
/// nothing anybody else can notice; the enumerator itself is created per sweep because
/// creating it is cheaper than reasoning about how long to keep it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioEndpoints : IAudioEndpoints
{
    private const int Render = 0;
    private const int Capture = 1;
    private const int StateActive = 1;
    private const int InprocServer = 1;

    public IReadOnlyList<AudioEndpoint> Enumerate()
    {
        var found = new List<AudioEndpoint>();
        IMMDeviceEnumerator enumerator = Create();

        try
        {
            foreach ((int flow, AudioFlow kind) in ((int, AudioFlow)[])[(Capture, AudioFlow.Capture), (Render, AudioFlow.Render)])
            {
                if (enumerator.EnumAudioEndpoints(flow, StateActive, out IMMDeviceCollection collection) != 0) continue;
                if (collection.GetCount(out int count) != 0) continue;

                for (int i = 0; i < count; i++)
                {
                    if (collection.Item(i, out IMMDevice device) != 0) continue;

                    try
                    {
                        if (Describe(device, kind) is { } endpoint) found.Add(endpoint);
                    }
                    finally
                    {
                        Release(device);
                    }
                }

                Release(collection);
            }
        }
        finally
        {
            Release(enumerator);
        }

        return found;
    }

    public IAudioEndpointChannel Open(string endpointId)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        IMMDeviceEnumerator enumerator = Create();

        try
        {
            if (enumerator.GetDevice(endpointId, out IMMDevice device) != 0)
            {
                throw new IOException($"Audio endpoint {endpointId} could not be opened.");
            }

            try
            {
                return new Channel(device);
            }
            catch
            {
                Release(device);
                throw;
            }
        }
        finally
        {
            Release(enumerator);
        }
    }

    /// <summary>
    /// Starts a capture stream on the endpoint. The device object is handed to the meter,
    /// which owns it from then on: the stream and the reference have to end together, and
    /// the meter is the thing whose lifetime is visible in the taskbar.
    /// </summary>
    public IAudioLevelMeter Listen(string endpointId)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        IMMDeviceEnumerator enumerator = Create();

        try
        {
            if (enumerator.GetDevice(endpointId, out IMMDevice device) != 0)
            {
                throw new IOException($"Audio endpoint {endpointId} could not be opened.");
            }

            try
            {
                return new WindowsAudioLevelMeter(device);
            }
            catch
            {
                Release(device);
                throw;
            }
        }
        finally
        {
            Release(enumerator);
        }
    }

    private static AudioEndpoint? Describe(IMMDevice device, AudioFlow flow)
    {
        if (device.GetId(out string id) != 0) return null;

        string? name = Text(device, FriendlyName);
        string? container = Text(device, ContainerId);

        return new AudioEndpoint
        {
            Id = id,
            Name = name ?? id,
            ContainerId = Guid.TryParse(container, out Guid parsed) ? parsed : AudioEndpoint.VirtualContainer,
            Flow = flow,
        };
    }

    private static IMMDeviceEnumerator Create()
    {
        Guid clsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E"); // MMDeviceEnumerator
        Guid iid = typeof(IMMDeviceEnumerator).GUID;

        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, InprocServer, ref iid, out object created);
        if (hr != 0) throw new IOException($"Core Audio is unavailable (0x{hr:X8}).");

        return (IMMDeviceEnumerator)created;
    }

    private static string? Text(IMMDevice device, PropertyKey key)
    {
        if (device.OpenPropertyStore(0, out IPropertyStore store) != 0) return null;

        var value = default(PropVariant);

        try
        {
            if (store.GetValue(ref key, ref value) != 0) return null;

            return value.VarType switch
            {
                VtLpwstr => Marshal.PtrToStringUni(value.Pointer),
                VtClsid => value.PointerToGuid()?.ToString("D"),
                _ => null,
            };
        }
        finally
        {
            _ = PropVariantClear(ref value);
            Release(store);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
    }

    /// <summary>One endpoint held open: its volume interface and its meter.</summary>
    private sealed class Channel : IAudioEndpointChannel
    {
        private IMMDevice? _device;
        private IAudioEndpointVolume? _volume;
        private IAudioMeterInformation? _meter;

        public Channel(IMMDevice device)
        {
            _device = device;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, InprocServer, IntPtr.Zero, out object activated) != 0)
            {
                throw new IOException("Endpoint volume could not be activated.");
            }

            _volume = (IAudioEndpointVolume)activated;

            // The meter is optional and the endpoint is not. A device that answers about
            // its volume and refuses a meter is still worth every other row on the page,
            // so this failure is a null rather than an exception.
            Guid meter = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref meter, InprocServer, IntPtr.Zero, out object metering) == 0)
            {
                _meter = (IAudioMeterInformation)metering;
            }
        }

        public int? Volume =>
            _volume is { } volume && volume.GetMasterVolumeLevelScalar(out float scalar) == 0
                ? Percent(scalar)
                : null;

        public bool? Mute =>
            _volume is { } volume && volume.GetMute(out bool muted) == 0 ? muted : null;

        public int? Peak =>
            _meter is { } meter && meter.GetPeakValue(out float peak) == 0 ? Percent(peak) : null;

        public bool SetVolume(int percent)
        {
            if (_volume is not { } volume) return false;

            // Clamped rather than refused. The scale is a float 0..1 and the caller works
            // in whole percent, so the only values that can arrive out of range are
            // rounding at the ends of the slider, and failing those would make the last
            // pixel of travel do nothing.
            float scalar = Math.Clamp(percent, 0, 100) / 100f;

            Guid context = Guid.Empty;
            return volume.SetMasterVolumeLevelScalar(scalar, ref context) == 0;
        }

        public bool SetMute(bool mute)
        {
            if (_volume is not { } volume) return false;

            Guid context = Guid.Empty;
            return volume.SetMute(mute, ref context) == 0;
        }

        /// <summary>
        /// Rounded, not truncated. Windows stores the scalar as a float and the value
        /// that comes back from a write of 40 is 0.39999998; truncating turned every
        /// setting into one less than the user chose, one poll later.
        /// </summary>
        private static int Percent(float scalar) => (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100f);

        public void Dispose()
        {
            Release(_meter);
            Release(_volume);
            Release(_device);

            _meter = null;
            _volume = null;
            _device = null;
        }
    }

    // ---------- interop ----------

    private const ushort VtLpwstr = 31;
    private const ushort VtClsid = 72;

    private static PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static PropertyKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, int id)
    {
        public Guid FormatId = format;
        public int PropertyId = id;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VarType;
        [FieldOffset(8)] public IntPtr Pointer;

        public readonly Guid? PointerToGuid() =>
            Pointer == IntPtr.Zero ? null : Marshal.PtrToStructure<Guid>(Pointer);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object comObject);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    /// <summary>
    /// Declared in full even though three methods are never called: a COM interface is a
    /// vtable, and leaving a method out silently shifts every method after it onto the
    /// wrong slot. <c>GetDevice</c> is the fourth entry and would have been called as
    /// <c>EnumAudioEndpoints</c>.
    /// </summary>
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        int RegisterEndpointNotificationCallback(IntPtr client);

        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out int count);

        int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, uint context, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);

        int OpenPropertyStore(int access, out IPropertyStore store);

        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);

        int GetAt(int index, ref PropertyKey key);

        int GetValue(ref PropertyKey key, ref PropVariant value);
    }

    /// <summary>
    /// Every method returns its HRESULT, and the two that carry a flag say how wide it is.
    ///
    /// Both of those are load-bearing and one of them was missing for months. A bare
    /// <c>bool</c> in a COM signature marshals as a two-byte VARIANT_BOOL, and this
    /// interface is Win32 rather than Automation: it wants a four-byte BOOL. Reading
    /// happened to survive it — a zero is a zero however many bytes you read — and writing
    /// did not: the endpoint saw a value that was neither TRUE nor FALSE and answered
    /// E_INVALIDARG, which reached the interface as a switch that moved and sprang back.
    /// </summary>
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>
    /// The endpoint's meter. Same rule as above about declaring the whole vtable: the
    /// three methods after <c>GetPeakValue</c> are never called and are here so that
    /// nothing else lands on their slots.
    /// </summary>
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);

        int GetMeteringChannelCount(out uint channels);

        int GetChannelsPeakValues(uint channels, [Out] float[] peaks);

        int QueryHardwareSupport(out uint mask);
    }
}
