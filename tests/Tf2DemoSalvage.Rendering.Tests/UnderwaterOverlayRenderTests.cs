using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary><c>DrawUnderwaterOverlay</c>'s plain rectangle, drawn for the error material (B660, B661, B711).</summary>
public sealed class UnderwaterOverlayRenderTests
{
    private const int Size = 64;

    /// <remarks>
    /// **The error material over the whole screen** (`viewrender.cpp:1332-1338`): <c>DrawScreenSpaceRectangle( mat, 0, 0, w,
    /// h, 0, 0, 1, 1, 1, 1 )</c> maps the 32-texel checkerboard once across 64 pixels, so each 4-texel square is 8 pixels.
    /// Sampled at square centres, where the bilinear taps fall inside one square: magenta at (4, 4) and (12, 12), and the
    /// black square at (12, 4) draws its black opaquely over the cleared grey. The control: the player's overlay slot, which
    /// is <c>ViewDrawFade</c>'s, draws nothing for it.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_TheErrorMaterialUnderWater_IsTheCheckerboardOverTheScreen()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(0.5f, 0.5f, 0.5f);
        target.DrawScreenOverlay(ScreenOverlayMaterial.Error, 0.0).ShouldBeFalse("the player's slot has no fade for it");

        target.DrawScreenOverlay(ScreenOverlayMaterial.Error, 0.0, underwater: true).ShouldBeTrue();

        (target.PixelAt(4, 4), target.PixelAt(12, 12), target.PixelAt(12, 4), target.PixelAt(4, 12)).ShouldBe(
            ((255, 0, 255), (255, 0, 255), (0, 0, 0), (0, 0, 0)));
    }

    /// <remarks>
    /// **The texture runs from half way across it** (`ScreenRectangle.Edges`): a 2-texel texture, red then blue, over 64
    /// pixels. Pixel 16's centre is at u = 0.492 + 16.5/64 × 1.016 = 0.754, a quarter past the blue texel's centre at
    /// 0.75, so it reads blue; a rectangle from 0 to 1 would put red's centre (0.25) there. Bilinear, 0.8% red leaks in.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_APlainOverlay_StartsHalfATextureIn()
    {
        byte[] texels = [255, 0, 0, 255, 0, 0, 255, 255, 255, 0, 0, 255, 0, 0, 255, 255];
        ScreenOverlayMaterial halves = ScreenOverlayMaterial.Error with
        {
            Name = "effects/synthetic_halves",
            Plain = new MapTexture(2, 2, 2, 2, TextureImage.Rgba(texels), IsTransparent: false),
        };

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(0f, 0f, 0f);
        target.DrawScreenOverlay(halves, 0.0, underwater: true).ShouldBeTrue();

        (int red, _, int blue) = target.PixelAt(16, 16);

        TestContext.Out.WriteLine($"PIXEL 16: red {red}, blue {blue}");
        blue.ShouldBeGreaterThan(240);
        red.ShouldBeLessThan(40);
    }

    /// <remarks>
    /// **A translucent plain overlay blends by its alpha, `BT_BLEND`** (BaseShader.h): a hand-built texture of black at
    /// alpha 128 over the cleared white. The target stores sRGB and blends in linear, as every translucent draw on it does,
    /// so white keeps 1 − 128/255 = 0.498 of its light: 187.6 encoded, where an opaque black would read 0.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_ATranslucentPlainOverlay_BlendsByItsAlpha()
    {
        byte[] texels = [0, 0, 0, 128, 0, 0, 0, 128, 0, 0, 0, 128, 0, 0, 0, 128];
        ScreenOverlayMaterial translucent = ScreenOverlayMaterial.Error with
        {
            Name = "effects/synthetic_translucent",
            Plain = new MapTexture(2, 2, 2, 2, TextureImage.Rgba(texels), IsTransparent: false, IsTranslucent: true),
        };

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(1f, 1f, 1f);
        target.DrawScreenOverlay(translucent, 0.0, underwater: true).ShouldBeTrue();

        (int red, _, _) = target.PixelAt(12, 4);

        red.ShouldBeInRange(187, 188);
    }

    /// <remarks>
    /// **`$additive` is `BT_ADD`, `src + dst`, or `BT_BLENDADD`, `src × α + dst`, when also translucent** (BaseShader.h):
    /// a texture of red 255 at alpha 128 added over a cleared blue of 0.5. Additive alone adds red outright, so red reads
    /// 255; blend-add adds 128/255 of it, 0.502 linear, 188 encoded. Blue is kept by both, 0.5 linear, 188.
    /// </remarks>
    [TestCase(false, 255)]
    [TestCase(true, 188)]
    public void DrawScreenOverlay_AnAdditivePlainOverlay_AddsByItsBlend(bool translucent, int red)
    {
        byte[] texels = [255, 0, 0, 128, 255, 0, 0, 128, 255, 0, 0, 128, 255, 0, 0, 128];
        ScreenOverlayMaterial additive = ScreenOverlayMaterial.Error with
        {
            Name = "effects/synthetic_additive",
            Plain = new MapTexture(
                2, 2, 2, 2, TextureImage.Rgba(texels), IsTransparent: false, IsAdditive: true, IsTranslucent: translucent),
        };

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(0f, 0f, 0.5f);
        target.DrawScreenOverlay(additive, 0.0, underwater: true).ShouldBeTrue();

        (int measuredRed, _, int blue) = target.PixelAt(32, 32);

        measuredRed.ShouldBeInRange(red - 1, red);
        blue.ShouldBeInRange(187, 188);
    }
}
