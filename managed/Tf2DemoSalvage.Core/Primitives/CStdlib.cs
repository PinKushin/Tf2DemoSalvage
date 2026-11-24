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
    /// **Hexadecimal floats and the <c>nan(…)</c> spellings are read as UCRT reads them** (B823 review 1):
    /// <c>parse_floating_point_from_source</c> and <c>parse_floating_point_possible_nan</c> (Windows SDK 10.0.26100.0,
    /// <c>ucrt/inc/corecrt_internal_strtox.h:1326-1475</c>, the bit patterns at <c>:552-613</c>). TF2's x64 <c>engine.dll</c>
    /// links that parser statically: its <c>IND)</c>/<c>ind)</c> tables are at 0x180406a48 (build 1007), read by
    /// build 1007 FUN_180334360, beside <c>nan(snan)</c> and <c>nan(ind)</c> (*disassembly*). *Not reproduced:* the
    /// rounding of a hexadecimal value that lands in the subnormal range, which here is rounded twice.
    /// </remarks>
    public static double Atof(ReadOnlySpan<char> text) => Atof(text, out _);

    /// <summary><c>strtod</c>: <see cref="Atof(ReadOnlySpan{char})"/>, and how many characters the number took.</summary>
    /// <param name="text">The text.</param>
    /// <param name="used">Where <c>strtod</c>'s end pointer would stop: 0 when no prefix is a number.</param>
    /// <returns>The value, as <see cref="Atof(ReadOnlySpan{char})"/> gives it.</returns>
    /// <remarks>
    /// So a caller can ask whether a WHOLE string is a number (B823 review 3): <c>0.1f</c> reads 0.1 with three of its four
    /// characters, and <c>255 255 255</c> reads 255 with three of eleven.
    /// </remarks>
    public static double Atof(ReadOnlySpan<char> text, out int used)
    {
        int at = SkipSpace(text);
        int start = at;
        used = 0;

        if (at < text.Length && text[at] is '+' or '-')
        {
            at++;
        }

        bool negative = at > start && text[start] == '-';
        ReadOnlySpan<char> rest = text[at..];

        if (rest.StartsWith("inf", StringComparison.OrdinalIgnoreCase))
        {
            used = at + (rest.StartsWith("infinity", StringComparison.OrdinalIgnoreCase) ? 8 : 3);

            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (rest.StartsWith("nan", StringComparison.OrdinalIgnoreCase))
        {
            double nan = Nan(rest[3..], negative, out int parenthesised);
            used = at + 3 + parenthesised;

            return nan;
        }

        if (rest.Length >= 2 && rest[0] == '0' && rest[1] is 'x' or 'X')
        {
            double hex = Hexadecimal(rest[2..], out int hexUsed);

            // No hex digit: the subject is the `0` alone, and the `x` begins what is left (:1563-1570).
            used = hexUsed == 0 ? at + 1 : at + 2 + hexUsed;

            return negative ? -hex : hex;
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

        used = at;

        return double.Parse(text[start..at], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>What follows <c>nan</c>: <c>(snan)</c>, <c>(ind)</c>, any other closed run, or nothing (UCRT <c>:1326-1399</c>).</summary>
    /// <param name="after">The text after <c>nan</c>.</param>
    /// <param name="negative">Whether a <c>-</c> came before it.</param>
    /// <param name="used">How much of <paramref name="after"/> the NaN took: a closed parenthesised run, else 0.</param>
    private static double Nan(ReadOnlySpan<char> after, bool negative, out int used)
    {
        const long Quiet = 0x7fffffffffffffffL;
        const long SignalingBits = 0x7ff0000000000001L;
        const ulong IndeterminateBits = 0xfff8000000000000UL;
        const long Sign = long.MinValue;
        used = 0;
        double quiet = BitConverter.Int64BitsToDouble(negative ? Quiet | Sign : Quiet);

        if (after.Length == 0 || after[0] != '(')
        {
            return quiet;
        }

        // `is_snan` then `is_ind` on ONE source (:1364-1374): each match advances, a failing test keeps what it matched,
        // and the next test starts where the last stopped (`parse_next_characters_from_source`, :1241-1261). So `(sind)` is
        // `s` for `snan`, then `ind)` (B823 review 4).
        int at = 1;

        if (Matches(after, ref at, "snan)"))
        {
            used = at;
            return BitConverter.Int64BitsToDouble(negative ? SignalingBits | Sign : SignalingBits);
        }

        // The indeterminate is "negative" whatever the text's sign (`assemble_floating_point_ind`, :602-613).
        if (Matches(after, ref at, "ind)"))
        {
            used = at;
            return BitConverter.Int64BitsToDouble(unchecked((long)IndeterminateBits));
        }

        // Letters, digits and `_` to a `)` are taken; anything else, or no `)`, leaves `nan` alone (:1379-1398).
        while (at < after.Length && after[at] != ')' && after[at] != '\0')
        {
            if (!char.IsAsciiLetterOrDigit(after[at]) && after[at] != '_')
            {
                return quiet;
            }

            at++;
        }

        if (at < after.Length && after[at] == ')')
        {
            used = at + 1;
        }

        return quiet;
    }

    /// <summary>`parse_next_characters_from_source`: each character of <paramref name="word"/>, without case, advancing on a match and stopping at the first that is not.</summary>
    private static bool Matches(ReadOnlySpan<char> text, ref int at, string word)
    {
        foreach (char expected in word)
        {
            if (at >= text.Length || char.ToLowerInvariant(text[at]) != expected)
            {
                return false;
            }

            at++;
        }

        return true;
    }

    /// <summary>The digits after a <c>0x</c>: hex on both sides of a point, then an optional <c>p</c> exponent, base 2.</summary>
    /// <param name="digits">The text after the <c>0x</c>.</param>
    /// <param name="used">How much of <paramref name="digits"/> the number took; 0 when no hex digit follows.</param>
    /// <returns>The magnitude; 0 when no hex digit follows, the <c>0</c> before the <c>x</c> being the number then.</returns>
    private static double Hexadecimal(ReadOnlySpan<char> digits, out int used)
    {
        System.Numerics.BigInteger mantissa = System.Numerics.BigInteger.Zero;
        int at = 0;
        int fraction = 0;
        bool any = false;
        used = 0;

        for (; at < digits.Length && char.IsAsciiHexDigit(digits[at]); at++, any = true)
        {
            mantissa = (mantissa << 4) + HexValue(digits[at]);
        }

        if (at < digits.Length && digits[at] == '.')
        {
            for (at++; at < digits.Length && char.IsAsciiHexDigit(digits[at]); at++, any = true, fraction++)
            {
                mantissa = (mantissa << 4) + HexValue(digits[at]);
            }
        }

        if (!any)
        {
            return 0d;
        }

        used = at;

        // `p`, a sign, then decimal digits; without a digit it is not an exponent (:1590-1661).
        int exponent = 0;

        if (at < digits.Length && digits[at] is 'p' or 'P')
        {
            int sign = 1;
            int scan = at + 1;

            if (scan < digits.Length && digits[scan] is '+' or '-')
            {
                sign = digits[scan] == '-' ? -1 : 1;
                scan++;
            }

            int first = scan;

            for (; scan < digits.Length && char.IsAsciiDigit(digits[scan]); scan++)
            {
                exponent = Math.Min((exponent * 10) + (digits[scan] - '0'), 100_000);
            }

            exponent *= sign;

            if (scan > first)
            {
                used = scan;
            }
        }

        if (mantissa.IsZero)
        {
            return 0d;
        }

        // Round the mantissa to 53 bits, to nearest and ties to even, by hand: BigInteger's own conversion truncates.
        // Scaling by a power of two is then exact above the subnormals.
        int scale = exponent - (4 * fraction);
        int excess = (int)mantissa.GetBitLength() - 53;

        if (excess > 0)
        {
            System.Numerics.BigInteger kept = mantissa >> excess;
            System.Numerics.BigInteger dropped = mantissa - (kept << excess);
            System.Numerics.BigInteger half = System.Numerics.BigInteger.One << (excess - 1);

            if (dropped > half || (dropped == half && !kept.IsEven))
            {
                kept += 1;
            }

            mantissa = kept;
            scale += excess;
        }

        return Math.ScaleB((double)mantissa, scale);
    }

    private static int HexValue(char digit) => digit <= '9' ? digit - '0' : (digit | 0x20) - 'a' + 10;

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
