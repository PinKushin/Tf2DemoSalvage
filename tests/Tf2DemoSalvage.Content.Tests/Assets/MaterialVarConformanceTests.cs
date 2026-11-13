using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What the material system makes of a VMT value that is no clean number (B728): the four shapes that made eight shipped
/// TF2 materials throw out of <c>MapAssets.Resolve</c>, and the rules behind them.
/// </summary>
/// <remarks>
/// **Read in disassembly** (TF2 x64 <c>materialsystem.dll</c>): <c>FUN_180005f80</c> makes the var. An empty string makes
/// none. A value starting <c>[</c> or <c>{</c> is a vector of up to four elements read by <c>strtod</c>, stopping at
/// <c>]</c>/<c>}</c>; an element <c>strtod</c> cannot start makes none; <c>FUN_180020630</c> zero-fills past the last and
/// takes <c>(int)</c> of the first as the integer. Anything else is a string var, <c>FUN_180020040</c>: every component
/// <c>atof</c> of the text, the integer <c>(int)atof</c>.
/// </remarks>
public sealed class MaterialVarConformanceTests
{
    private static VmtMaterial Material(string key, string value) =>
        VmtMaterial.Parse(Encoding.UTF8.GetBytes($"\"VertexLitGeneric\"\n{{\n\"{key}\" \"{value}\"\n}}\n"));

    /// <remarks>`models/weapons/c_items/c_tomislav_gold` (and the workshop copy) write `$phongboost ""`: no var, so undeclared.</remarks>
    [Test]
    public void Number_AnEmptyValue_IsUndeclared() =>
        Material("$phongboost", string.Empty).Number("$phongboost", 7f).ShouldBe(7f);

    /// <remarks>`$rimlightboost "[.05]"` (`models/player/items/taunts/chicken_bucket` and three more): a one-element vector.</remarks>
    [Test]
    public void Number_AOneElementVector_IsItsElement() =>
        Material("$rimlightboost", "[.05]").Number("$rimlightboost", 1f).ShouldBe(0.05f);

    /// <remarks>`models/items/target_duck`'s `$envmaptint "[.1 .5.1]"`: <c>strtod</c> stops at the second point.</remarks>
    [Test]
    public void Colour_AVectorWithTwoPointsInOneElement_SplitsWhereStrtodStops() =>
        Material("$envmaptint", "[.1 .5.1]").Colour("$envmaptint").ShouldBe((0.1f, 0.5f, 0.1f));

    /// <remarks>`models/props_mining/ingot001`'s `$basemapalphaphongmask ".1"`: <c>(int)atof</c> is 0, so not one.</remarks>
    [Test]
    public void BaseMapAlphaPhongMaskIsOne_AFractionBelowOne_IsFalse()
    {
        Material("$basemapalphaphongmask", "1.9").BaseMapAlphaPhongMaskIsOne.ShouldBeTrue("the control: (int)1.9 is 1");
        Material("$basemapalphaphongmask", ".1").BaseMapAlphaPhongMaskIsOne.ShouldBeFalse();
    }

    /// <remarks>
    /// **A flag is read by `KeyValues::GetInt`, not through the material var** (`materialsystem.dll` `FUN_180008b40` calls
    /// `FUN_180079bf0( kv, NULL, 0 )`), so the value's KeyValues TYPE decides it (`tier1/KeyValues.cpp:2519-2572`, `:1290`): a
    /// token `strtol` reads whole is an int; one `strtod` reads whole and further is a float, truncated; anything else is a
    /// string, read by `atoi`. `"2e-1"` is the float 0.2, so 0 — `atoi` would read 2. `"[1]"` is a string, `atoi` 0.
    /// **An int token past `int`'s range is a string** (`bOverflow`, `:2526`, `:2564`), and the CRT's `atoi` saturates it to
    /// `INT_MAX`/`INT_MIN`, so the flag is set. An 18-character `0x` token is a `TYPE_UINT64`, which `GetInt` reads as 0.
    /// </remarks>
    [TestCase("1", true)]
    [TestCase("2e-1", false)]
    [TestCase("1e1", true)]
    [TestCase("1.9", true)]
    [TestCase("0.9", false)]
    [TestCase("1abc", true)]
    [TestCase("[1]", false)]
    [TestCase("", false)]
    [TestCase(" 1", true)]
    [TestCase("+1", true)]
    [TestCase("-1", true)]
    [TestCase("1.", true)]
    [TestCase(".5", false)]
    [TestCase("-0.5", false)]
    [TestCase("1 ", true)]
    [TestCase("99999999999", true)]
    [TestCase("-99999999999", true)]
    [TestCase("99999999999abc", true)]
    [TestCase("0x0000000000000001", false)]
    public void IsNoCull_AFlagValue_IsKeyValuesGetIntNonZero(string value, bool expected) =>
        Material("$nocull", value).IsNoCull.ShouldBe(expected);

    /// <remarks>
    /// **A shader parameter is no flag** (B728 review): `$phong` is a material var, read by `GetIntValue()`, so `"[1]"` — a
    /// one-element vector var — is 1 there, where the flag path's string reads 0. `$nocull`, a flag, is the control.
    /// </remarks>
    [Test]
    public void HasPhong_AOneElementVector_IsTheVarsIntegerNotAFlagsAtoi()
    {
        Material("$nocull", "[1]").IsNoCull.ShouldBeFalse("the control: a flag reads the string's atoi");
        Material("$phong", "[1]").HasPhong.ShouldBeTrue();
    }

    [Test]
    public void Number_TextAfterANumber_IsTheLeadingNumber() =>
        Material("$phongboost", "2.5x").Number("$phongboost", 1f).ShouldBe(2.5f);

    [Test]
    public void Number_AnExponent_IsRead() =>
        Material("$phongboost", "1e1").Number("$phongboost", 1f).ShouldBe(10f);

    [Test]
    public void Number_ABraceVector_IsDividedBy255() =>
        Material("$phongboost", "{51 0 0}").Number("$phongboost", 1f).ShouldBe(0.2f, 1e-6f);

    [Test]
    public void Number_AnEmptyVector_IsUndeclared() =>
        Material("$phongboost", "[]").Number("$phongboost", 7f).ShouldBe(7f);

    [Test]
    public void Colour_AVectorWithACommaBetweenElements_IsUndeclared() =>
        Material("$color", "[1,0,0]").Colour("$color").ShouldBe((1f, 1f, 1f));

    [Test]
    public void Colour_FiveElements_StopsAtFour() =>
        Material("$color", "[.1 .2 .3 .4 .5]").Colour("$color").ShouldBe((0.1f, 0.2f, 0.3f));
}
