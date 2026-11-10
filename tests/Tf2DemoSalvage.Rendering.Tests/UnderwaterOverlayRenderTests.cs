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
    /// **A translucent plain overlay blends by its alpha, `BT_BLEND`** (BaseShader.h): the error texture marked translucent
    /// lays its black squares, alpha 128, over the cleared white. The target stores sRGB and blends in linear, as every
    /// translucent draw on it does, so white keeps 1 − 128/255 = 0.498 of its light: 187.6 encoded, where the opaque control
    /// above reads 0.
    /// </remarks>
    [Test]
    public void DrawScreenOverlay_ATranslucentPlainOverlay_BlendsByItsAlpha()
    {
        ScreenOverlayMaterial translucent = ScreenOverlayMaterial.Error with
        {
            Name = "effects/synthetic_translucent",
            Plain = ScreenOverlayMaterial.Error.Plain!.Value with { IsTranslucent = true },
        };

        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(Size, Size), "no Direct3D on this machine");

        target.Clear(1f, 1f, 1f);
        target.DrawScreenOverlay(translucent, 0.0, underwater: true).ShouldBeTrue();

        (int red, _, _) = target.PixelAt(12, 4);

        red.ShouldBeInRange(187, 188);
    }
}
