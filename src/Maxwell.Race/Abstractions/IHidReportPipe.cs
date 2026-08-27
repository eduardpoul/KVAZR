namespace Maxwell.Race.Abstractions;

/// <summary>
/// A collection that is asked and answers, over ordinary input and output reports.
///
/// Distinct from <see cref="IHidVendorPipe"/>, which does the same job over feature
/// reports. The difference is not a detail of plumbing: a VIA keyboard declares no
/// feature reports at all — measured on a NuPhy Air96 V2, feature length zero on every
/// one of its collections — so a pipe built on <c>HidD_SetFeature</c> cannot reach it.
///
/// The caller supplies the predicate that recognises its own answer, because only the
/// protocol knows. VIA echoes the command and its arguments and carries no sequence
/// number, so "the first report that arrives" is not the answer: it can be the reply to
/// the previous question, or a keypress on the same pipe.
/// </summary>
public interface IHidReportPipe : IDisposable
{
    /// <summary>How long a report is, report id included. Requests are written at this size.</summary>
    int ReportLength { get; }

    /// <summary>
    /// Writes a report and returns the first reply the predicate accepts, or null when
    /// nothing acceptable arrived within the wait.
    /// </summary>
    byte[]? Request(byte[] report, Func<byte[], bool> accepts, TimeSpan within);
}

/// <summary>
/// Opens such a pipe on a collection named by its usage, on a device named by its ids.
///
/// By usage rather than by path, for the reason the listener factory gives: a path is a
/// machine-specific string that changes when a device moves port. By vendor and product
/// as well, because a class protocol is found on several devices at once and a driver is
/// asking about one of them.
/// </summary>
public interface IHidReportPipeFactory
{
    /// <summary>Null when nothing matches, which is the ordinary case and not an error.</summary>
    IHidReportPipe? Open(ushort vendorId, ushort productId, ushort usagePage, byte usage);
}
