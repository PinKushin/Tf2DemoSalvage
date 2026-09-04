using System.IO;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>$blendtintbybasealpha</c> reaching the renderer's material state (B331).
/// </summary>
/// <remarks>
/// **The wiring assertion, and a stated limit on what a pixel can prove here.**
/// `TintBlendConformanceTests` proves `VmtMaterial` reads the two flags; this proves a real material
/// carries them out the far side of a map load, which is the hop that has shipped no-ops in this
/// project before.
///
/// ## Why there is no pixel assertion, measured rather than assumed
///
/// The first version of this file rendered a quad twice at different VERTEX alphas and asserted the
/// pixels differed. They did not, and the code was right: the branch reads `first.a` — the BASE
/// TEXTURE's alpha at the sampled UV — and a quad's vertex alpha never reaches it. An input the
/// manipulation does not touch is the "wrong condition" case of
/// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md`, and it produced a red test against
/// correct code.
///
/// **Three further conditions were considered and each is confounded:**
///
/// 1. **Two UVs of one texture.** The alpha differs between texels, but so does the albedo, so a
///    plain multiply also produces two different pixels. Nothing separates the branches.
/// 2. **A texel with alpha 0 against one with alpha 1.** Under the tint branch the first is
///    untinted and the second is fully tinted; under the multiply branch both are tinted. The two
///    readings differ by a factor of the modulation — which cancels only if the two texels share an
///    albedo, and no shipped texture guarantees a region of constant colour and varying alpha.
/// 3. **A control material identical but for the flag.** It does not exist in shipped content, and
///    authoring one would need a VMT this project cannot inject into a map load.
///
/// So the branch itself is verified by reading — it is Valve's four lines transcribed, cited on the
/// shader — and by the conformance suite, and what is asserted here is that the material state it
/// consumes arrives populated. Recorded in full rather than left as a gap, the way
/// `PhongRenderTests` records the same shape for `$lightwarptexture`.
/// </remarks>
public sealed class TintBlendRenderTests
{
    /// <remarks>
    /// **A cosmetic, because no brush material tints this way.** `$blendtintbybasealpha` is TF2's
    /// tintable-item mechanism; loading the map alone finds nothing, and a test that skipped on
    /// that would report the absence as "not installed" rather than as "looked in the wrong place".
    /// </remarks>
    [Test]
    public void TintsByBaseAlpha_APaintableCosmetic_ReachesTheMaterialStateWithAColour()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int tinting = assets.Textures.Count(texture => texture is { TintsByBaseAlpha: true });

        int both = assets.Textures.Count(
            texture => texture is { TintsByBaseAlpha: true, Modulation: not null });

        TestContext.Out.WriteLine(
            $"{tinting} of {assets.Textures.Count} materials tint by base alpha, {both} with a colour");

        tinting.ShouldBeGreaterThan(
            0, "the loaded cosmetics declare $blendtintbybasealpha and it must survive the load");

        // **Both halves, because either alone is inert.** The flag without a colour multiplies by
        // white and changes no pixel; a colour without the flag takes the other branch entirely. A
        // painted item needs the pair, and asserting only the flag would pass against a load that
        // dropped every modulation.
        both.ShouldBeGreaterThan(0, "a tintable item carries $colortint_base as its $color2");
    }

    /// <remarks>
    /// **The control, and it is what stops the assertion above meaning "everything tints".** The
    /// map's own brushwork must NOT carry the flag — it is a model mechanism — so a reader that
    /// answered true by default would change how every surface in the game draws and still pass the
    /// test above.
    /// </remarks>
    [Test]
    public void TintsByBaseAlpha_TheMapsOwnBrushMaterials_DoNotTintByAlpha()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int brushwork = Enumerable.Range(0, assets.BrushMaterialCount)
            .Count(index => assets.Textures[index] is { TintsByBaseAlpha: true });

        brushwork.ShouldBe(
            0, "$blendtintbybasealpha is TF2's tintable-item mechanism, not a brushwork one");
    }

    /// <remarks>
    /// `$blendtintcoloroverbase` is a LERP and every shipped cosmetic sets it to zero — the end that
    /// keeps the texture's detail under the colour. Asserted so a reader defaulting it to one, which
    /// would paint every tintable region flat, cannot pass.
    /// </remarks>
    [Test]
    public void TintOverBase_TheLoadedCosmetics_KeepTheirTextureUnderTheColour()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        foreach (MapTexture? texture in assets.Textures)
        {
            if (texture is { TintsByBaseAlpha: true })
            {
                texture.Value.TintOverBase.ShouldBe(0f);
            }
        }
    }

    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load(entityModels:
            [
                "models/workshop/player/items/all_class/hwn2019_horrible_horns/hwn2019_horrible_horns_scout.mdl",
                "models/player/items/scout/bit_trippers_scout.mdl",
                "models/player/scout.mdl",
            ]);
        }
    }
}
