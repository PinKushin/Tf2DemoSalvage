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

    [Test]
    public void Atof_ADecimalWithNoLeadingZero_IsRead()
    {
        // The soundscape scripts' own spelling: ".6", ".30".
        CStdlib.Atof(".6").ShouldBe(0.6f);
        CStdlib.Atof(" -1.5e1x").ShouldBe(-15f);
    }

    [Test]
    public void Atof_NoNumber_IsZero()
    {
        CStdlib.Atof("SNDLVL_NORM").ShouldBe(0f);
        CStdlib.Atof(string.Empty).ShouldBe(0f);
    }
}
