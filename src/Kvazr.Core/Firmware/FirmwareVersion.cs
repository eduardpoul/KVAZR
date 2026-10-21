using System.Globalization;

namespace Kvazr.Core.Firmware;

/// <summary>
/// A Maxwell firmware version, as both the device and Audeze's manifests spell it:
/// four dotted numbers, sometimes with a leading "v".
///
/// Compared numerically rather than as text, because that is where the interesting
/// bug lives — as strings, "v1.0.1.9" sorts above "v1.0.1.74" and the app would offer
/// a downgrade as an update.
/// </summary>
public readonly record struct FirmwareVersion(int Major, int Minor, int Patch, int Build)
    : IComparable<FirmwareVersion>
{
    public static bool TryParse(string? text, out FirmwareVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];

        Span<Range> parts = stackalloc Range[5];
        int count = span.Split(parts, '.');
        if (count != 4) return false;

        Span<int> numbers = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(span[parts[i]], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new FirmwareVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public static FirmwareVersion? Parse(string? text) =>
        TryParse(text, out FirmwareVersion version) ? version : null;

    public int CompareTo(FirmwareVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : Build.CompareTo(other.Build);
    }

    public static bool operator <(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(FirmwareVersion left, FirmwareVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Rendered the way Audeze writes it, so it matches what the headset reports.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"v{Major}.{Minor}.{Patch}.{Build}");
}
