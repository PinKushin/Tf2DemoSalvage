using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Map content this project never looks at, specified before it is built.
/// </summary>
/// <remarks>
/// **Fourth batch, and it is deliberately over-specified.** The owner's instruction is that too much
/// specification beats too little, because a gap written down can be dismissed on purpose while a
/// gap that is not written down comes back as a bug. The one clean exclusion is physics and movement
/// simulation — decoding gives the run as recorded, so nothing here needs to reproduce it.
///
/// Everything below is a lump or an entity the engine reads and this project does not open at all.
/// That is a different class from the earlier batches: those were parameters we decode and ignore,
/// these are bytes we never touch.
/// </remarks>
public sealed class UnimplementedContentConformanceTests
{
    /// <summary>Where the engine declares the game lumps.</summary>
    private const string GameBsp = "src/public/gamebspfile.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Content_DetailProps_AreASecondPropSystemInTheSameLump()
    {
        // **`dprp`, a sibling of `sprp` that this project does not open.** gamebspfile.h:26 declares
        // GAMELUMP_DETAIL_PROPS alongside GAMELUMP_STATIC_PROPS at line 28, both inside LUMP_GAME —
        // so the reader already walks past this one to reach the props it does read.
        //
        // Detail props are the grass, pebbles and small clutter scattered across displacements. They
        // are stored as DetailObjectLump_t (line 87) with their own lighting lumps, `dplt` and
        // `dplh` for LDR and HDR (lines 27 and 29).
        //
        // Absent, a map's ground is bare where the author placed foliage. That is not obviously
        // wrong to anyone who has not played the map, which is what makes it worth writing down.
        LumpNames().ShouldContain("GAMELUMP_DETAIL_PROPS");

        Assert.Ignore(
            "the dprp game lump is never opened, so detail props — grass and ground clutter — are " +
            "absent. BspStaticProps already walks LUMP_GAME to find sprp and steps over this.");
    }

    [Test]
    public void Content_CubemapSamples_ArePositionedAndSized()
    {
        // **This skipped saying "LUMP_CUBEMAPS is not read" long after it was read**, which is the
        // same drift `ConformanceGapAuditTests` was built to police on the viewer side. The skip is
        // gone and the claims it was making are now assertions.
        //
        // dcubemapsample_t (bspfile.h:992) is an integer position and a size byte, where
        // "0 - default, otherwise 1<<(size-1)". The compiled .vtf faces live in the map's own
        // pakfile and the filename is derived from the position — the comment beside the field says
        // so outright: "the filename for the vtf file is derived from the position".
        BspLumpIndex.Cubemaps.ShouldBe(42);

        // Read, with the size resolved from its CODE rather than passed through as one. 0 means the
        // default 32; `1 << (0 - 1)` in C# is `1 << 31`, because the shift count is masked.
        IReadOnlyList<BspCubemap> placed = BspCubemaps.Read(Map(Sample(544, 1952, 929, size: 0)));

        placed.Count.ShouldBe(1);
        placed[0].ShouldBe(new BspCubemap(544, 1952, 929, 32));

        // And the derivation, which is vbsp's own format string (vbsp/cubemap.cpp:511) with the
        // separator empty for a texture name and the whole thing lowercased by Q_strlower.
        BspCubemaps.TextureName("cp_process_final", placed[0])
            .ShouldBe("maps/cp_process_final/c544_1952_929");
    }

    /// <summary>One <c>dcubemapsample_t</c>: three little-endian ints, a size byte, three of padding.</summary>
    /// <remarks>
    /// Sixteen bytes rather than thirteen. C++ pads the struct to its own four-byte alignment and
    /// <c>SwapLumpToDisk&lt;dcubemapsample_t&gt;</c> writes <c>sizeof</c>, so the padding is on disk —
    /// which a reader built to the declaration gets wrong from the SECOND record onward.
    /// <c>BspCubemapsTests</c> carries that case; this one only needs a well-formed record.
    /// </remarks>
    private static byte[] Sample(int x, int y, int z, byte size)
    {
        byte[] record = new byte[BspCubemaps.Stride];

        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0), x);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), y);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), z);
        record[12] = size;

        return record;
    }

    /// <summary>A BSP carrying only a cubemap lump.</summary>
    private static byte[] Map(byte[] sample)
    {
        const int headerSize = 1036;

        byte[] file = new byte[headerSize + sample.Length];

        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 21);
        sample.CopyTo(file, headerSize);

        int entry = 8 + (BspLumpIndex.Cubemaps * 16);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry + 4), sample.Length);

        return file;
    }

    [Test]
    public void Content_ALightStyle_AnimatesAFacesLighting()
    {
        // A face carries styles[MAXLIGHTMAPS] — four bytes at offset 16 (bspfile.h:728) — naming up
        // to four light styles, each with its own lightmap layer. Style 0 is the static light; the
        // others are animated, driven by a per-style intensity the server updates.
        //
        // This project reads the styles field and draws only the first layer, so a flickering light
        // is drawn at its baseline and never changes. Flame, computer screens and the pulsing lights
        // on TF2's control points all animate this way.
        //
        // The lightmap data for the extra layers is already present — they are consecutive in the
        // lighting lump — so this is a matter of reading past the first.
        BspStructLayout.FaceStylesOffset.ShouldBe(16);

        Assert.Ignore(
            "only lightstyle 0 is drawn. A face names up to four and the extra layers sit " +
            "consecutively in the lighting lump, so animated lights render at their static value.");
    }

    [Test]
    public void Content_TheOcclusionAndAreaportalLumps_BoundVisibility()
    {
        // LUMP_OCCLUSION (9) and LUMP_AREAPORTALS (21) are how the engine stops drawing what a
        // doorway or an occluder hides. Neither is read here, which — like the leaf lists — makes
        // this viewer draw MORE than the engine rather than less.
        //
        // Correct and unbounded, which is the same trade as the PVS. Worth specifying because the
        // moment performance matters these are the first two answers, and because a viewer that
        // ignores areaportals shows the inside of a closed room through its doorway.
        IReadOnlyDictionary<string, int> lumps = SourceSdk.Constants("src/public/bspfile.h");

        lumps["LUMP_OCCLUSION"].ShouldBe(9);
        lumps["LUMP_AREAPORTALS"].ShouldBe(21);

        Assert.Ignore(
            "occlusion and areaportal lumps are not read, so nothing is culled by them. Correct " +
            "but unbounded, and a closed room is visible through its doorway.");
    }

    [Test]
    public void Content_Overlays_CarryARenderOrder()
    {
        // This project reads overlays and draws them (B68). What it does not use is the render
        // ORDER, packed into the top two bits of m_nFaceCountAndRenderOrder at offset 6 —
        // OVERLAY_RENDER_ORDER_MASK is 0xC000 and OVERLAY_RENDER_ORDER_NUM_BITS is 2.
        //
        // Two overlays on the same surface are drawn in that order, so ignoring it makes their
        // stacking arbitrary: a stain over a sign or a sign over a stain, decided by lump order
        // rather than by the author.
        IReadOnlyDictionary<string, int> lumps = SourceSdk.Constants("src/public/bspfile.h");

        lumps["OVERLAY_RENDER_ORDER_MASK"].ShouldBe(0xC000);
        BspStructLayout.OverlayFaceCountOffset.ShouldBe(6);

        Assert.Ignore(
            "overlay render order is decoded into the same field as the face count and not used, " +
            "so overlapping decals stack in lump order rather than the author's order.");
    }

    [Test]
    public void Content_Displacements_BlendTwoTexturesByPerVertexAlpha()
    {
        // CDispVert carries m_flAlpha, at offset 16 of the 20-byte record and already derived by
        // DisplacementConformanceTests. On a WorldVertexTransition material that alpha chooses
        // between $basetexture and $basetexture2 per vertex — which is how a dirt path fades into
        // grass across a hillside.
        //
        // The value is read as part of the vertex and not applied, so terrain draws entirely as its
        // first texture. That is a plausible-looking hillside of uniform dirt where the map has a
        // path through grass.
        BspStructLayout.DispVertStride.ShouldBe(20);

        Assert.Ignore(
            "displacement alpha is decoded and not applied, so blended terrain draws as its first " +
            "texture only. CDispVert.m_flAlpha is at offset 16 and already derived.");
    }

    [Test]
    public void Content_TheMap_NamesTheCubemapAndLightingItWasCompiledFor()
    {
        // **HDR and LDR are two complete sets of lighting data**, and a map compiled for HDR carries
        // its real lighting in LUMP_LIGHTING_HDR (53) with the LDR lump (8) holding something stale
        // or empty. BspLumpIndex names both and BspLightmaps chooses between them.
        //
        // What is NOT chosen anywhere is the matching face set: LUMP_FACES_HDR (58) exists for the
        // same reason and this project always reads LUMP_FACES (7). On a map where the two differ,
        // the lighting comes from one set of faces and the geometry from another.
        //
        // Whether TF2's maps actually differ between the two is not established here — this states
        // the hazard and the fact that nothing checks it.
        BspLumpIndex.FacesHdr.ShouldBe(58);
        BspLumpIndex.Faces.ShouldBe(7);

        Assert.Ignore(
            "LUMP_FACES_HDR is never read. HDR lighting is selected but the HDR face set is not, " +
            "so a map whose two face sets differ mixes geometry from one with lighting from the " +
            "other. Whether TF2 maps differ is unmeasured.");
    }

    /// <summary>Every game lump name the engine declares.</summary>
    private static List<string> LumpNames()
    {
        string source = SourceSdk.Text(GameBsp)
            ?? throw new InvalidOperationException($"{GameBsp} is missing from the SDK checkout");

        List<string> names =
        [
            .. System.Text.RegularExpressions.Regex
                .Matches(
                    source,
                    @"(GAMELUMP_[A-Z_]+)\s*=",
                    System.Text.RegularExpressions.RegexOptions.None,
                    TimeSpan.FromSeconds(10))
                .Select(hit => hit.Groups[1].Value),
        ];

        names.Count.ShouldBeGreaterThan(3, "no game lump names were extracted");

        return names;
    }
}
