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

    /// <summary>The grey the surface is cleared to, in the target's own terms (188 when read back).</summary>
    private const float Surface = 0.5f;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [
            Grey(128, modulate: true, twice: true, decal: true),
            Grey(32, modulate: true, twice: true, decal: true),
            Grey(200),
            Grey(128, modulate: true),
            Grey(128, modulate: true, additive: true),
        ],
        ["DecalModulate", "DecalModulate", "LightmappedGeneric", "Modulate", "Modulate"]);

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Draw_DecalModulateNeutralTexelUnderAnyToneMapScale_LeavesTheSurfaceUnchanged(float scale)
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) neutral = Draw(DecalModulateNeutral, asDecal: true, scale) ?? Skip();
        (int, int, int) dark = Draw(DecalModulateDark, asDecal: true, scale) ?? Skip();

        // The control: the same decal with a dark texel darkens the surface, so the decal draws.
        dark.Item2.ShouldBeLessThan(under.Item2 - 40, $"at scale {scale} the dark texel drew {dark} over {under}: nothing drew");

        Close(neutral, under, $"at scale {scale} the neutral texel moved the surface from {under} to {neutral}");
    }

    [Test]
    public void Draw_LitDecalUnderHalfToneMapScale_IsScaled()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(Lit, asDecal: true, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(Lit, asDecal: true, 0.5f) ?? Skip();

        // The control: the lit decal covers the surface at all.
        System.Math.Abs(atOne.Item2 - under.Item2).ShouldBeGreaterThan(10, $"the lit decal drew {atOne} over {under}: nothing drew");

        // TONEMAP_SCALE_LINEAR: a lit material must still be scaled, or a change that unscales every decal passes.
        atHalf.Item2.ShouldBeLessThan(atOne.Item2 - 20, $"the lit decal drew {atOne} at scale 1 and {atHalf} at 0.5");
    }

    [Test]
    public void Draw_PlainModulateOnTheWorldUnderHalfAndUnitToneMapScale_DrawsTheSame()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(PlainModulate, asDecal: false, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(PlainModulate, asDecal: false, 0.5f) ?? Skip();

        // The control: a plain (not mod2x) Modulate of 128 halves the surface in gamma, so it visibly darkens it.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 40, $"the Modulate surface drew {atOne} over {under}: nothing drew");

        Close(atHalf, atOne, $"plain Modulate drew {atOne} at scale 1 and {atHalf} at 0.5");
    }

    [Test]
    public void Draw_ModulateDeclaringAdditiveUnderHalfAndUnitToneMapScale_DrawsTheSameMultiplied()
    {
        (int, int, int) under = Under() ?? Skip();
        (int, int, int) atOne = Draw(AdditiveModulate, asDecal: false, 1f) ?? Skip();
        (int, int, int) atHalf = Draw(AdditiveModulate, asDecal: false, 0.5f) ?? Skip();

        // Multiplied, as Valve's blend is whatever $additive says: it DARKENS, where an additive draw would brighten.
        atOne.Item2.ShouldBeLessThan(under.Item2 - 40, $"the additive Modulate drew {atOne} over {under}: not multiplied");

        Close(atHalf, atOne, $"the additive Modulate drew {atOne} at scale 1 and {atHalf} at 0.5");
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

    /// <summary>One quad of one material over the cleared surface, as a decal or as world geometry; null without a device.</summary>
    private static (int, int, int)? Draw(int material, bool asDecal, float scale)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(Surface, Surface, Surface);

        // An unlit decal's corners carry the mod2x light (DecalMesh); everything else is lit by the reserved white texel.
        float light = material is DecalModulateNeutral or DecalModulateDark ? Device3D.DecalModulateLight : 1f;
        List<WorldVertex> quad =
        [
            new(-64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
            new(-64f, 0f, -64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
            new(-64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
            new(64f, 0f, 64f, 0.5f, 0.5f, 0f, 0f, 0f, light, light, light),
        ];

        float[] camera = new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix();

        if (asDecal)
        {
            // A decal goes in the overlay pass with one EMPTY world run, since the world pass returns with none.
            target.DrawWorld(
                quad,
                [new WorldBatch(material, 0, 0)],
                camera,
                Assets,
                decals: [new WorldBatch(material, 0, 6, Category: SurfaceCategory.Overlay)],
                toneMapScale: scale);
        }
        else
        {
            // A surface that is not a decal: the model pass, which chooses the blend per material as production does
            // (a Modulate on brushwork reaches the screen through the blended world, not the offscreen opaque list).
            target.DrawModelPose(
                quad,
                [new WorldBatch(material, 0, 6)],
                camera,
                [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f],
                Assets,
                bothSides: true,
                toneMapScale: scale);
        }

        return target.PixelAt(8, 8);
    }

    /// <summary>A 4x4 texture of one grey, opaque, with the flags a shader of that kind gives its material.</summary>
    private static MapTexture Grey(byte value, bool modulate = false, bool twice = false, bool additive = false, bool decal = false) =>
        new(
            4,
            4,
            4,
            4,
            TextureImage.Rgba(Enumerable.Range(0, 16).SelectMany(_ => new[] { value, value, value, (byte)255 }).ToArray()),
            IsTransparent: false,
            IsAdditive: additive,
            IsTranslucent: decal,
            IsModulate: modulate,
            IsModulateTwice: twice,
            IsDecal: decal);

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
