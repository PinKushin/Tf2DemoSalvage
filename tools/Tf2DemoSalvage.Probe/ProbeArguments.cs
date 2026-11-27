using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe;

/// <summary>A probe's arguments, validated rather than parsed blind; a refusal sets a non-zero exit code.</summary>
/// <remarks>**"Not measured" must not exit like "measured"** — a missing or unfound demo, or a bad value, exits 1.</remarks>
internal static class ProbeArguments
{
    /// <summary>Finds the demo <paramref name="arguments"/>[0] names, as <see cref="DemoCorpus.Find"/> does.</summary>
    /// <returns>False, with the usage or the reason printed and the exit code set, when none is named or found.</returns>
    public static bool TryDemo(IReadOnlyList<string> arguments, string usage, TextWriter output, out string path)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        path = string.Empty;

        if (arguments.Count == 0)
        {
            output.WriteLine(usage);
        }
        else if (DemoCorpus.Find(arguments[0], output) is { } found)
        {
            path = found;
            return true;
        }
        else
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"error: no demo matches '{arguments[0]}'."));
        }

        Environment.ExitCode = 1;
        return false;
    }

    /// <summary>Reads <paramref name="arguments"/>[<paramref name="index"/>] as a finite, non-negative number, if given.</summary>
    /// <returns>False, with the reason printed and the exit code set, for a value that is not such a number.</returns>
    /// <remarks>`float.TryParse` accepts "Infinity" and "NaN"; neither is a margin.</remarks>
    public static bool TryNonNegative(IReadOnlyList<string> arguments, int index, TextWriter output, out float? value)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        value = null;

        if (arguments.Count <= index)
        {
            return true;
        }

        if (float.TryParse(arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
            float.IsFinite(parsed) && parsed >= 0f)
        {
            value = parsed;
            return true;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"error: '{arguments[index]}' is not a finite number of at least 0."));
        Environment.ExitCode = 1;
        return false;
    }

    /// <summary>Reads <paramref name="arguments"/>[<paramref name="index"/>] as a count of at least <paramref name="minimum"/>.</summary>
    /// <returns>False, with the reason printed and the exit code set, for a value that is not such a count.</returns>
    public static bool TryCount(
        IReadOnlyList<string> arguments, int index, int fallback, int minimum, TextWriter output, out int count)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        count = fallback;

        if (arguments.Count <= index)
        {
            return true;
        }

        if (int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out count) && count >= minimum)
        {
            return true;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"error: '{arguments[index]}' is not a whole number of at least {minimum}."));
        Environment.ExitCode = 1;
        return false;
    }

    /// <summary>The archive <paramref name="arguments"/>[0] names, or the installed game's <c>tf2_misc_dir.vpk</c>.</summary>
    /// <returns>The archive and its path, or null with the reason printed and the exit code set when there is none.</returns>
    public static (VpkArchive Archive, string Path)? Archive(IReadOnlyList<string> arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        string? folder = ProbeInstall.Folder(output);
        string? named = arguments.Count > 0 ? arguments[0] : null;

        named ??= folder is null ? null : Path.Combine(folder, "tf2_misc_dir.vpk");

        if (named is null || !File.Exists(named))
        {
            output.WriteLine($"No archive: {named ?? "the game folder could not be found"}");
            Environment.ExitCode = 1;
            return null;
        }

        return (VpkArchive.Open(named), named);
    }
}
