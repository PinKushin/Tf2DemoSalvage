using System;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A model's <c>Refract</c> material, drawn by the model path over the frame it copies (B506).</summary>
/// <remarks>
/// **The subject is the Bazaar Bargain's lens**, `c_bazaar_sniper_lens`: <c>Refract</c> with <c>$model 1</c> and
/// <c>$bluramount 0.5</c> — BLUR 0, since <c>GetIntValue</c> takes the integer part — and no <c>$envmap</c>, so it is the
/// combo this port draws (`refract-census`, 2026-10-05). It is loaded through the production model path from the
/// shipped VPKs; the geometry is a quad over the whole target, so the picture is the shader and nothing else.
/// </remarks>
public sealed class RefractModelRenderTests
{
    private const string Rifle = "models/workshop/weapons/c_models/c_bazaar_sniper/c_bazaar_sniper.mdl";
    private const int Size = 64;

    /// <remarks>
    /// **A model's Refract material resolves to its normal map with the parameters attached, blended**:
    /// <c>InitParamsRefract_DX9</c> sets <c>MATERIAL_VAR_TRANSLUCENT</c> on every one (`refract_dx9_helper.cpp:22`), and
    /// its integer-part <c>$bluramount</c> of 0.5 is 0. Before B506 it resolved no texture at all and drew as the
    /// missing-material chequer.
    /// </remarks>
    [Test]
    public void Load_TheBazaarLensOnAModel_ResolvesARefractSlot()
    {
        MapAssets assets = MapCache.Load(entityModels: [Rifle]);

        MapTexture lens = Lens(assets);

        lens.Refract.ShouldNotBeNull();
        lens.IsTranslucent.ShouldBeTrue("Refract sets MATERIAL_VAR_TRANSLUCENT");
        lens.Refract.BlurAmount.ShouldBe(0, "GetIntValue of 0.5");
    }

    /// <remarks>
    /// **Over a uniform frame the warp moves nothing, so the lens is the frame times its tint**: every sample of
    /// <c>RefractSampler</c> reads the same grey, and <c>colorWarp *= refractTintColor</c> with the tint read linear.
    /// Blended by the normal map's alpha toward the same grey, so whatever the alpha, the pixel is
    /// <c>grey · (1 − a + a · tint)</c> — and with a white tint, exactly the grey. Painting the normal map instead (what a
    /// model draw that is not the refract shader does with this slot) is lilac, not grey.
    /// </remarks>
    [Test]
    public void DrawModelPose_TheLensOverAUniformFrame_IsTheFrameTimesTheTint()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load(entityModels: [Rifle]);
        MapTexture lens = Lens(assets);

        lens.Refract!.RefractTint.ShouldBe((1f, 1f, 1f), "the lens states no $refracttint, so the prediction is the grey");
        lens.Refract.RefractTintTexture.ShouldBeNull();

        target.Clear(0.5f, 0.5f, 0.5f);

        (int, int, int) grey = target.PixelAt(32, 32);

        DrawQuad(target, assets, LensIndex(assets));

        Near(target.PixelAt(32, 32), grey);
        Near(target.PixelAt(5, 50), grey);
    }

    /// <remarks>
    /// **Over a half-lit frame the lens shows only the FRAME**: every pixel is black, the wall, or a linear blend of the
    /// two — never a colour of its own, which is what painting the slot's normal map gives. The control is the frame
    /// drawn without the lens: each column is one value top to bottom.
    ///
    /// **What this cannot see is the warp's size.** The lens's normal map moves the frame by about a pixel at this
    /// size, under the blur of the frame copy's 1024-square resample; zeroing the warp changed 128 pixels against 64
    /// with it, not a prediction worth asserting. The warp arithmetic is the strip pass's, whose tests are exact
    /// (<see cref="RefractTrailRenderTests"/>).
    /// </remarks>
    [Test]
    public void DrawModelPose_TheLensOverAHalfLitFrame_ShowsOnlyTheFrame()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");
        MapAssets assets = MapCache.Load(entityModels: [Rifle]);

        HalfLit(target, assets);

        int[] before = [.. Enumerable.Range(0, Size).Select(column => Sum(target.PixelAt(column, Size / 2)))];

        before[Size - 1].ShouldNotBe(before[0], "the control: the wall must differ from the black half");
        Enumerable.Range(0, Size).Select(row => Sum(target.PixelAt(Size / 2, row))).Distinct().Count()
            .ShouldBe(1, "the control: without the lens a column is one value");

        DrawQuad(target, assets, LensIndex(assets), clearDepth: false);

        int changed = Enumerable.Range(0, Size)
            .SelectMany(column => Enumerable.Range(0, Size).Select(row => (column, row)))
            .Count(at => Sum(target.PixelAt(at.column, at.row)) != before[at.column]);

        TestContext.Out.WriteLine($"REFRACT MODEL {changed} pixels changed of {Size * Size}");

        changed.ShouldBeGreaterThan(0, "the lens must draw for its pixels to be judged");

        // Every pixel is the FRAME — black, the wall, or a blend of the two in linear light — never a colour of its own.
        (int Red, int Green, int Blue) wall = target.PixelAt(Size - 1, 0) is var corner && Sum(corner) == before[Size - 1]
            ? corner
            : throw new InvalidOperationException("the far corner is not the wall");

        for (int column = 0; column < Size; column += 3)
        {
            for (int row = 0; row < Size; row += 3)
            {
                (int red, int green, int blue) = target.PixelAt(column, row);
                double share = SrgbTarget.Decode(Math.Max(red, Math.Max(green, blue))) /
                    SrgbTarget.Decode(Math.Max(wall.Red, Math.Max(wall.Green, wall.Blue)));

                Near((red, green, blue), (Mix(wall.Red, share), Mix(wall.Green, share), Mix(wall.Blue, share)));
            }
        }
    }

    private static int Mix(int channel, double share) => SrgbTarget.Encode(SrgbTarget.Decode(channel) * share);

    private static int Sum((int Red, int Green, int Blue) pixel) => pixel.Red + pixel.Green + pixel.Blue;

    private static void Near((int Red, int Green, int Blue) actual, (int Red, int Green, int Blue) expected)
    {
        Math.Abs(actual.Red - expected.Red).ShouldBeLessThanOrEqualTo(1, $"red {actual} against {expected}");
        Math.Abs(actual.Green - expected.Green).ShouldBeLessThanOrEqualTo(1, $"green {actual} against {expected}");
        Math.Abs(actual.Blue - expected.Blue).ShouldBeLessThanOrEqualTo(1, $"blue {actual} against {expected}");
    }

    private static int LensIndex(MapAssets assets)
    {
        int index = Enumerable.Range(0, assets.Materials.Count)
            .FirstOrDefault(at => assets.Materials[at].Name.Contains("bazaar_sniper_lens", StringComparison.OrdinalIgnoreCase), -1);

        index.ShouldBeGreaterThanOrEqualTo(0, "the rifle's model names its lens material");

        return index;
    }

    private static MapTexture Lens(MapAssets assets) =>
        assets.Textures[LensIndex(assets)] ?? throw new InvalidOperationException("the lens resolved no slot");

    /// <summary>Black, with the right half (x &gt; 0) a white wall, in clip space.</summary>
    private static void HalfLit(OffscreenTarget target, MapAssets assets)
    {
        WorldVertex Corner(float x, float y) => new(x, y, 0.9f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);

        WorldVertex[] wall = [Corner(0f, -1f), Corner(1f, 1f), Corner(1f, -1f), Corner(0f, -1f), Corner(0f, 1f), Corner(1f, 1f)];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [new WorldBatch(0, 0, wall.Length)], Identity, assets, surfaceColours: true, translucent: false);
    }

    /// <summary>The lens material on one quad over the whole target, in front of everything.</summary>
    private static void DrawQuad(OffscreenTarget target, MapAssets assets, int material, bool clearDepth = true)
    {
        WorldVertex Corner(float x, float y, float u, float v) =>
            new(x, y, 0.5f, u, v, 0f, 0f, 1f) { NormalZ = -1f };

        WorldVertex[] quad =
        [
            Corner(-1f, -1f, 0f, 1f), Corner(1f, 1f, 1f, 0f), Corner(1f, -1f, 1f, 1f),
            Corner(-1f, -1f, 0f, 1f), Corner(-1f, 1f, 0f, 0f), Corner(1f, 1f, 1f, 0f),
        ];

        target.DrawModelPose(quad, [new WorldBatch(material, 0, quad.Length)], Identity, Identity, assets, bothSides: true, clearDepth: clearDepth);
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
