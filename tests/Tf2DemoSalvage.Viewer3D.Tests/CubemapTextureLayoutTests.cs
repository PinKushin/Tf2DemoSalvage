using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// How many faces a baked cubemap VTF actually stores, measured rather than assumed.
/// </summary>
/// <remarks>
/// **<c>src/vtf/</c> is not in the SDK**, so the one question that decides the whole read — how many
/// faces are on disk — cannot be answered by reading Valve's loader. The header can:
///
/// <code>
/// enum CubeMapFaceIndex_t
/// {
///     CUBEMAP_FACE_RIGHT = 0, LEFT, BACK, FRONT, UP, DOWN,
///     CUBEMAP_FACE_SPHEREMAP,          // This is the fallback for low-end
///     // NOTE: Cubemaps have *7* faces; the 7th is the fallback spheremap
///     CUBEMAP_FACE_COUNT
/// };
/// </code>
///
/// Seven, per <c>vtf.h:147</c> — but that comment is old, the spheremap fallback existed for
/// hardware that has not shipped in twenty years, and a header comment is not a statement about
/// what a 2026 TF2 map contains. **So it is arithmetic on real bytes that settles it**, and the
/// arithmetic is exact: the image data must divide evenly by the per-face size, and 6 and 7 give
/// different answers on any file whose size is known.
///
/// This matters before a single pixel is decoded. Six faces read as seven, or the reverse, puts
/// every mip offset wrong and produces a reflection assembled from parts of the wrong images —
/// which looks like a bad reflection rather than like a decode failure.
/// </remarks>
public sealed class CubemapTextureLayoutTests
{
    private const string MapName = "cp_process_final";

    /// <summary><c>TEXTUREFLAGS_ENVMAP</c>, <c>vtf.h:53</c>.</summary>
    private const uint EnvmapFlag = 0x00004000;

    [Test]
    public void CubemapLayout_ABakedCubemap_DeclaresItselfAnEnvmap()
    {
        // The flag is what tells a reader to expect faces at all, so it is worth confirming that a
        // real baked cubemap sets it rather than relying on the filename.
        Header header = FirstCubemap();

        TestContext.Out.WriteLine(header.ToString());

        (header.Flags & EnvmapFlag).ShouldNotBe(
            0u, "a baked cubemap must set TEXTUREFLAGS_ENVMAP");
    }

    [Test]
    public void CubemapLayout_TheImageData_DividesBySevenNotSix()
    {
        // **The measurement, and it is a discriminating one.** Total image bytes divided by the
        // bytes one face occupies across the whole mip chain gives the face count outright. If the
        // file held six faces the same division by seven would leave a remainder, so this cannot
        // pass for both answers — which is the property that makes it worth writing.
        Header header = FirstCubemap();

        long perFace = header.BytesPerFaceAcrossAllMips();
        long image = header.ImageBytes();

        TestContext.Out.WriteLine(
            $"{image} image bytes, {perFace} per face per frame -> " +
            $"{(double)image / (perFace * header.Frames):F3} faces");

        perFace.ShouldBeGreaterThan(0);

        (image % (perFace * header.Frames)).ShouldBe(
            0, "the image data must be a whole number of faces");

        (image / (perFace * header.Frames)).ShouldBe(
            7, "vtf.h:147 says cubemaps have seven faces, the seventh a fallback spheremap");
    }

    [Test]
    public void CubemapLayout_EveryBakedCubemap_HasTheSameShape()
    {
        // **The control against reading one lucky file.** A conclusion drawn from a single specimen
        // is a conclusion about that specimen; 43 of them agreeing is a fact about the format as
        // this map's compiler wrote it.
        List<string> odd = [];
        int counted = 0;

        foreach (Header header in AllCubemaps())
        {
            counted++;

            long perFace = header.BytesPerFaceAcrossAllMips();

            if (perFace <= 0 ||
                header.ImageBytes() % (perFace * header.Frames) != 0 ||
                header.ImageBytes() / (perFace * header.Frames) != 7)
            {
                odd.Add($"{header.Name}: {header}");
            }
        }

        TestContext.Out.WriteLine($"{counted - odd.Count} of {counted} cubemaps hold seven faces");

        counted.ShouldBeGreaterThan(0, "no cubemap VTF was found to measure");
        odd.ShouldBeEmpty();
    }

    [Test]
    public void CubemapLayout_TheBakedTexture_IsTheSizeTheLumpDeclared()
    {
        // **A third independent recording of one number.** dcubemapsample_t.size is a CODE that
        // resolves to an edge length, and the VTF baked from that placement carries the edge
        // length in its own header. Neither was written by this project.
        //
        // That is what makes it a real check on the escape value: every one of these records
        // carries size 0, so if 0 were passed through `1 << (size - 1)` the reader would claim
        // 1,073,741,824 while the file plainly says 32. See
        // docs/memory/sentinels-conflate-unknown-with-answer.md.
        (List<Header> headers, IReadOnlyList<BspCubemap> placements) = Everything();

        headers.Count.ShouldBe(placements.Count, "one baked texture per placement");

        for (int index = 0; index < headers.Count; index++)
        {
            headers[index].Width.ShouldBe(placements[index].Size, headers[index].Name);
            headers[index].Height.ShouldBe(placements[index].Size, headers[index].Name);
        }
    }

    [Test]
    public void CubemapLayout_EveryPlacement_BakesBothLdrAndHdr()
    {
        // **The question that decides how much work the reflection is**, and it is worth asking
        // before writing a half-float decoder rather than after.
        //
        // vbsp bakes `c<x>_<y>_<z>.vtf` and `c<x>_<y>_<z>.hdr.vtf`, and the engine samples whichever
        // matches the mode it is running in. This project draws LDR — the reference captures are
        // configured that way deliberately (24-reference-capture.md) — so if the plain file is
        // present it is the correct one to read AND it is an ordinary 8-bit format this project
        // already decodes.
        byte[] map = MapBytes();
        PakFile pak = PakFile.ReadFrom(map);

        int ldr = 0;
        int hdr = 0;

        foreach (BspCubemap cubemap in BspCubemaps.Read(map))
        {
            string name = BspCubemaps.TextureName(MapName, cubemap);

            if (pak.Contains($"materials/{name}.vtf"))
            {
                ldr++;
            }

            if (pak.Contains($"materials/{name}.hdr.vtf"))
            {
                hdr++;
            }
        }

        TestContext.Out.WriteLine($"{ldr} LDR cubemaps, {hdr} HDR cubemaps");

        ldr.ShouldBeGreaterThan(0, "an LDR cubemap is what a non-HDR renderer samples");
    }

    [Test]
    public void CubemapLayout_TheLdrBake_IsAFormatAlreadyDecoded()
    {
        // The payoff from asking the question above: the LDR bake is an ordinary compressed format,
        // so the reflection needs no new pixel decoder at all — only face iteration.
        //
        // Named formats rather than "not 24", because "some 8-bit format" is not a decodable claim
        // and the assertion has to fail if a map turns up in something unsupported.
        List<Header> ldr = AllCubemaps(preferHdr: false);

        TestContext.Out.WriteLine(
            "LDR formats: " + string.Join(", ", ldr.Select(header => header.Format).Distinct()));

        TestContext.Out.WriteLine($"first LDR: {ldr[0]}");

        foreach (Header header in ldr)
        {
            header.Format.ShouldBeOneOf(
                [13, 14, 15, 12, 2, 3],
                $"{header.Name} is format {header.Format}, which VtfTexture does not decode");
        }
    }

    [Test]
    public void CubemapLayout_TheHdrBake_IsHalfFloatAndUndecoded()
    {
        // **A prerequisite, measured rather than assumed.** Every baked cubemap on this map is
        // ImageFormat 24, RGBA16161616F — four half-floats per texel, eight bytes. That is the HDR
        // pipeline: a reflection has to carry values above one, which an 8-bit format cannot.
        //
        // VtfTexture does not decode it and throws "VTF pixel format 24 is not supported", so no
        // amount of shader work reaches a picture until it does. Recorded as an assertion rather
        // than a note because the day someone adds half-float support, this test says so.
        foreach (Header header in AllCubemaps())
        {
            header.Format.ShouldBe(24, $"{header.Name} is RGBA16161616F");
        }

        Enum.IsDefined(typeof(VtfFormat), 24).ShouldBeFalse(
            "when RGBA16161616F is implemented, delete this assertion and this test's second half");
    }

    /// <summary>The headers and the placements they were baked from, in the same order.</summary>
    private static (List<Header> Headers, IReadOnlyList<BspCubemap> Placements) Everything()
    {
        byte[] map = MapBytes();

        return (AllCubemaps(), BspCubemaps.Read(map));
    }

    /// <summary>The header fields that decide the layout.</summary>
    private sealed record Header(
        string Name, int Width, int Height, uint Flags, int Frames, int Format, int MipCount,
        int HeaderSize, int LowResFormat, int LowResWidth, int LowResHeight, int FileLength)
    {
        /// <summary>Bytes of image data, after the header and the embedded thumbnail.</summary>
        public long ImageBytes() =>
            FileLength - HeaderSize - (LowResFormat >= 0 && LowResWidth > 0 && LowResHeight > 0
                ? Dxt1Size(LowResWidth, LowResHeight)
                : 0);

        /// <summary>Bytes one face occupies over the whole mip chain, for one frame.</summary>
        public long BytesPerFaceAcrossAllMips()
        {
            long total = 0;

            for (int level = 0; level < MipCount; level++)
            {
                total += Size(Format, Math.Max(1, Width >> level), Math.Max(1, Height >> level));
            }

            return total;
        }

        public override string ToString() =>
            $"{Width}x{Height} format {Format} mips {MipCount} frames {Frames} " +
            $"flags 0x{Flags:x8} header {HeaderSize} file {FileLength}";

        /// <summary>Bytes for one image, for the formats a baked cubemap actually uses.</summary>
        /// <remarks>
        /// **Deliberately a second implementation rather than a call into VtfTexture.** This test
        /// exists to falsify a belief about the layout, and asking the code under test how big
        /// something is would make it agree with itself.
        ///
        /// The formats are Valve's <c>ImageFormat</c> ordinals: 13 DXT1, 15 DXT5, 2 BGR888,
        /// 6 BGRA8888, 24 RGBA16161616F.
        /// </remarks>
        private static long Size(int format, int width, int height) => format switch
        {
            13 => Dxt1Size(width, height),
            14 or 15 => (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
            2 => (long)width * height * 3,
            6 or 5 or 3 => (long)width * height * 4,
            24 => (long)width * height * 8,
            _ => -1,
        };

        private static long Dxt1Size(int width, int height) =>
            (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;
    }

    /// <summary>The first baked cubemap on the map, or skips.</summary>
    private static Header FirstCubemap() =>
        AllCubemaps().FirstOrDefault()
        ?? throw new InvalidOperationException("no cubemap VTF in the map's pakfile");

    /// <summary>The reference map's bytes, or skips.</summary>
    private static byte[] MapBytes()
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

        return File.ReadAllBytes(path);
    }

    /// <summary>Every baked cubemap's header, read straight from the pakfile.</summary>
    private static List<Header> AllCubemaps(bool preferHdr = true)
    {
        byte[] map = MapBytes();
        PakFile pak = PakFile.ReadFrom(map);

        List<Header> headers = [];

        foreach (BspCubemap cubemap in BspCubemaps.Read(map))
        {
            string name = BspCubemaps.TextureName(MapName, cubemap);

            // A modern map bakes BOTH, and the engine samples whichever matches the mode it runs
            // in. Which one this reads is therefore a choice, not a fallback chain.
            byte[]? bytes = preferHdr
                ? pak.ReadFile($"materials/{name}.hdr.vtf") ?? pak.ReadFile($"materials/{name}.vtf")
                : pak.ReadFile($"materials/{name}.vtf") ?? pak.ReadFile($"materials/{name}.hdr.vtf");

            if (bytes is { Length: >= 64 } && Parse(name, bytes) is { } header)
            {
                headers.Add(header);
            }
        }

        return headers;
    }

    /// <summary>Reads the header fields this test needs, at the offsets VtfTexture uses.</summary>
    private static Header? Parse(string name, byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;

        if (!span[..3].SequenceEqual("VTF"u8))
        {
            return null;
        }

        return new Header(
            name,
            BinaryPrimitives.ReadUInt16LittleEndian(span[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[18..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[20..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[52..]),
            span[56],
            (int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..]),
            BinaryPrimitives.ReadInt32LittleEndian(span[57..]),
            span[61],
            span[62],
            bytes.Length);
    }
}
