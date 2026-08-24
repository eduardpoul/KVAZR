using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Maxwell.Cli;

/// <summary>
/// Walks the Core Audio endpoints and reads what the class API offers for free: volume,
/// mute, a live peak meter, and which device container each endpoint belongs to.
///
/// This is the microphone-and-headset half of the baseline capability question. Every
/// USB or Bluetooth audio device gets these from Windows with no vendor protocol at
/// all — the probe exists to confirm they read correctly against the desk, and to show
/// the container id that ties an endpoint back to a device row.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AudioProbe
{
    public static int Run()
    {
        Guid clsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E"); // MMDeviceEnumerator
        Guid iid = typeof(IMMDeviceEnumerator).GUID;

        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1 /*CLSCTX_INPROC_SERVER*/, ref iid, out object comObject);
        if (hr != 0)
        {
            Console.WriteLine($"CoCreateInstance: 0x{hr:X8}");
            return 1;
        }

        var enumerator = (IMMDeviceEnumerator)comObject;

        foreach ((int flow, string title) in ((int, string)[])[(1, "ЗАПИСЬ (микрофоны)"), (0, "ВОСПРОИЗВЕДЕНИЕ")])
        {
            Console.WriteLine(title);

            enumerator.EnumAudioEndpoints(flow, 1 /*DEVICE_STATE_ACTIVE*/, out IMMDeviceCollection collection);
            collection.GetCount(out int count);

            for (int i = 0; i < count; i++)
            {
                collection.Item(i, out IMMDevice device);
                Describe(device);
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static void Describe(IMMDevice device)
    {
        device.GetId(out string id);

        string name = Property(device, PkeyFriendlyName) ?? "(без имени)";
        string container = Property(device, PkeyContainerId) ?? "—";

        Guid volumeIid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref volumeIid, 1, IntPtr.Zero, out object volumeObject);
        var volume = (IAudioEndpointVolume)volumeObject;

        volume.GetMasterVolumeLevelScalar(out float scalar);
        volume.GetMute(out bool mute);

        Guid meterIid = typeof(IAudioMeterInformation).GUID;
        device.Activate(ref meterIid, 1, IntPtr.Zero, out object meterObject);
        var meter = (IAudioMeterInformation)meterObject;
        meter.GetPeakValue(out float peak);

        Console.WriteLine($"  {name}");
        Console.WriteLine($"      громкость {scalar * 100:0}%   mute {(mute ? "да" : "нет")}   пик {peak * 100:0}%");
        Console.WriteLine($"      container {container}");
    }

    private static string? Property(IMMDevice device, PropertyKey key)
    {
        device.OpenPropertyStore(0, out IPropertyStore store);

        var value = default(PropVariant);
        if (store.GetValue(ref key, ref value) != 0) return null;

        try
        {
            return value.VarType switch
            {
                31 => Marshal.PtrToStringUni(value.Pointer),
                72 => value.PointerToGuid()?.ToString("D"),
                _ => null,
            };
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static PropertyKey PkeyFriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static PropertyKey PkeyContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

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

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
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
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);

        int GetAt(int index, ref PropertyKey key);

        int GetValue(ref PropertyKey key, ref PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);

        int UnregisterControlChangeNotify(IntPtr notify);

        int GetChannelCount(out uint count);

        int SetMasterVolumeLevel(float level, ref Guid context);

        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        int GetMasterVolumeLevel(out float level);

        int GetMasterVolumeLevelScalar(out float level);

        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        int GetChannelVolumeLevel(uint channel, out float level);

        int GetChannelVolumeLevelScalar(uint channel, out float level);

        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }
}
