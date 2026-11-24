using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>C's <c>atoi</c> and <c>atof</c>, which every KeyValues <c>GetInt</c>/<c>GetFloat</c> and entity keyfield reads through.</summary>
/// <remarks>
/// **The C standard's contract, not a .NET parser's**: leading white space is skipped, the longest prefix that reads as a
/// number is taken, and nothing at all is zero. `ParseKeyvalue` reads a boolean keyfield as `atoi( szValue ) != 0`
/// (<c>saverestore_gamedll.cpp:62</c>); a TryParse with a fallback gives a different answer for every value it rejects.
/// </remarks>
public sealed class CStdlibConformanceTests
{
    [Test]
    public void Atoi_LeadingSpaceASignAndTrailingText_IsTheSignedPrefix()
    {
        CStdlib.Atoi("  -12abc").ShouldBe(-12);
        CStdlib.Atoi("+7").ShouldBe(7);
        CStdlib.Atoi("3.9").ShouldBe(3, "atoi stops at the point rather than rounding");
    }

    [Test]
    public void Atoi_NoDigits_IsZero()
    {
        CStdlib.Atoi("yes").ShouldBe(0);
        CStdlib.Atoi("-").ShouldBe(0);
        CStdlib.Atoi(string.Empty).ShouldBe(0);
    }

    /// <remarks>
    /// **The Microsoft C runtime clamps** — <i>"atoi and _wtoi return INT_MAX and INT_MIN on these conditions"</i>, its
    /// own example reading <c>"3336402735171707160320"</c> as 2147483647 (learn.microsoft.com, <c>atoi</c>). The engine
    /// binaries this project reads are built against it. A TryParse that fails on overflow answered zero.
    /// </remarks>
    [Test]
    public void Atoi_ANumberPastTheIntRange_ClampsAsTheMicrosoftRuntimeDoes()
    {
        CStdlib.Atoi("3336402735171707160320").ShouldBe(int.MaxValue);
        CStdlib.Atoi("-99999999999x").ShouldBe(int.MinValue);
        CStdlib.Atoi("2147483647").ShouldBe(int.MaxValue, "the control: the largest int is itself");
        CStdlib.Atoi("-2147483648").ShouldBe(int.MinValue);
    }

    /// <remarks>
    /// **C's white space is six characters** — space, <c>\t \n \v \f \r</c> (C11 7.4.1.10, the "C" locale) — not .NET's
    /// Unicode set. A byte 0xA0 read as Latin-1 is a no-break space to <c>char.IsWhiteSpace</c> and a non-digit to C, so
    /// it stops the scan at once.
    /// </remarks>
    [Test]
    public void AtoiAndAtof_LeadingWhiteSpace_IsOnlyCs()
    {
        CStdlib.Atoi("\v\f7").ShouldBe(7);
        CStdlib.Atoi(" 7").ShouldBe(0);
        CStdlib.Atof("\t\r.5").ShouldBe(0.5d);
        CStdlib.Atof(" .5").ShouldBe(0d);
    }

    /// <remarks>
    /// **<c>atof</c> reads <c>inf</c>, <c>infinity</c> and <c>nan</c>, without case** (C11 7.22.1.3). The NaN's bits are
    /// the runtime's: narrowed to float, vphysics' own surface table held <c>0x7fffffff</c> for <c>"nan"</c>
    /// (`VphysicsSurfaceDataConformanceTests`, read back from the shipped binary).
    /// </remarks>
    [Test]
    public void Atof_InfinityAndNan_AreTheCRuntimesSpecials()
    {
        CStdlib.Atof("inf").ShouldBe(double.PositiveInfinity);
        CStdlib.Atof(" -Infinity").ShouldBe(double.NegativeInfinity);
        CStdlib.Atof("INFx").ShouldBe(double.PositiveInfinity);
        BitConverter.SingleToInt32Bits((float)CStdlib.Atof("nan")).ShouldBe(0x7fffffff);
        BitConverter.SingleToInt32Bits((float)CStdlib.Atof("-NaN")).ShouldBe(unchecked((int)0xffffffff));
        CStdlib.Atof("in").ShouldBe(0d, "the control: a prefix of a special is no number");
    }

    /// <remarks>
    /// **The Microsoft runtime reads hexadecimal floats** (B823 review 1, Fable, Sonnet): UCRT's
    /// <c>parse_floating_point_from_source</c> (Windows SDK 10.0.26100.0, <c>ucrt/inc/corecrt_internal_strtox.h:1452-1475</c>)
    /// takes a <c>0x</c> prefix, hex digits on both sides of the point, and a <c>p</c> exponent in decimal, base 2
    /// (<c>:1590-1594</c>, <c>:1700</c>); <c>0x</c> with no digit after it is the subject <c>0</c> (<c>:1563-1570</c>), and
    /// <c>p</c> with no digit after it is not an exponent (<c>:1648-1661</c>).
    /// </remarks>
    [TestCase("0x10", 16d)]
    [TestCase(" -0X1.8p1", -3d)]
    [TestCase("0x.8", 0.5d)]
    [TestCase("0x1p-2", 0.25d)]
    [TestCase("0x1p", 1d)]
    [TestCase("0xg", 0d)]
    [TestCase("0x", 0d)]
    [TestCase("0x1e", 30d)]
    [TestCase("0x8000000000000001", 9223372036854775808d)]
    [TestCase("0x20000000000003", 9007199254740996d)]
    public void Atof_AHexadecimalFloat_IsReadAsTheMicrosoftRuntimeReadsIt(string text, double expected) =>
        CStdlib.Atof(text).ShouldBe(expected);

    /// <remarks>
    /// **`nan(…)` is a NaN, and two of its spellings are not the quiet one** (UCRT <c>corecrt_internal_strtox.h:1326-1399</c>,
    /// <c>:552-613</c>): <c>nan(snan)</c> has mantissa 1; <c>nan(ind)</c> is the "negative" indeterminate, sign set and
    /// only the quiet bit; any other run of letters, digits and <c>_</c> closed by <c>)</c> is the quiet NaN; an unclosed
    /// one is <c>nan</c> alone. A sign carries to the quiet and signalling NaNs.
    /// </remarks>
    [TestCase("nan(snan)", 0x7ff0000000000001L)]
    [TestCase("-nan(SNAN)", unchecked((long)0xfff0000000000001UL))]
    [TestCase("nan(ind)", unchecked((long)0xfff8000000000000UL))]
    [TestCase("nan(x_1)", 0x7fffffffffffffffL)]
    [TestCase("nan(x", 0x7fffffffffffffffL)]
    [TestCase("-nan", unchecked((long)0xffffffffffffffffUL))]
    public void Atof_ANanSpelling_IsTheRuntimesNan(string text, long bits) =>
        BitConverter.DoubleToInt64Bits(CStdlib.Atof(text)).ShouldBe(bits);

    /// <remarks>
    /// **Where `strtod`'s end pointer stops** (B823 review 3, Fable): the subject sequence UCRT's parser accepts
    /// (<c>corecrt_internal_strtox.h:1401-1722</c>) — leading white space counted, `0x` with no digit the `0` alone, a `p` or
    /// `e` with no digit not taken, `nan(` taken only when closed.
    /// </remarks>
    [TestCase("0.1f", 3)]
    [TestCase("255 255 255", 3)]
    [TestCase("  12", 4)]
    [TestCase("abc", 0)]
    [TestCase("", 0)]
    [TestCase("inf", 3)]
    [TestCase("-Infinity", 9)]
    [TestCase("nan(x_1)", 8)]
    [TestCase("nan(x", 3)]
    [TestCase("0x", 1)]
    [TestCase("0x1p", 3)]
    [TestCase("0x1p2", 5)]
    [TestCase("1e", 1)]
    [TestCase("1e5", 3)]
    public void Atof_AText_UsesWhatStrtodTakes(string text, int used)
    {
        _ = CStdlib.Atof(text, out int took);

        took.ShouldBe(used);
    }

    [Test]
    public void Atof_ADecimalWithNoLeadingZero_IsRead()
    {
        // The soundscape scripts' own spelling: ".6", ".30".
        CStdlib.Atof(".6").ShouldBe(0.6d);
        CStdlib.Atof(" -1.5e1x").ShouldBe(-15d);
    }

    [Test]
    public void Atof_NoNumber_IsZero()
    {
        CStdlib.Atof("SNDLVL_NORM").ShouldBe(0d);
        CStdlib.Atof(string.Empty).ShouldBe(0d);
    }

    /// <remarks>
    /// **`atof` returns a DOUBLE** (C11 7.22.1.2, <c>double atof( const char *nptr )</c>), and the engine narrows it only
    /// where it assigns — <c>tmp.start = atof( token )</c> into <c>interval_t::start</c>, a float (<c>interval.cpp:34</c>).
    /// Two roundings are not one: this text lies a hair above the midpoint between 1 and the next float, so read straight
    /// to float it rounds UP to <c>1.00000012</c>, while read to double it lands exactly ON the midpoint (1 + 2⁻²⁴, which
    /// a double holds) and the float conversion then rounds to even, which is 1.
    /// </remarks>
    [Test]
    public void Atof_AValueJustAboveAFloatMidpoint_IsTheDoubleAndNarrowsToTheEvenFloat()
    {
        const string Text = "1.0000000596046447755";

        CStdlib.Atof(Text).ShouldBe(1d + Math.Pow(2, -24), "the double nearest the text is the midpoint itself");
        BitConverter.SingleToInt32Bits((float)CStdlib.Atof(Text)).ShouldBe(0x3f800000, "narrowed, the tie goes to 1");

        // The control: the case discriminates — a single rounding straight to float gives the other neighbour.
        BitConverter.SingleToInt32Bits(float.Parse(Text, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(0x3f800001);
    }
}
