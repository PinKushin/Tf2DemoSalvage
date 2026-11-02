using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The fog clear stores the engine's BYTES, read back raw from a real target (fog-open-items plan, item 1).
/// </summary>
/// <remarks>
/// **What Valve stores** (<c>shaderapidx9.dll</c>, read 2026-11-02): <c>ClearColor4ub</c> (<c>0x180014f90</c>) packs the
/// bytes with no conversion, and <c>ClearBuffers</c> (<c>0x180014bd0</c>) sets <c>D3DRS_SRGBWRITEENABLE</c> (194) to 0
/// around <c>IDirect3DDevice9::Clear</c> whenever the current shadow state has sRGB write on, restoring it afterwards
/// through <c>0x180031db0</c>. So the stored texel IS the byte, whatever the previous draw left. D3D11's
/// <c>ClearRenderTargetView</c> on an sRGB view instead takes LINEAR values and encodes them, so the clear must hand it the
/// decode of the byte. These tests clear the offscreen target's sRGB view (the window's <c>B8G8R8A8_UNORM_SRGB</c>; the
/// water targets are <c>R8G8B8A8_UNORM_SRGB</c>, the same curve) and read the raw texels, which are also what the B641
/// gamma twin reads.
/// </remarks>
public sealed class WaterClearConformanceTests
{
    /// <remarks>Every byte, unscaled: the stored texel is the byte. A clear handed byte/255 as if linear would store 0..255 brightened (128 stores 188).</remarks>
    [Test]
    public void Clear_EveryFogByteThroughWaterClearColour_StoresTheByteItself()
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(4, 4), "no Direct3D on this machine");

        for (int fogByte = 0; fogByte < 256; fogByte++)
        {
            float value = WorldRenderer.WaterClearColour(fogByte / 255f, 1f, scaled: false);

            target.Clear(value, value, value);
            target.PixelAt(1, 1).ShouldBe((fogByte, fogByte, fogByte), $"byte {fogByte}");
        }

        target.Clear(128f / 255f, 128f / 255f, 128f / 255f);
        target.PixelAt(1, 1).ShouldNotBe((128, 128, 128), "the control: the raw byte as a linear value is brightened");
    }

    /// <remarks>
    /// Scaled, at the reflection-free frame scale 0.6 and at 4 (integer HDR's overbright reach): the stored texel is
    /// <c>(int)( byte · pow( s, 1/2.2 ) ) &amp; 255</c> (<c>client.dll</c> <c>0x18034d560</c>). At 4, 200 stores 119.
    /// </remarks>
    [TestCase(200, 0.6f, 158)]
    [TestCase(51, 0.6f, 40)]
    [TestCase(200, 4f, 119)]
    public void Clear_AScaledFogByte_StoresSetClearColorToFogColorsByte(int fogByte, float scale, int stored)
    {
        using OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(4, 4), "no Direct3D on this machine");
        float value = WorldRenderer.WaterClearColour(fogByte / 255f, scale, scaled: true);

        target.Clear(value, value, value);

        target.PixelAt(1, 1).ShouldBe((stored, stored, stored));
    }
}
