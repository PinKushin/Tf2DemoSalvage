using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The frame's tone-map scale reaches the world pass whether or not the camera was sent again this frame (B636).
/// </summary>
/// <remarks>
/// **Valve:** <c>SetToneMappingScaleLinear</c> (shaderapidx9.dll 0x180023be0, B514) writes <c>LINEAR_LIGHT_SCALE</c> as
/// a shader constant when it is called, for whatever draws next: it is not part of the view. `DrawFrame` set only the
/// renderer's property, which the camera upload (<c>SetCamera</c>) copies into the constant buffer, so a frame that sent
/// no camera after setting it (no 3D sky, no water, no monitor) drew the world at the PREVIOUS upload's scale: 1 after
/// the HUD's reset (viewrender.cpp:2211-2216) when the camera had moved, the previous frame's exposure otherwise.
///
/// **The subject is synthetic** (D38): one lit grey quad, so the only thing that differs between the draws is the
/// scale. A Direct3D device is all it needs; without one the tests are ignored for that reason alone.
/// </remarks>
[NonParallelizable]
public sealed class FrameToneMapScaleConformanceTests
{
    private const float Half = 0.5f;

    /// <remarks>
    /// **The predicted pixel, from the shader's arithmetic:** <c>TONEMAP_SCALE_LINEAR</c> multiplies the linear result by
    /// <c>LINEAR_LIGHT_SCALE</c> (<c>common_ps_fxc.h:348-350</c>), and the target is sRGB, so the pixel at 0.5 is
    /// <c>srgb(0.5 · linear(p1))</c> of the pixel <c>p1</c> drawn at 1 — about 138 to about 100 for this quad.
    /// </remarks>
    [Test]
    public void RedrawWorld_AfterTheFrameScaleIsSetWithNoCameraUpload_DrawsAtTheFrameScale()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        // The previous frame: its camera sent at 1, the scale the HUD left it at.
        target.Clear(ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface);
        target.DrawWorld(Quad(), [new WorldBatch(ModulateToneMapConformanceTests.Lit, 0, 6)], ModulateToneMapConformanceTests.Camera(), ModulateToneMapConformanceTests.Assets, toneMapScale: 1f);

        (int, int Green, int) atOne = target.PixelAt(8, 8);

        // This frame: the scale set, the camera not sent again, the world drawn.
        target.SetToneMappingScaleLinear(Half);
        target.Clear(ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface);
        target.RedrawWorld();

        (int, int Green, int) redrawn = target.PixelAt(8, 8);
        int predicted = Scaled(atOne.Green, Half);
        string drew = $"drawn {atOne} at 1, then {redrawn} after the frame's scale {Half}; predicted green {predicted}";

        // The controls: the quad covers the 188 surface, and the prediction is far from the scale-1 value.
        atOne.Green.ShouldBeLessThan(188 - 30, drew);
        predicted.ShouldBeLessThan(atOne.Green - 20, drew);

        redrawn.Green.ShouldBeInRange(predicted - 2, predicted + 2, drew);
    }

    /// <remarks>
    /// **The HUD's reset is the same call** (viewrender.cpp:2211-2216): after the frame at 0.5, setting 1 puts the
    /// world's constants back at 1, so anything drawn on them after it is unexposed.
    /// </remarks>
    [Test]
    public void RedrawWorld_AfterTheHudsResetToOne_DrawsAtOne()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        target.Clear(ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface);
        target.DrawWorld(Quad(), [new WorldBatch(ModulateToneMapConformanceTests.Lit, 0, 6)], ModulateToneMapConformanceTests.Camera(), ModulateToneMapConformanceTests.Assets, toneMapScale: Half);

        (int, int Green, int) atHalf = target.PixelAt(8, 8);

        target.SetToneMappingScaleLinear(1f);
        target.Clear(ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface, ModulateToneMapConformanceTests.Surface);
        target.RedrawWorld();

        (int, int Green, int) redrawn = target.PixelAt(8, 8);
        string drew = $"drawn {atHalf} at {Half}, then {redrawn} after the reset to 1";

        // The control: the scale-1 pixel is far from the 0.5 one, so equal-to-0.5 cannot pass.
        Scaled(redrawn.Green, Half).ShouldBeInRange(atHalf.Green - 2, atHalf.Green + 2, drew);
        redrawn.Green.ShouldBeGreaterThan(atHalf.Green + 20, drew);
    }

    /// <remarks>
    /// **The capture log's figure is carried, not recomputed** (B243): the scale the constants hold, which a property
    /// assigned afterwards without an upload does not change.
    /// </remarks>
    [Test]
    public void UploadedLinearLightScale_AfterThePropertyChangesWithNoUpload_IsTheUploadedScale()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        target.DrawWorld(Quad(), [new WorldBatch(ModulateToneMapConformanceTests.Lit, 0, 6)], ModulateToneMapConformanceTests.Camera(), ModulateToneMapConformanceTests.Assets, toneMapScale: 1f);
        target.SetToneMappingScaleLinear(Half);

        target.WorldUploadedLinearLightScale.ShouldBe(Half, "the frame's scale reached the constants");

        // DrawWorld's own push and pop: the property moves to 0.25 and back to 0.5 with an upload at 0.25 between.
        target.DrawWorld(Quad(), [new WorldBatch(ModulateToneMapConformanceTests.Lit, 0, 6)], ModulateToneMapConformanceTests.Camera(), ModulateToneMapConformanceTests.Assets, toneMapScale: 0.25f);

        target.WorldLinearLightScale.ShouldBe(Half, "the control: the property was put back");
        target.WorldUploadedLinearLightScale.ShouldBe(0.25f, "the constants still hold the draw's upload");
    }

    /// <summary>Gamma pixel <paramref name="pixel"/> with its linear value scaled by <paramref name="scale"/>, as the sRGB target stores it.</summary>
    private static int Scaled(int pixel, float scale)
    {
        double linear = ToLinear(pixel / 255.0) * scale;

        return (int)Math.Round(ToGamma(linear) * 255.0);
    }

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double ToGamma(double l) => l <= 0.0031308 ? l * 12.92 : (1.055 * Math.Pow(l, 1.0 / 2.4)) - 0.055;

    private static List<WorldVertex> Quad() => ModulateToneMapConformanceTests.Quad(1f);
}
