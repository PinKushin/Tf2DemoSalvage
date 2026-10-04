using System;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>C's <c>atoi</c> and <c>atof</c>, which the engine reads every text number through.</summary>
/// <remarks>
/// **A text value the engine reads is not parsed, it is prefix-scanned**: `KeyValues::GetInt`/`GetFloat` on a string,
/// every entity keyfield (`ParseKeyvalue`, <c>saverestore_gamedll.cpp:56-76</c>) and <c>ReadInterval</c> all go through
/// these two. A word is zero, a trailing word is ignored, and nothing is ever a fallback — which a
/// <see cref="int.TryParse(string?, out int)"/> with a default gets wrong for every value it rejects.
/// </remarks>
public static class CStdlib
{
    /// <summary>C's <c>atoi</c>: leading white space, an optional sign, then digits; anything else stops it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value, or zero when no digit follows the sign.</returns>
    public static int Atoi(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        int length = 0;

        if (length < text.Length && text[length] is '-' or '+')
        {
            length++;
        }

        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        return int.TryParse(text[..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    /// <summary>C's <c>atof</c>: the longest prefix that reads as a decimal number, else zero.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value, or zero when no prefix is a number.</returns>
    public static float Atof(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();

        for (int length = text.Length; length > 0; length--)
        {
            if (float.TryParse(
                    text[..length],
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture,
                    out float value))
            {
                return value;
            }
        }

        return 0f;
    }
}
