using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A scheme's colours and base settings, as `vgui2.dll`'s `CScheme` resolves them.</summary>
/// <remarks>
/// Renamed in `tf2vgui2.gpr`: `CScheme_LoadFromFile` (0x18000e570) seeds 117 base settings from the table at 0x18007b760;
/// `CScheme_LookupSchemeSetting` (0x18000e8c0) returns a name that already scans as three numbers, else its `Colors` value
/// as is, else its `BaseSettings` value resolved again, else the name; `GetColor` (0x18000d000) scans `%d %d %d %d` into
/// zeroed ints and takes three or more, byte-truncated, else the caller's default.
/// </remarks>
public sealed class VguiSchemeColourConformanceTests
{
    private static readonly (byte, byte, byte, byte) Missing = (9, 9, 9, 9);

    [Test]
    public void GetColor_ANamedColour_IsItsNumbers() =>
        Scheme("""Colors { "Orange" "255 128 0 255" }""").GetColor("Orange", Missing).ShouldBe(((byte)255, (byte)128, (byte)0, (byte)255));

    [Test]
    public void GetColor_ABaseSettingChain_ResolvesThroughToTheColour() =>
        Scheme("""Colors { "Orange" "255 128 0 255" } BaseSettings { "A" "B" "B" "Orange" }""")
            .GetColor("A", Missing).ShouldBe(((byte)255, (byte)128, (byte)0, (byte)255));

    [Test]
    public void GetColor_AColourNamingAnotherColour_IsNotFollowed()
    {
        // A `Colors` hit is returned as it stands; only `BaseSettings` values are resolved again.
        Scheme("""Colors { "Orange" "255 128 0 255" "Alias" "Orange" }""").GetColor("Alias", Missing).ShouldBe(Missing);
    }

    [Test]
    public void GetColor_ThreeNumbers_HaveAZeroAlpha() =>
        Scheme("""Colors { "Rgb" "10 20 30" }""").GetColor("Rgb", Missing).ShouldBe(((byte)10, (byte)20, (byte)30, (byte)0));

    [Test]
    public void GetColor_ANumberAboveAByte_IsTruncated() =>
        Scheme("""Colors { "Wide" "300 0 0 255" }""").GetColor("Wide", Missing).ShouldBe(((byte)44, (byte)0, (byte)0, (byte)255));

    [Test]
    public void GetColor_AnUnknownName_IsTheDefault() =>
        Scheme("""Colors { }""").GetColor("Nothing", Missing).ShouldBe(Missing);

    [Test]
    public void Load_ABaseSettingTheFileOmits_IsSeededFromItsFallback() =>
        Scheme("""Colors { "FgColor" "1 2 3 4" }""").GetColor("Panel.FgColor", Missing).ShouldBe(((byte)1, (byte)2, (byte)3, (byte)4));

    [Test]
    public void Load_TheConcatenatedBorderDarkEntry_SeedsItsJoinedNameAndNotBorderDark()
    {
        // `{ "Border.Dark" "BorderDark", "40 40 40 196", NULL }` in Valve's table: a missing comma joins the first two.
        VguiScheme scheme = Scheme("""Colors { }""");

        scheme.GetColor("Border.DarkBorderDark", Missing).ShouldBe(((byte)40, (byte)40, (byte)40, (byte)196));
        scheme.GetColor("Border.Dark", Missing).ShouldBe(Missing);
    }

    [Test]
    public void Load_AFallbackNamedButUnresolved_TakesTheFallbacksNameNotTheDefault()
    {
        // `Border.Bright`'s default "200 200 200 196" is never used: its fallback "BorderBright" is named, and an
        // unresolved name resolves to itself.
        Scheme("""Colors { }""").GetColor("Border.Bright", Missing).ShouldBe(Missing);
    }

    private static VguiScheme Scheme(string body) =>
        VguiScheme.Load(KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { " + body + " }"), "test.res", _ => null));
}
