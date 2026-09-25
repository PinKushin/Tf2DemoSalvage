using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `R_AnimateLight` (`engine.dll` `0x1800d3ec0`): each of 64 styles takes its pattern from the `lightstyles` table and
/// sets `d_lightstylevalue` to `( pattern[ (int)( time · 10 ) % length ] − 'a' ) · 22`, or 256 for an empty pattern, and
/// stamps the style when its value changes.
/// </summary>
public sealed class LightStyleValuesConformanceTests
{
    [Test]
    public void Advance_ASteadyM_Is264SoAScaleOfOne()
    {
        LightStyleValues values = new();

        values.Set(0, "m");
        values.Advance(0.0);

        values.Value(0).ShouldBe(264);
        values.Scale(0).ShouldBe(1f);
    }

    [Test]
    public void Advance_AnEmptyPattern_Is256()
    {
        LightStyleValues values = new();

        values.Advance(0.0);

        values.Value(13).ShouldBe(256);
    }

    /// <remarks>"abc" at 0.25 s is step 2, 'c': 2 · 22. At 0.31 s it is step 3, which wraps to 'a'.</remarks>
    [Test]
    public void Advance_APattern_StepsTenTimesASecondAndWraps()
    {
        LightStyleValues values = new();

        values.Set(1, "abc");

        values.Advance(0.25);
        values.Value(1).ShouldBe(44);

        values.Advance(0.31);
        values.Value(1).ShouldBe(0);
    }

    [Test]
    public void Advance_AStyleWhoseValueChanged_IsReportedAndOneThatDidNotIsNot()
    {
        LightStyleValues values = new();

        values.Set(32, "m");
        values.Set(33, "m");
        values.Advance(0.0);

        values.Set(32, "a");

        HashSet<int> changed = [.. values.Advance(0.05)];

        changed.ShouldContain(32);
        changed.ShouldNotContain(33);
    }
}
