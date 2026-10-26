using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Probe;

/// <summary>A probe's optional count argument, validated rather than parsed blind.</summary>
internal static class ProbeArguments
{
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
}
