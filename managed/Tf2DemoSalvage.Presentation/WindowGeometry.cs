using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A window size and position, read out of the strings a launcher supplies.</summary>
/// <remarks>
/// **This was `MainForm.ApplyGeometryOverride`** (B208). Applying a size to a window is view work;
/// deciding whether `"1280x720"` is a size, and whether `"0x720"` is one, is parsing — and it could
/// only be exercised by launching a window with the environment set.
///
/// **Separate from `LaunchOptions`, which reads the command line**, because these answer different
/// questions: that one is *what to show*, this is *how big the window is*. Folding them together
/// would give `LaunchOptions` a second reason to change.
/// </remarks>
public static class WindowGeometry
{
    /// <summary>Reads a <c>WIDTHxHEIGHT</c> pair.</summary>
    /// <param name="text">The value, or null when the variable is unset.</param>
    /// <returns>The size, or null when the text is not a usable one.</returns>
    /// <remarks>
    /// **Zero and negative are rejected even though they parse.** A window sized `0x720` is
    /// invisible and a negative one crashes some window managers, so "unusable" has to be wider
    /// than "unparseable" — which is the reason this returns null rather than a parsed pair.
    /// </remarks>
    public static (int Width, int Height)? Size(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split('x', StringSplitOptions.TrimEntries);

        return parts.Length == 2 &&
            int.TryParse(parts[0], CultureInfo.InvariantCulture, out int width) &&
            int.TryParse(parts[1], CultureInfo.InvariantCulture, out int height) &&
            width > 0 && height > 0
            ? (width, height)
            : null;
    }

    /// <summary>Reads an <c>X,Y</c> pair.</summary>
    /// <param name="text">The value, or null when the variable is unset.</param>
    /// <returns>The position, or null when the text is not a usable one.</returns>
    /// <remarks>
    /// **Negatives ARE accepted here, unlike <see cref="Size"/>, and the asymmetry is deliberate.**
    /// A negative position is ordinary — it is how a window lands on a monitor left of or above the
    /// primary one. Treating the two alike would either break multi-monitor placement or allow an
    /// invisible window.
    /// </remarks>
    public static (int X, int Y)? Position(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        return parts.Length == 2 &&
            int.TryParse(parts[0], CultureInfo.InvariantCulture, out int x) &&
            int.TryParse(parts[1], CultureInfo.InvariantCulture, out int y)
            ? (x, y)
            : null;
    }
}
