using Silk.NET.DXGI;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What the GPU is TOLD about a block-compressed texture, which no byte-level test can reach.
/// </summary>
/// <remarks>
/// **This exists because of a question the owner asked about the tests around it:** *"im making
/// sure we are not creating a test that doesnt actually check anything in a round about way."*
///
/// The answer was partly yes, and this closes the part that was hollow. Expanding blocks on the CPU
/// and asserting on the pixels checks that the bytes handed over are the **right bytes** — correct
/// offset, correct mip, correct face, correct size. That is the half that actually changes.
///
/// **It cannot check the description.** Hand Direct3D a DXT1 image while calling it `BC3_UNORM`, or
/// give it a pitch measured in pixels rather than blocks, and the bytes are still perfectly correct —
/// only the sentence describing them is wrong. Nothing decoded on the CPU would notice, because the
/// CPU decode never sees the description. The result is a skewed or wrongly-coloured image, which is
/// a picture rather than an error.
///
/// **And a readback test cannot cover it either, because CI has no GPU** — *"no it has to be cpu,
/// the ci has no gpu"*. So the description is pulled out into two pure functions and checked as
/// arithmetic, which runs anywhere.
///
/// What is left genuinely uncovered, and is stated here rather than implied: whether the GPU's own
/// interpretation of a correctly-described BC block matches Valve's. That is fixed by the S3TC
/// specification and by the hardware, and the only instrument for it is looking at the screen.
/// </remarks>
public sealed class BlockUploadDescriptionTests
{
    [Test]
    public void BlockFormat_EachDxtFormat_MapsToItsOwnBcFormat()
    {
        // **DXT1 is BC1, DXT3 is BC2, DXT5 is BC3** — the same bits under two names. Getting the
        // pairing wrong is the failure this test exists for: BC1 is 8 bytes a block and BC2/BC3 are
        // 16, so a mismatch reads the image at the wrong stride and produces a smear.
        WorldRenderer.BlockFormat(VtfFormat.Dxt1, srgb: true).ShouldBe(Format.FormatBC1UnormSrgb);
        WorldRenderer.BlockFormat(VtfFormat.Dxt3, srgb: true).ShouldBe(Format.FormatBC2UnormSrgb);
        WorldRenderer.BlockFormat(VtfFormat.Dxt5, srgb: true).ShouldBe(Format.FormatBC3UnormSrgb);
    }

    [Test]
    public void BlockFormat_Dxt1WithOneBitAlpha_IsStillBc1()
    {
        // Valve's `IMAGE_FORMAT_DXT1_ONEBITALPHA` is a statement about how the block's endpoints are
        // ordered, not a different layout — it is still eight bytes and still BC1. Treating it as
        // BC3 doubles the assumed stride.
        WorldRenderer.BlockFormat(VtfFormat.Dxt1OneBitAlpha, srgb: true)
            .ShouldBe(Format.FormatBC1UnormSrgb);
    }

    [Test]
    public void BlockFormat_WithoutSrgb_IsTheLinearVariant()
    {
        // **The one that fails silently and uniformly.** A colour texture uploaded as `BC1_UNORM`
        // rather than `BC1_UNORM_SRGB` samples too bright everywhere at once, which reads as a
        // lighting decision rather than a mistake — and a cubemap uploaded as sRGB darkens every
        // reflection by the gamma curve, which reads the same way in the other direction.
        WorldRenderer.BlockFormat(VtfFormat.Dxt1, srgb: false).ShouldBe(Format.FormatBC1Unorm);
        WorldRenderer.BlockFormat(VtfFormat.Dxt5, srgb: false).ShouldBe(Format.FormatBC3Unorm);
    }

    [Test]
    public void BlockPitch_IsMeasuredInBlocksRatherThanPixels()
    {
        // A 256-wide BC1 texture is 64 blocks across at 8 bytes each: 512 bytes a row. The pixel
        // answer — 256 * 4 — is 1024, and handing that to Direct3D reads every other row.
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 256, level: 0).ShouldBe(512);
        WorldRenderer.BlockPitch(VtfFormat.Dxt5, width: 256, level: 0).ShouldBe(1024);
    }

    [Test]
    public void BlockPitch_ADeeperMip_HalvesWithTheLevel()
    {
        // Level 1 of a 256-wide texture is 128 wide: 32 blocks, 256 bytes. A chain uploaded with the
        // top level's pitch throughout skews every mip but the first, which only shows at distance.
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 256, level: 1).ShouldBe(256);
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 256, level: 2).ShouldBe(128);
    }

    [Test]
    public void BlockPitch_TheSmallestMips_StayOneBlockWide()
    {
        // **A 4x4, 2x2 and 1x1 level are all one block.** Halving past a block and trusting the
        // arithmetic gives a pitch of zero, and the tail of every chain is exactly these levels.
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 4, level: 0).ShouldBe(8);
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 4, level: 1).ShouldBe(8);
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 4, level: 2).ShouldBe(8);
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 1, level: 0).ShouldBe(8);
    }

    [Test]
    public void BlockPitch_AWidthThatIsNotAWholeBlock_RoundsUp()
    {
        // 5 pixels across is 2 blocks, not 1. Truncating loses the last column of every row.
        WorldRenderer.BlockPitch(VtfFormat.Dxt1, width: 5, level: 0).ShouldBe(16);
    }
}
