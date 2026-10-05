using System;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>C's <c>atoi</c> and <c>atof</c>, which the engine reads every text number through.</summary>
/// <remarks>
/// **A text value the engine reads is not parsed, it is prefix-scanned**: `KeyValues::GetInt`/`GetFloat` on a string,
/// every entity keyfield (`ParseKeyvalue`, <c>saverestore_gamedll.cpp:56-76</c>) and <c>ReadInterval</c> all go through
/// these two. A word is zero, a trailing word is ignored, and nothing is ever a fallback — which a
/// <see cref="int.TryParse(string?, out int)"/> with a default gets wrong for every value it rejects.
///
/// **The runtime is Microsoft's**, which the engine binaries this project reads are linked against: where the C standard
/// leaves a case to the implementation — an <c>atoi</c> past the range of an int — the answer here is MSVC's.
/// </remarks>
public static class CStdlib
{
    /// <summary>C's <c>atoi</c>: leading white space, an optional sign, then digits; anything else stops it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value, zero when no digit follows the sign, and <see cref="int.MaxValue"/> or <see cref="int.MinValue"/> past the range.</returns>
    /// <remarks>
    /// **Past the range it CLAMPS** — <i>"atoi and _wtoi return INT_MAX and INT_MIN on these conditions"</i>, Microsoft's
    /// own reference for <c>atoi</c>, whose example reads <c>"3336402735171707160320"</c> as 2147483647.
    /// </remarks>
    public static int Atoi(ReadOnlySpan<char> text)
    {
        int at = SkipSpace(text);
        bool negative = at < text.Length && text[at] == '-';

        if (at < text.Length && text[at] is '-' or '+')
        {
            at++;
        }

        long value = 0;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            // Saturate rather than overflow: once past an int's range the answer is the clamp whatever follows.
            value = Math.Min((value * 10) + (text[at] - '0'), (long)int.MaxValue + 1);
            at++;
        }

        return (int)Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
    }

    /// <summary>C's <c>atof</c>: the longest prefix that reads as a decimal number, an infinity or a NaN, else zero.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value as a DOUBLE, or zero when no prefix is a number.</returns>
    /// <remarks>
    /// **A double, as C declares it** (<c>double atof( const char * )</c>), and the caller narrows where the engine
    /// assigns. Read straight to float, a value just past a float midpoint rounds once where the engine rounds twice —
    /// `"1.0000000596046447755"` is 1.00000012 one way and 1.0 the other — and arithmetic the engine does on the double
    /// before narrowing (<c>atof( token ) - tmp.start</c>, <c>interval.cpp:38</c>) comes out a float apart.
    ///
    /// **<c>inf</c>, <c>infinity</c> and <c>nan</c> are numbers to C** (C11 7.22.1.3), without case. The NaN is positive
    /// with every payload bit set, which narrowed is <c>0x7fffffff</c> — what vphysics' own surface table held for
    /// <c>"nan"</c>, read back from the shipped binary (B369).
    ///
    /// *Not reproduced:* hexadecimal floats. The runtime reads <c>0x1.8p1</c> as 3; this reads its <c>0</c>.
    /// </remarks>
    public static double Atof(ReadOnlySpan<char> text)
    {
        int at = SkipSpace(text);
        int start = at;

        if (at < text.Length && text[at] is '+' or '-')
        {
            at++;
        }

        ReadOnlySpan<char> rest = text[at..];

        if (rest.StartsWith("inf", StringComparison.OrdinalIgnoreCase) ||
            rest.StartsWith("nan", StringComparison.OrdinalIgnoreCase))
        {
            double special = rest.StartsWith("inf", StringComparison.OrdinalIgnoreCase)
                ? double.PositiveInfinity
                : BitConverter.Int64BitsToDouble(long.MaxValue);

            return text[start] == '-' ? -special : special;
        }

        int digits = 0;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
            digits++;
        }

        if (at < text.Length && text[at] == '.')
        {
            at++;

            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return 0d;
        }

        // An exponent counts only when a digit follows it: "1e" and "1e+" are 1.
        if (at < text.Length && text[at] is 'e' or 'E')
        {
            int exponent = at + 1;

            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                at = exponent;

                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }
            }
        }

        return double.Parse(text[start..at], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>C's float-to-int conversion as MSVC compiles it, <c>cvttss2si</c>: toward zero, and <c>0x80000000</c> for NaN or anything out of range.</summary>
    /// <param name="value">The float.</param>
    /// <returns>The int.</returns>
    public static int Truncate(float value) =>
        float.IsNaN(value) || value >= 2147483648f || value < -2147483648f ? int.MinValue : (int)value;

    /// <summary>Where C's <c>isspace</c> stops: space, <c>\t \n \v \f \r</c> — C11 7.4.1.10 in the "C" locale.</summary>
    private static int SkipSpace(ReadOnlySpan<char> text)
    {
        int at = 0;

        while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r')
        {
            at++;
        }

        return at;
    }
}
