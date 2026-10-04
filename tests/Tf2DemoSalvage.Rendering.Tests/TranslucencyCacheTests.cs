using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether a renderable's materials are translucent is decided when it is registered and kept on it (B262):
/// <c>CreateRenderableHandle</c> asks <c>IsTransparent()</c> once (<c>clientleafsystem.cpp:651</c>) and stores the group
/// on the handle (<c>NewRenderable</c>, <c>:631</c>), where collation reads it (<c>:1607</c>, <c>:1678</c>).
/// </summary>
public sealed class TranslucencyCacheTests
{
    private static readonly IReadOnlyDictionary<int, int> NoSwap = new Dictionary<int, int>();

    [Test]
    public void For_TheSameRenderableTwiceUnchanged_AsksTheMaterialsOnce()
    {
        TranslucencyCache cache = new();
        int asked = 0;

        cache.For((1, "a.mdl"), 0, NoSwap, null, 0, () => { asked++; return true; }).ShouldBeTrue();
        cache.For((1, "a.mdl"), 0, NoSwap, null, 0, () => { asked++; return false; }).ShouldBeTrue();

        asked.ShouldBe(1);
    }

    /// <remarks>The control: when what selects its materials changes — here the body — it is asked again.</remarks>
    [Test]
    public void For_ARenderableWhoseBodyChanged_AsksAgain()
    {
        TranslucencyCache cache = new();
        int asked = 0;

        cache.For((1, "a.mdl"), 0, NoSwap, null, 0, () => { asked++; return true; });
        cache.For((1, "a.mdl"), 0, NoSwap, null, 1, () => { asked++; return false; }).ShouldBeFalse();

        asked.ShouldBe(2);
    }

    [Test]
    public void For_TwoRenderablesOfOneModel_AsksForEach()
    {
        TranslucencyCache cache = new();
        int asked = 0;

        cache.For((1, "a.mdl"), 0, NoSwap, null, 0, () => { asked++; return true; });
        cache.For((2, "a.mdl"), 0, NoSwap, null, 0, () => { asked++; return true; });

        asked.ShouldBe(2);
    }
}
