using System;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where screenshots go, what they are called, and how many are kept.</summary>
/// <remarks>
/// **This was `MainForm.CaptureFolder`, `CaptureName` and `CapturesKept`** (B208). Grabbing the
/// back buffer is view work; deciding where the file lands, what it is named, and when an old one is
/// deleted is policy, and it sat in a WinForms class only because the F12 handler did.
///
/// **`CaptureName` was already `public static` and already tested**, which is the tell: somebody had
/// decided it was a testable policy rather than window code, and it stayed put anyway.
/// </remarks>
public static class Captures
{
    /// <summary>How many captures to keep before deleting the oldest.</summary>
    /// <remarks>
    /// **Twenty rather than the logs' fifty, purely on size**: a viewport capture is close to a
    /// megabyte where a run's log is tens of kilobytes. Carried over verbatim rather than
    /// reasoned afresh — the first draft of this file invented "a session's worth", which sounds
    /// like a reason and is not the one anybody had.
    /// </remarks>
    public const int Kept = 20;

    /// <summary>The glob that matches what <see cref="Name"/> produces.</summary>
    /// <remarks>
    /// **Beside the name it has to match**, because retention deletes by this pattern: change the
    /// prefix in one and not the other and nothing is ever pruned — silently, since deleting zero
    /// files looks exactly like having nothing to delete.
    /// </remarks>
    public const string Pattern = "shot-*.png";

    /// <summary>What to call the capture taken at a given moment.</summary>
    /// <param name="when">When it was taken.</param>
    /// <returns>The file name.</returns>
    /// <remarks>
    /// **Milliseconds are in the stamp on purpose.** Captures can be taken a frame apart — F12 held
    /// down, or an automatic shot beside a manual one — and a second-resolution name would have the
    /// second overwrite the first.
    /// </remarks>
    public static string Name(DateTime when) =>
        string.Create(CultureInfo.InvariantCulture, $"shot-{when:yyyyMMdd-HHmmss-fff}.png");

    /// <summary>Where captures should be written.</summary>
    /// <param name="wanted">The folder the user asked for, or null or blank for none.</param>
    /// <param name="fallback">Where to write when there is no usable choice.</param>
    /// <param name="log">Where to report an unusable choice.</param>
    /// <returns>A folder that exists, or the fallback.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    /// <remarks>
    /// **Falls back rather than failing, and says so.** A screenshot is a diagnostic, so an
    /// unwritable folder must not stop the viewer — but a silent fallback would leave the user
    /// looking for files in the folder they configured, which is why the warning names the path.
    ///
    /// **Creates the folder as part of deciding on it.** "Can I write here" is only answerable by
    /// trying; a check that returned true and then failed at write time would move the error to a
    /// place with less context.
    /// </remarks>
    public static string Folder(string? wanted, string fallback, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (string.IsNullOrWhiteSpace(wanted))
        {
            return fallback;
        }

        try
        {
            Directory.CreateDirectory(wanted);
            return wanted;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException)
        {
            log.LogWarning(failure, "{Message}", $"cannot write captures to {wanted}");
            return fallback;
        }
    }
}
