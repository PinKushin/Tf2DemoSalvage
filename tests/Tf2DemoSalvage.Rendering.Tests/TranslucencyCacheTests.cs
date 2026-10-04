namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A model's material translucency is a flag ON THE MODEL, not on the entity (B262).
/// </summary>
/// <remarks>
/// <c>C_BaseEntity::IsTransparent</c> asks <c>modelinfo->IsTranslucent(model)</c> (<c>c_baseentity.cpp:1825</c>), and
/// engine.dll's implementation (<c>CModelInfoClient</c> vtable slot 12, <c>0x1801cabf0</c>) is one bit read off the
/// model: <c>TEST byte ptr [RDX + 0x24],0x2</c>. That bit is written only by <c>Mod_RecomputeTranslucency</c>
/// (<c>0x1801046f0</c>), reached through <c>RecomputeTranslucency</c> (slot 14, <c>0x1801cac50</c>), which the SDK's client
/// calls for detail models alone (<c>detailobjectsystem.cpp:2809</c>). An entity's skin or body never re-asks it.
/// What IS recomputed per frame is the group, from that flag plus the alpha and render mode
/// (<c>ComputeFxBlend</c> → <c>SetRenderGroup( GetRenderGroup() )</c>, <c>c_baseentity.cpp:3545</c>, <c>:5661-5701</c>).
/// </remarks>
public sealed class TranslucencyCacheTests
{
    [Test]
    public void For_TwoEntitiesOfOneModel_AsksTheMaterialsOnce()
    {
        TranslucencyCache cache = new();
        int asked = 0;

        cache.For("a.mdl", () => { asked++; return true; }).ShouldBeTrue();
        cache.For("a.mdl", () => { asked++; return false; }).ShouldBeTrue();

        asked.ShouldBe(1);
    }

    /// <remarks>The control: another model is its own flag.</remarks>
    [Test]
    public void For_AnotherModel_AsksAgain()
    {
        TranslucencyCache cache = new();
        int asked = 0;

        cache.For("a.mdl", () => { asked++; return true; });
        cache.For("b.mdl", () => { asked++; return false; }).ShouldBeFalse();

        asked.ShouldBe(2);
    }
}
