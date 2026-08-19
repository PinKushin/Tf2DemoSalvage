using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Decoding the faces of cubemaps a real compiler baked.
/// </summary>
/// <remarks>
/// **<c>VtfCubeFaceTests</c> cannot falsify the layout**, because its fixtures are written from the
/// same belief as the reader — the trap that has now cost this project three bugs in one session
/// (<c>docs/memory/put-the-real-file-in-the-fixture.md</c>). It pins face SELECTION, which is what a
/// synthetic fixture is good for. This pins the layout, against 43 files vbsp wrote.
///
/// **The decisive assertion is that the last face ends exactly at the end of the file.** Any error
/// in the mip stride, the face stride, or the face count moves that boundary — and unlike a picture,
/// it is exact. A reflection assembled from the wrong offsets still looks like a reflection.
/// </remarks>
public sealed class CubemapFaceDecodeTests
{
    private const string MapName = "cp_process_final";

    [Test]
    public void ABakedCubemapReportsSevenFaces()
    {
        VtfTexture texture = VtfTexture.Decode(FirstCubemap());

        texture.IsCubeMap.ShouldBeTrue("a baked cubemap sets TEXTUREFLAGS_ENVMAP");
        texture.FaceCount.ShouldBe(7);
    }

    [Test]
    public void EveryFaceOfEveryBakedCubemapDecodes()
    {
        // A face that lands outside the file throws, so this is a real bounds check across 43 files
        // and 301 faces rather than a smoke test — and it is the cheapest way to catch a stride
        // that is right for the first face and wrong for the rest.
        int decoded = 0;

        foreach (byte[] file in AllCubemaps())
        {
            for (int face = 0; face < VtfTexture.CubeFaceCount; face++)
            {
                VtfTexture texture = VtfTexture.Decode(file, face: face);

                texture.Width.ShouldBe(32);
                texture.Pixels.Length.ShouldBe(32 * 32 * 4);
                decoded++;
            }
        }

        TestContext.Out.WriteLine($"{decoded} faces decoded across {AllCubemaps().Count} cubemaps");

        decoded.ShouldBe(43 * 7);
    }

    [Test]
    public void TheReadersLastFaceReachesTheLastByteOfTheFile()
    {
        // **The one that is sensitive to the reader's own arithmetic**, and the reason the test
        // below it is not enough.
        //
        // Dropping `* faces` from the mip skip — the single likeliest mistake here, and the one this
        // reader originally had — moves every offset 1,104 bytes earlier on a 32x32 cubemap. The
        // data still lies inside the file, still decodes, and still yields six different-looking
        // images, so EVERY other test in this file passes against it. Measured, not supposed: the
        // sabotage was applied and all five went green.
        //
        // Removing one byte from the end changes that. If the reader's last face genuinely ends at
        // the last byte, it can no longer be read; if its offsets are short, the truncated file
        // still satisfies it. So the pair below — succeeds whole, throws truncated — pins the
        // boundary through the code rather than beside it.
        byte[] file = FirstCubemap();

        Should.NotThrow(() => VtfTexture.Decode(file, face: 6), "the whole file holds seven faces");

        byte[] truncated = file[..^1];

        Should.Throw<InvalidDataException>(
            () => VtfTexture.Decode(truncated, face: 6),
            "the seventh face ends at the last byte, so one byte short must not decode");
    }

    [Test]
    public void TheFormatItselfPutsTheSeventhFaceAtTheEndOfTheFile()
    {
        // The same boundary computed from the header, independently of the reader. This checks the
        // UNDERSTANDING of the format rather than the implementation of it — worth having, and not
        // a substitute for the test above, which is the distinction that was missed the first time
        // this file was written.
        foreach (byte[] file in AllCubemaps())
        {
            (int headerSize, int width, int height, int mips) = HeaderOf(file);

            long at = headerSize;

            // Every mip below 0, seven faces each.
            for (int level = mips - 1; level > 0; level--)
            {
                at += Dxt1(Math.Max(1, width >> level), Math.Max(1, height >> level)) * 7;
            }

            // Faces 0 to 5 of mip 0, then face 6.
            long faceBytes = Dxt1(width, height);

            (at + (faceBytes * 7)).ShouldBe(file.Length, "the seventh face ends the file");
        }
    }

    [Test]
    public void TheSixCubeFacesAreNotAllTheSameImage()
    {
        // **The control against a reader that ignores the face argument entirely.** Returning face 0
        // for every request passes every bounds check and every size assertion above.
        //
        // A room reflects differently in each direction, so the six real faces of a baked cubemap
        // must differ. Asserted as "not all identical" rather than "all distinct": a cubemap in a
        // symmetrical corridor can legitimately have two matching faces, and demanding six unique
        // ones would fail on correct data.
        byte[] file = FirstCubemap();

        List<string> fingerprints = [];

        for (int face = 0; face < 6; face++)
        {
            fingerprints.Add(Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(VtfTexture.Decode(file, face: face).Pixels)));
        }

        TestContext.Out.WriteLine(
            $"{fingerprints.Distinct().Count()} distinct images across the six cube faces");

        fingerprints.Distinct().Count().ShouldBeGreaterThan(
            1, "a room does not reflect the same image in all six directions");
    }

    [Test]
    public void TheSpheremapFaceIsNotOneOfTheSix()
    {
        // Face 6 is a fallback spheremap, a different projection of the same room rather than a
        // seventh direction. It has to be dropped before upload, and this says it is a distinct
        // image — so a reader that quietly returned face 5 for it, or that treated seven faces as
        // six plus a duplicate, is caught.
        byte[] file = FirstCubemap();

        string spheremap = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(VtfTexture.Decode(file, face: 6).Pixels));

        for (int face = 0; face < 6; face++)
        {
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(VtfTexture.Decode(file, face: face).Pixels))
                .ShouldNotBe(spheremap, $"face {face} must not be the spheremap");
        }
    }

    private static long Dxt1(int width, int height) =>
        (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;

    /// <summary>Header fields, read independently of VtfTexture.</summary>
    private static (int HeaderSize, int Width, int Height, int Mips) HeaderOf(byte[] file) =>
        ((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12)),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(16)),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(18)),
            file[56]);

    private static byte[] FirstCubemap() => AllCubemaps()[0];

    /// <summary>Every LDR baked cubemap in the map's pakfile, or skips.</summary>
    /// <remarks>
    /// **LDR deliberately**, not a fallback: vbsp bakes both, this renderer draws LDR, and the LDR
    /// bake is DXT1 — a format this project already decodes. Reading the HDR one instead is what
    /// produced a half-float "prerequisite" that did not exist.
    /// </remarks>
    private static List<byte[]> AllCubemaps()
    {
        if (Tf2Install.Folder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");

            throw new InvalidOperationException("unreachable; Assert.Ignore throws");
        }

        string path = Path.Combine(game, "maps", MapName + ".bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{MapName} is not installed.");
        }

        byte[] map = File.ReadAllBytes(path);
        PakFile pak = PakFile.ReadFrom(map);

        List<byte[]> files = [];

        foreach (BspCubemap cubemap in BspCubemaps.Read(map))
        {
            if (pak.ReadFile($"materials/{BspCubemaps.TextureName(MapName, cubemap)}.vtf") is { } bytes)
            {
                files.Add(bytes);
            }
        }

        return files;
    }
}
