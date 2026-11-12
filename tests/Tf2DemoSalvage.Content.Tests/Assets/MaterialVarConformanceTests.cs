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
