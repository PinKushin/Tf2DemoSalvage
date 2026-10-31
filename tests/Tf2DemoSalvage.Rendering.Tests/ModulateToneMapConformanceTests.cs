using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which draws the tone-map scale reaches, on hand-built materials with no game install (B635): a modulating material
/// takes none, a lit one does, and `$additive` does not turn a modulating material into an additive one.
/// </summary>
/// <remarks>
/// **Valve, quoted:** `Modulate` and `DecalModulate` end in `FinalOutput( …, TONEMAP_SCALE_NONE )`
/// (<c>modulate_ps2x.fxc:37</c>, <c>decalmodulate_ps2x.fxc:44</c>), which is <c>result.rgb = vShaderColor.rgb</c>
/// (<c>common_ps_fxc.h:356-358</c>); a lit material's <c>TONEMAP_SCALE_LINEAR</c> multiplies by
/// <c>LINEAR_LIGHT_SCALE</c> (<c>:348-350</c>). `Modulate`'s blend is chosen by `$mod2x` alone
/// (<c>modulate_dx9.cpp:141-148</c>), so `$additive` changes neither its blend nor its scale; five shipped Modulate
/// materials declare it (<c>vmt-param $additive shader=Modulate</c>: <c>effects/red</c> among them).
///
/// **The subject is synthetic on purpose** (D38): a 4x4 texture of one grey, so the texel is known exactly and the
/// guard holds on a machine without TF2. Only a Direct3D device is needed; without one the tests are ignored for that
/// reason alone. Every test draws a control beside its claim: the material must change the cleared surface, so a draw
/// that never happened cannot pass.
/// </remarks>
[NonParallelizable]
public sealed class ModulateToneMapConformanceTests
{
    private const int DecalModulateNeutral = 0;
    private const int DecalModulateDark = 1;
    private const int Lit = 2;
    private const int PlainModulate = 3;
    private const int AdditiveModulate = 4;
    private const int TranslucentLit = 5;

    /// <summary>How a quad reaches the screen.</summary>
    private enum Route
    {
        /// <summary>The overlay pass, as a decal.</summary>
        Decal,

        /// <summary>The model pass, which chooses the blend per material.</summary>
        Model,

        /// <summary>World brushwork: <c>DrawWorld</c>, its opaque pass and then its translucent world.</summary>
        Brush,
    }

    /// <summary>The grey the surface is cleared to, in the target's own terms (188 when read back).</summary>
    private const float Surface = 0.5f;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [
            Grey(128, modulate: true, twice: true, decal: true),
            Grey(32, modulate: true, twice: true, decal: true),

            // 100, so lit at the white texel's light it lands near 138 at scale 1 and near 100 at 0.5: far from the
            // 188 surface and from the 255 clamp, with a wide gap between the two scales.
            Grey(100),
            Grey(128, modulate: true),
            Grey(128, modulate: true, additive: true),
            Grey(100, translucent: true),
        ],
        ["DecalModulate", "DecalModulate", "LightmappedGeneric", "Modulate", "Modulate", "LightmappedGeneric"]);

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Draw_DecalModulateNeutralTexelUnderAnyToneMapScale_LeavesTheSurfaceUnchanged(float scale)
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) neutral = Draw(DecalModulateNeutral, Route.Decal, scale) ?? Skip();
        (int, int, int) dark = Draw(DecalModulateDark, Route.Decal, scale) ?? Skip();

        // The control: the same decal with a dark texel darkens the surface, so the decal draws.
        dark.Item2.ShouldBeLessThan(under.Item2 - 40, $"at scale {scale} the dark texel drew {dark} over {under}: nothing drew");

        Close(neutral, under, $"at scale {scale} the neutral texel moved the surface from {under} to {neutral}");
    }

    [Test]
    public void Draw_LitDecalUnderHalfToneMapScale_IsScaled()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(Lit, Route.Decal, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(Lit, Route.Decal, 0.5f) ?? Skip();
        string drew = $"the lit decal drew {atOne} at scale 1 and {atHalf} at 0.5, over {under}";

        // The control: the lit decal covers the surface, well away from it and from the clamp, or a margin means nothing.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 30, drew);
        atOne.Item2.ShouldBeLessThan(255, drew);

        // TONEMAP_SCALE_LINEAR: a lit material must still be scaled, or a change that unscales every decal passes.
        atHalf.Item2.ShouldBeLessThan(atOne.Item2 - 20, drew);
    }

    [Test]
    public void Draw_PlainModulateOnAModelUnderHalfAndUnitToneMapScale_DrawsTheSame()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(PlainModulate, Route.Model, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(PlainModulate, Route.Model, 0.5f) ?? Skip();

        // The control: a plain (not mod2x) Modulate of 128 halves the surface in gamma, so it visibly darkens it.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 40, $"the Modulate surface drew {atOne} over {under}: nothing drew");

        Close(atHalf, atOne, $"plain Modulate drew {atOne} at scale 1 and {atHalf} at 0.5");
    }

    [Test]
    public void Draw_ModulateDeclaringAdditiveUnderHalfAndUnitToneMapScale_DrawsTheSameMultiplied()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(AdditiveModulate, Route.Model, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(AdditiveModulate, Route.Model, 0.5f) ?? Skip();

        // Multiplied, as Valve's blend is whatever $additive says: it DARKENS, where an additive draw would brighten.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 40, $"the additive Modulate drew {atOne} over {under}: not multiplied");

        Close(atHalf, atOne, $"the additive Modulate drew {atOne} at scale 1 and {atHalf} at 0.5");
    }

    /// <remarks>
    /// **World brushwork** (B635 review): a Modulate run was neither in the opaque pass's skip nor in the translucent
    /// world (<c>IsBlendedMaterial</c> counted translucent and additive only), so it drew in the opaque pass with no blend
    /// at all: its own texel, 176 over a 188 surface. Valve's Modulate enables blending in its shadow state
    /// (<c>modulate_dx9.cpp:141-148</c>), so it multiplies wherever it is drawn.
    /// </remarks>
    [TestCase(PlainModulate)]
    [TestCase(AdditiveModulate)]
    public void Draw_ModulateOnBrushworkUnderHalfAndUnitToneMapScale_MultipliesTheSame(int material)
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(material, Route.Brush, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(material, Route.Brush, 0.5f) ?? Skip();
        (int, int, int) onAModel = Draw(material, Route.Model, 1f) ?? Skip();

        // Multiplied: a plain Modulate of 128 halves the surface in gamma, far below the 176 an opaque draw gives.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 40, $"the brush Modulate drew {atOne} over {under}: not multiplied");

        // Exactly the model pass's multiply over the bare surface: drawing it opaque first, then multiplying over that,
        // also darkens, and only the value tells the two apart.
        Close(atOne, onAModel, $"the brush Modulate drew {atOne}, the same material on a model {onAModel}, over {under}");
        Close(atHalf, atOne, $"the brush Modulate drew {atOne} at scale 1 and {atHalf} at 0.5");
    }

    /// <remarks>
    /// **The harness's own sequence** (B635 review): <c>DrawWorld(translucent: false)</c>, a model, then
    /// <c>DrawTranslucentWorld</c>, as a static prop is drawn between the world and its glass (B426). The constant buffer
    /// keeps the last <c>SetCamera</c>, so the glass drew at the MODEL's scale. It must match the glass drawn in one go.
    /// </remarks>
    [Test]
    public void DrawTranslucentWorld_AfterAModelAtAnotherScale_DrawsAtTheWorldsScale()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Skip();
            return;
        }

        float[] camera = Camera();

        target.Clear(Surface, Surface, Surface);
        target.DrawWorld(Quad(1f), [new WorldBatch(TranslucentLit, 0, 6)], camera, Assets, toneMapScale: 0.5f);

        (int, int, int) together = target.PixelAt(8, 8);

        target.Clear(Surface, Surface, Surface);
        target.DrawWorld(Quad(1f), [new WorldBatch(TranslucentLit, 0, 6)], camera, Assets, translucent: false, toneMapScale: 0.5f);

        // A model off to the side, outside the pixel read, at scale 1.
        target.DrawModelPose(
            Quad(1f, offset: 5000f),
            [new WorldBatch(Lit, 0, 6)],
            camera,
            Identity,
            Assets,
            bothSides: true,
            clearDepth: false,
            toneMapScale: 1f);
        target.DrawTranslucentWorld();

        (int, int, int) apart = target.PixelAt(8, 8);

        // The control: the glass drew at all, so the comparison is not two empty surfaces.
        together.Item2.ShouldBeLessThan(188 - 30, $"the translucent run drew {together}: nothing drew");

        Close(apart, together, $"the glass drew {together} in one go and {apart} after a model at scale 1");
    }

    /// <remarks>
    /// **The pairing the water-fog branch relies on** (B635 review): that branch scales by
    /// <c>ToneMapScale(tintControl.w)</c> and is entered only below fog mode 1.5, so today no modulating material reaches
    /// it and swapping it back to <c>debugModes2.y</c> changes no pixel; a render test there cannot fail. What CAN change
    /// is the pairing below, the moment a modulate decal gets Valve's grey fog (the ponytail at the upload): this goes red
    /// then, and the water branch is the place to look.
    /// </remarks>
    [Test]
    public void FogModeAndToneMapType_ForEveryKindOfMaterial_KeepModulatingOnesOutOfTheScaledFogBranches()
    {
        foreach (MapTexture? material in Assets.Textures.Append(null))
        {
            float fog = WorldRenderer.FogModeOf(material);
            float none = WorldRenderer.ToneMapScaleNoneOf(material);

            // Entering a fog branch (mode below 1.5) must mean TONEMAP_SCALE_LINEAR, or that branch's scale decides.
            if (fog < 1.5f)
            {
                none.ShouldBe(0f, $"{material} enters the fog branches with TONEMAP_SCALE_NONE");
            }
        }

        WorldRenderer.ToneMapScaleNoneOf(Assets.Textures[AdditiveModulate]).ShouldBe(1f, "Modulate with $additive is still NONE");
        WorldRenderer.FogModeOf(Assets.Textures[AdditiveModulate]).ShouldBe(2f, "Modulate with $additive does not fog to black");
        WorldRenderer.ToneMapScaleNoneOf(Assets.Textures[Lit]).ShouldBe(0f, "a lit material is LINEAR");
    }

    private static void Close((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected, string because)
    {
        actual.Red.ShouldBeInRange(expected.Red - 2, expected.Red + 2, because);
        actual.Green.ShouldBeInRange(expected.Green - 2, expected.Green + 2, because);
        actual.Blue.ShouldBeInRange(expected.Blue - 2, expected.Blue + 2, because);
    }

    /// <summary>The cleared surface's centre pixel; null without a device.</summary>
    private static (int, int, int)? Under()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(Surface, Surface, Surface);

        return target.PixelAt(8, 8);
    }

    /// <summary>One quad of one material over the cleared surface, by one route; null without a device.</summary>
    private static (int, int, int)? Draw(int material, Route route, float scale)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(Surface, Surface, Surface);

        // An unlit decal's corners carry the mod2x light (DecalMesh); everything else is lit by the reserved white texel.
        List<WorldVertex> quad = Quad(material is DecalModulateNeutral or DecalModulateDark ? Device3D.DecalModulateLight : 1f);
        float[] camera = Camera();

        switch (route)
        {
            case Route.Decal:
                // A decal goes in the overlay pass with one EMPTY world run, since the world pass returns with none.
                target.DrawWorld(
                    quad,
                    [new WorldBatch(material, 0, 0)],
                    camera,
                    Assets,
                    decals: [new WorldBatch(material, 0, 6, Category: SurfaceCategory.Overlay)],
                    toneMapScale: scale);
                break;

            case Route.Model:
                // The model pass, which chooses the blend per material as production does.
                target.DrawModelPose(quad, [new WorldBatch(material, 0, 6)], camera, Identity, Assets, bothSides: true, toneMapScale: scale);
                break;

            default:
                // World brushwork: the opaque pass, then the translucent world, which blended runs belong to.
                target.DrawWorld(quad, [new WorldBatch(material, 0, 6)], camera, Assets, toneMapScale: scale);
                break;
        }

        return target.PixelAt(8, 8);
    }

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    private static float[] Camera() => new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

    /// <summary>A 128-unit square facing the camera, every corner on one texel, lit by <paramref name="light"/>.</summary>
    private static List<WorldVertex> Quad(float light, float offset = 0f) =>
    [
        new(offset - 64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        new(offset + 64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        new(offset + 64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        new(offset - 64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        new(offset - 64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        new(offset + 64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
    ];

    /// <summary>A 4x4 texture of one grey, opaque, with the flags a shader of that kind gives its material.</summary>
    private static MapTexture Grey(
        byte value, bool modulate = false, bool twice = false, bool additive = false, bool decal = false, bool translucent = false) =>
        new(
            4,
            4,
            4,
            4,
            TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { value, value, value, (byte)255 }).ToArray()),
            IsTransparent: false,
            IsAdditive: additive,
            IsTranslucent: decal || translucent,
            IsModulate: modulate,
            IsModulateTwice: twice,
            IsDecal: decal);

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
