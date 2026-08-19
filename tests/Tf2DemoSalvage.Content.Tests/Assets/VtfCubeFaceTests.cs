using System;
using System.Buffers.Binary;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Selecting one face of a cubemap VTF.
/// </summary>
/// <remarks>
/// **The layout, measured rather than read** — <c>src/vtf/</c> is not in the SDK. Storage runs
/// smallest mip first, and within each mip: every frame, and within each frame every face:
///
/// <code>
/// for mip = mipCount-1 .. 0        // smallest first
///     for frame = 0 .. frames-1
///         for face = 0 .. faces-1
/// </code>
///
/// **Seven faces, not six**, when <c>TEXTUREFLAGS_ENVMAP</c> is set — the seventh is a fallback
/// spheremap for hardware that has not shipped in twenty years (<c>vtf.h:147</c>). Confirmed by
/// exact division on all 43 baked cubemaps of cp_process_final, where six would leave a remainder.
///
/// **These fixtures are synthetic and cannot falsify the layout**, because they are built from the
/// same belief as the reader. They pin face SELECTION — that asking for face 3 returns face 3's
/// pixels and not face 2's — which is what a synthetic fixture is good for. The layout itself is
/// tested against real baked cubemaps in <c>CubemapFaceDecodeTests</c>, where the last face has to
/// end exactly at the end of the file. See
/// <c>docs/memory/put-the-real-file-in-the-fixture.md</c>.
/// </remarks>
public sealed class VtfCubeFaceTests
{
    /// <summary><c>TEXTUREFLAGS_ENVMAP</c>, <c>vtf.h:53</c>.</summary>
    private const uint EnvmapFlag = 0x00004000;

    private const int HeaderSize = 64;

    [Test]
    public void VtfCubeFaces_AFlatTexture_ReportsOneFace()
    {
        // The control for everything below: a texture without the flag is not a cubemap, and asking
        // it for face 0 must behave exactly as it always has.
        VtfTexture texture = VtfTexture.Decode(Bgra(4, 4, faces: 1, flags: 0));

        texture.FaceCount.ShouldBe(1);
        texture.IsCubeMap.ShouldBeFalse();
    }

    [Test]
    public void VtfCubeFaces_AnEnvmap_ReportsSevenFaces()
    {
        VtfTexture texture = VtfTexture.Decode(Bgra(4, 4, faces: 7, flags: EnvmapFlag));

        texture.FaceCount.ShouldBe(7);
        texture.IsCubeMap.ShouldBeTrue();
    }

    [Test]
    public void VtfCubeFaces_EachFace_DecodesToItsOwnPixels()
    {
        // **The selection, and why every face gets a distinct value.** A reader that ignored the
        // face argument, or applied it with the wrong stride, would return face 0 for everything —
        // which with identical faces is indistinguishable from working.
        //
        // Face n is filled with the byte (n + 1) * 16, so the value names the face it came from.
        ReadOnlyMemory<byte> file = Bgra(4, 4, faces: 7, flags: EnvmapFlag);

        for (int face = 0; face < 7; face++)
        {
            byte expected = (byte)((face + 1) * 16);

            VtfTexture texture = VtfTexture.Decode(file, face: face);

            texture.Pixels[0].ShouldBe(expected, $"face {face}");
            texture.Pixels[^1].ShouldBe(expected, $"face {face}, last byte");
        }
    }

    [Test]
    public void VtfCubeFaces_TheDefaultFace_IsTheFirst()
    {
        // Every existing caller passes no face, and a cubemap reaching one of those must not
        // silently change which image it gets.
        VtfTexture.Decode(Bgra(4, 4, faces: 7, flags: EnvmapFlag)).Pixels[0].ShouldBe((byte)16);
    }

    [Test]
    public void VtfCubeFaces_AFaceOutsideTheRange_IsRejected()
    {
        // Not clamped. A caller asking for face 7 of a seven-face texture has an off-by-one, and
        // returning face 6 hides it behind a picture that is merely wrong.
        Should.Throw<ArgumentOutOfRangeException>(
            () => VtfTexture.Decode(Bgra(4, 4, faces: 7, flags: EnvmapFlag), face: 7));

        Should.Throw<ArgumentOutOfRangeException>(
            () => VtfTexture.Decode(Bgra(4, 4, faces: 7, flags: EnvmapFlag), face: -1));
    }

    [Test]
    public void VtfCubeFaces_AFaceBeyondTheFirstOnAFlatTexture_IsRejected()
    {
        // A flat texture has one face, so anything past zero is a caller bug rather than a format
        // question — and the same check catches it.
        Should.Throw<ArgumentOutOfRangeException>(
            () => VtfTexture.Decode(Bgra(4, 4, faces: 1, flags: 0), face: 1));
    }

    [Test]
    public void VtfCubeFaces_MipsBelowTheChosenOne_AreSkippedForEveryFace()
    {
        // **The stride that a one-mip fixture cannot test.** Mip data is stored smallest first, and
        // each level below the wanted one costs frames x FACES, not frames. A reader multiplying by
        // frames alone lands six faces early on any cubemap with a mip chain — which on a real
        // 32x32 cubemap is a 5,220-byte error and a picture assembled from the wrong images.
        //
        // Two mips, so the skip is exercised; face 5 of mip 0, so both the mip stride and the face
        // stride have to be right for the answer to come out.
        VtfTexture texture = VtfTexture.Decode(
            Bgra(4, 4, faces: 7, flags: EnvmapFlag, mips: 2), face: 5);

        texture.Width.ShouldBe(4);
        texture.Pixels[0].ShouldBe((byte)96, "face 5 of the full-size mip");
    }

    /// <summary>
    /// A BGRA8888 VTF whose face <c>n</c> at every mip is filled with the byte <c>(n + 1) * 16</c>.
    /// </summary>
    private static ReadOnlyMemory<byte> Bgra(
        int width, int height, int faces, uint flags, int mips = 1)
    {
        int image = 0;

        for (int level = 0; level < mips; level++)
        {
            image += Math.Max(1, width >> level) * Math.Max(1, height >> level) * 4;
        }

        byte[] file = new byte[HeaderSize + (image * faces)];

        "VTF\0"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24), 1);

        // 12 is IMAGE_FORMAT_BGRA8888.
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(52), 12);
        file[56] = (byte)mips;

        // No thumbnail: format -1 with zero dimensions.
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(57), -1);

        int at = HeaderSize;

        // **Smallest mip first**, which is the order the file uses and the order this fixture must
        // therefore write.
        for (int level = mips - 1; level >= 0; level--)
        {
            int bytes = Math.Max(1, width >> level) * Math.Max(1, height >> level) * 4;

            for (int face = 0; face < faces; face++)
            {
                file.AsSpan(at, bytes).Fill((byte)((face + 1) * 16));
                at += bytes;
            }
        }

        return file;
    }
}
