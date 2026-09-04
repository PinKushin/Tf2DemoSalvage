using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a real map's <c>$selfillummask</c> reaches the list the renderer uploads (B327).
/// </summary>
/// <remarks>
/// **The hop that has shipped three no-ops in this project**: a parameter parsed, unit-tested, and
/// read by nothing. `SelfIllumMaskConformanceTests` proves `VmtMaterial` reports the key; it says
/// nothing about whether a map load ever asks, whether the texture resolves, or whether the entry
/// lands at the material's own index — and the failure is silent, because a null entry means "the
/// base map's alpha decides", which is a legitimate value for nearly every material.
///
/// This was found by the parameter census firing on `$selfillummask` the moment the DirectX-level
/// blocks started being read (B326): a real map asks for it, and nothing accounted for it.
/// </remarks>
public sealed class SelfIllumMaskLoadingTests
{
    /// <remarks>
    /// **Counted rather than named, and the count is the assertion.** Naming a material would pin
    /// this to one map's content; what is being tested is that the chain produces anything at all,
    /// and zero is exactly what an unwired resolver returns.
    /// </remarks>
    [Test]
    public void SelfIllumMasks_AfterARealMapLoad_HoldAtLeastOneResolvedTexture()
    {
        MapAssets assets = MapCache.Load();

        int masked = assets.SelfIllumMasks.Count(mask => mask is not null);

        TestContext.Out.WriteLine(
            $"{masked} of {assets.SelfIllumMasks.Count} materials carry a self-illumination mask");

        masked.ShouldBeGreaterThan(
            0, "the parameter census reports a real map asking for $selfillummask");
    }

    /// <remarks>
    /// **The list must be index-parallel with the materials, which is the failure a count cannot
    /// see.** Every per-material list in `MapAssets` is addressed by the same index — a batch's
    /// material number indexes textures, details, bumps and this — so a list built by appending
    /// only the materials that HAVE a mask would have the right count and paint the wrong surfaces.
    /// `MaterialTable.Add` appends all of them at once precisely to stop that, and this asserts the
    /// property that guarantee exists for.
    /// </remarks>
    [Test]
    public void SelfIllumMasks_AfterARealMapLoad_AreIndexParallelWithTheMaterials()
    {
        MapAssets assets = MapCache.Load();

        assets.SelfIllumMasks.Count.ShouldBe(assets.Textures.Count);
    }

    /// <remarks>
    /// **Every mask belongs to a material that lights itself**, which is the engine's gate:
    /// `bool bHasSelfIllumMask = IS_FLAG_SET( MATERIAL_VAR_SELFILLUM ) &amp;&amp; …`
    /// (`vertexlitgeneric_dx9_helper.cpp:289`). A mask resolved for a material without
    /// `$selfillum` would be a texture uploaded for a draw that never samples it — and, worse,
    /// evidence that the resolver dropped the gate rather than that the map declared something odd.
    ///
    /// **Stated coverage limit: this holds VACUOUSLY on this map, and that was measured rather than
    /// assumed.** Removing the `IsSelfIlluminated` gate from the resolver reddens nothing here, so
    /// no material in the fixture declares a mask without also declaring `$selfillum` — the
    /// assertion cannot currently distinguish a resolver that keeps the gate from one that does
    /// not. A sabotage that reddens nothing names a missing INPUT, not a weak assertion
    /// (`docs/memory/a-sabotage-that-reddens-nothing-names-the-missing-input.md`), and the input in
    /// question is a material with `$selfillummask` and no `$selfillum`.
    ///
    /// Kept anyway, for two reasons: it is a real invariant that a future map or model could break,
    /// and its cost is one loop. `SelfIllumMaskConformanceTests` covers the parse side of the same
    /// pair with a fixture that does have the odd shape, since a fixture can be written and a
    /// shipped material cannot.
    /// </remarks>
    [Test]
    public void SelfIllumMasks_EveryOneThatResolved_BelongsToASelfIlluminatedMaterial()
    {
        MapAssets assets = MapCache.Load();

        for (int index = 0; index < assets.SelfIllumMasks.Count; index++)
        {
            if (assets.SelfIllumMasks[index] is null)
            {
                continue;
            }

            assets.Textures[index].ShouldNotBeNull(
                $"material {index} carries a self-illumination mask");

            assets.Textures[index]!.Value.SelfIllum.ShouldNotBeNull(
                $"material {index} '{assets.Materials[index].Name}' has a mask but no $selfillum, "
                + "which the engine gates on");
        }
    }
}
