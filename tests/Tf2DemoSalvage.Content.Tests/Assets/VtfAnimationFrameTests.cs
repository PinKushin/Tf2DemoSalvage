using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading a VTF's animation frames, against a texture TF2 ships (B338).
/// </summary>
/// <remarks>
/// **The reader has always known the frame count and always read frame zero.** The comment it
/// carried said so in as many words — *"Within one mip: frame, then face. Frame zero is the only
/// one this reads, so the frame term is zero"* — and 7,027 shipped materials run `AnimatedTexture`
/// over a texture that has more.
///
/// **Measured on the file that matters**: `effects/tiledfire/fireLayeredSlowTiled512.vtf` is
/// 64x64 DXT1 with **121 frames**, and 6,735 of those 7,027 materials animate that one file
/// through `$detail`/`$detailframe`. It is TF2's fire overlay — what a burning player is covered
/// in.
///
/// **A real file rather than a hand-built one, deliberately, and this is the exception D38 allows.**
/// The question is whether the offset arithmetic lands on the frame Valve's own writer put there;
/// a fixture would be this project's arithmetic checked against this project's arithmetic. The
/// control below is what makes it a test rather than a measurement: the frames must DIFFER, which
/// a reader still returning frame zero cannot satisfy.
/// </remarks>
public sealed class VtfAnimationFrameTests
{
    private const string Fire = "materials/effects/tiledfire/fireLayeredSlowTiled512.vtf";

    [Test]
    public void FrameCount_TF2sFireOverlay_IsMoreThanOne()
    {
        if (Read(Fire) is not { } bytes)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VtfTexture.Decode(bytes).FrameCount.ShouldBeGreaterThan(
            1, "this is an animated sheet and a reader reporting 1 has not read numFrames");
    }

    /// <remarks>
    /// **The control, and the whole test.** A reader whose frame offset is wrong — or still zero —
    /// returns the same image for every frame, and every assertion about frame counts still passes.
    /// Distinct pixels are the only thing that says the offset moved.
    /// </remarks>
    [Test]
    public void Decode_TwoDifferentFrames_ReturnDifferentPixels()
    {
        if (Read(Fire) is not { } bytes)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        byte[] first = VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: 0).Pixels;
        byte[] later = VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: 8).Pixels;

        first.Length.ShouldBe(later.Length, "the same mip of the same file is the same size");

        first.ShouldNotBe(
            later,
            "frames 0 and 8 of a fire sheet are different pictures; identical bytes mean the frame "
            + "term is still zero");
    }

    /// <remarks>
    /// **A frame past the end wraps rather than reading off the file**, which is the proxy's own
    /// arithmetic: `int intFrame = ((int)frame) % numFrames;`
    /// (`baseanimatedtextureproxy.cpp:110`). It matters because the frame comes from a CLOCK — the
    /// proxy multiplies elapsed time by a frame rate — so nothing bounds it before it arrives.
    /// </remarks>
    [Test]
    public void Decode_AFramePastTheEnd_WrapsAsTheProxyWrapsIt()
    {
        if (Read(Fire) is not { } bytes)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        int frames = VtfTexture.Decode(bytes).FrameCount;

        VtfTexture wrapped = VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: frames + 3);

        wrapped.Frame.ShouldBe(3);

        wrapped.Pixels.ShouldBe(
            VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: 3).Pixels,
            "wrapping must land on the same image, not merely on a valid one");
    }

    /// <remarks>
    /// **A still texture is unaffected**, which is the control on every other texture in the game:
    /// asking for frame 0 of a one-frame file must behave exactly as it did before frames existed.
    /// </remarks>
    [Test]
    public void Decode_AStillTexture_IsUnchangedByTheFrameParameter()
    {
        if (Read("materials/models/player/soldier/soldier_red.vtf") is not { } bytes)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VtfTexture image = VtfTexture.Decode(bytes);

        image.FrameCount.ShouldBe(1, "a player's skin is not a sheet");
        image.Frame.ShouldBe(0);

        // And a caller asking for a frame it does not have gets frame 0 rather than an exception.
        VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: 5).Frame.ShouldBe(0);
    }

    /// <summary>The file's bytes, or null when the game is not installed.</summary>
    /// <remarks>
    /// **Both archives, because a TEXTURE is not in `tf2_misc`.** The first version of this looked
    /// only there, and all four tests skipped with "Team Fortress 2 is not installed" on a machine
    /// where it is — a silent skip wearing the shape of an honest one, which is the fault
    /// `docs/memory/read-the-trx-total-not-the-console.md` is about. Caught by reading the run
    /// rather than its exit code.
    ///
    /// A statement body rather than a conditional expression, because `byte[]` converts to
    /// `ReadOnlyMemory&lt;byte&gt;` and a null arm would arrive as `Empty` rather than as null —
    /// B333, which cost four days of red CI.
    /// </remarks>
    private static ReadOnlyMemory<byte>? Read(string path)
    {
        foreach (string name in new[] { "tf2_textures", "tf2_misc" })
        {
            if (GameInstall.Vpk(name) is { } archive &&
                VpkArchive.Open(archive).ReadFile(path) is { } bytes)
            {
                return bytes;
            }
        }

        return null;
    }
}
