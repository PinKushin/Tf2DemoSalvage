using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Every BSP structure size and field offset, derived from the engine's own declaration of it.
/// </summary>
/// <remarks>
/// **A stride is the second-highest-consequence magic number in a format reader, and there are far
/// more of them than lump indices.** Every one fails identically: it lands on real bytes and produces
/// a number that is a perfectly ordinary value of the wrong thing. A face read at 60 bytes instead of
/// 56 walks steadily out of phase across the lump, so the first faces are right and the rest are
/// garbage — a map that draws, with holes.
///
/// **Derived, not compared.** These assertions do not hold a second copy of 56 next to the first.
/// <see cref="CStruct"/> reads <c>dface_t</c> out of <c>public/bspfile.h</c>, sums its members with C's
/// own alignment rules, and the test asserts that total against
/// <see cref="BspStructLayout.FaceStride"/>. A number typed twice tests typing; a number computed
/// from the declaration tests the reader.
///
/// **Offsets matter as much as sizes and are easier to get wrong.** A structure's total can be right
/// while the fields inside it are read from the wrong places — the sum is the same either way — so
/// every offset this project reaches into is asserted by name.
///
/// **The parser refuses rather than guesses**, and these tests check that it did not refuse: a
/// layout that came back null fails loudly instead of skipping a structure, because a silently
/// unchecked stride is exactly what this suite exists to prevent.
/// </remarks>
public sealed class BspStructTests
{
    /// <summary>Where the engine declares the BSP file structures.</summary>
    private const string BspFile = "src/public/bspfile.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void ThePlaneAndVertexAndEdgeSizes_MatchTheirDeclarations()
    {
        Size("dplane_t").ShouldBe(BspStructLayout.PlaneStride);
        Size("dvertex_t").ShouldBe(BspStructLayout.VertexStride);
        Size("dedge_t").ShouldBe(BspStructLayout.EdgeStride);
        Size("dnode_t").ShouldBe(BspStructLayout.NodeStride);
    }

    [Test]
    public void TheFaceLayout_MatchesItsDeclaration()
    {
        CLayout face = Layout("dface_t");

        face.Size.ShouldBe(BspStructLayout.FaceStride);

        // Named individually because each is a place this project reaches into, and a wrong one
        // reads a neighbouring field: texinfo at 12 would return dispinfo, which is -1 on most
        // faces and would send every flat surface to the same material.
        face.Offset("texinfo").ShouldBe(BspStructLayout.FaceTexinfoOffset);
        face.Offset("dispinfo").ShouldBe(BspStructLayout.FaceDisplacementOffset);
        face.Offset("styles").ShouldBe(BspStructLayout.FaceStylesOffset);
        face.Offset("lightofs").ShouldBe(BspStructLayout.FaceLightOffset);
        face.Offset("m_LightmapTextureMinsInLuxels").ShouldBe(BspStructLayout.FaceLuxelMinsOffset);
        face.Offset("m_LightmapTextureSizeInLuxels").ShouldBe(BspStructLayout.FaceLuxelSizeOffset);
    }

    [Test]
    public void TheTexinfoLayout_MatchesItsDeclaration()
    {
        // Declared as `typedef struct texinfo_s { … } texinfo_t`, so the name in the declaration is
        // the _s one. Asking for texinfo_t would find nothing and — without the refusal check in
        // Layout — would skip the whole structure while reporting a pass.
        CLayout texinfo = Layout("texinfo_s");

        texinfo.Size.ShouldBe(BspStructLayout.TexinfoStride);

        texinfo.Offset("lightmapVecsLuxelsPerWorldUnits")
            .ShouldBe(BspStructLayout.TexinfoLightmapVecsOffset);
        texinfo.Offset("flags").ShouldBe(BspStructLayout.TexinfoFlagsOffset);
        texinfo.Offset("texdata").ShouldBe(BspStructLayout.TexinfoTexdataOffset);
    }

    [Test]
    public void TheTexdataSize_MatchesItsDeclaration()
    {
        Size("dtexdata_t").ShouldBe(BspStructLayout.TexdataStride);
    }

    [Test]
    public void TheModelLayout_MatchesItsDeclaration()
    {
        CLayout model = Layout("dmodel_t");

        model.Size.ShouldBe(BspStructLayout.ModelStride);
        model.Offset("firstface").ShouldBe(BspStructLayout.ModelFirstFaceOffset);
    }

    [Test]
    public void TheWorldLightSize_MatchesItsDeclaration()
    {
        // emittype_t is an enum, which is an int. Stated at the call site rather than buried in the
        // parser, because it is the one type here whose size is a language rule rather than a
        // declaration this test can read.
        Size("dworldlight_t").ShouldBe(BspStructLayout.WorldLightStride);
    }

    [Test]
    public void TheWorldLightFalloffFields_MatchTheirDeclaration()
    {
        // **Derived from Valve's struct, not transcribed from it.** Every one of these offsets was
        // arrived at by adding up field sizes by hand first, and a hand-added offset is exactly the
        // kind of thing that is wrong by four bytes and still reads plausibly: a light would simply
        // fall off at the wrong rate, which looks like a lighting choice rather than a parse error.
        CLayout light = Layout("dworldlight_t");

        light.Offset("stopdot").ShouldBe(BspStructLayout.WorldLightStopDotOffset);
        light.Offset("stopdot2").ShouldBe(BspStructLayout.WorldLightStopDot2Offset);
        light.Offset("exponent").ShouldBe(BspStructLayout.WorldLightExponentOffset);
        light.Offset("radius").ShouldBe(BspStructLayout.WorldLightRadiusOffset);

        // The three terms of 1 / (constant + linear * d + quadratic * d^2), which bspfile.h states
        // inline. Their ORDER is the part worth pinning: all three are floats, so swapping two of
        // them parses cleanly and produces a falloff curve that is wrong everywhere except at the
        // one distance where the curves happen to cross.
        light.Offset("constant_attn")
            .ShouldBe(BspStructLayout.WorldLightConstantAttenuationOffset);
        light.Offset("linear_attn").ShouldBe(BspStructLayout.WorldLightLinearAttenuationOffset);
        light.Offset("quadratic_attn")
            .ShouldBe(BspStructLayout.WorldLightQuadraticAttenuationOffset);

        light.Offset("flags").ShouldBe(BspStructLayout.WorldLightFlagsOffset);
    }

    [Test]
    public void TheOverlayLayout_MatchesItsDeclaration()
    {
        CLayout overlay = Layout("doverlay_t");

        overlay.Size.ShouldBe(BspStructLayout.OverlayStride);

        overlay.Offset("nTexInfo").ShouldBe(BspStructLayout.OverlayTexinfoOffset);
        overlay.Offset("m_nFaceCountAndRenderOrder")
            .ShouldBe(BspStructLayout.OverlayFaceCountOffset);
        overlay.Offset("aFaces").ShouldBe(BspStructLayout.OverlayFacesOffset);
        overlay.Offset("flU").ShouldBe(BspStructLayout.OverlayUOffset);
        overlay.Offset("flV").ShouldBe(BspStructLayout.OverlayVOffset);
        overlay.Offset("vecUVPoints").ShouldBe(BspStructLayout.OverlayCornersOffset);
        overlay.Offset("vecOrigin").ShouldBe(BspStructLayout.OverlayOriginOffset);
        overlay.Offset("vecBasisNormal").ShouldBe(BspStructLayout.OverlayBasisNormalOffset);
    }

    [Test]
    public void TheLeafSize_MatchesItsDeclarationForVersionOne()
    {
        // The declaration is the post-version-1 shape: the ambient cube is commented out with a
        // note that it was removed. The older 56-byte leaf is that number plus the cube, checked
        // below rather than here.
        Size("dleaf_t").ShouldBe(BspStructLayout.LeafStride);
    }

    [Test]
    public void TheOlderLeafIsTheNewerOnePlusACube()
    {
        // **Arithmetic on two facts the header states**, which is the strongest form available for
        // a field that was deleted from the declaration. bspfile.h says the CompressedLightCube was
        // removed for version 1 and leaves the member as a comment, so the version 0 size is the
        // version 1 size plus the size of that cube, computed from its own header.
        (BspStructLayout.LeafStride + CubeSize()).ShouldBe(BspStructLayout.LeafStrideWithCube);
    }

    [Test]
    public void TheAmbientLumpSizes_MatchTheirDeclarations()
    {
        // The sample is a light cube plus a position, and the cube's size is derived from its own
        // header rather than stated here — so this checks a chain of three declarations, not a
        // number with a number written beside it.
        Size(
            "dleafambientlighting_t",
            BspFile,
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["CompressedLightCube"] = new(CubeSize(), 1),
            })
            .ShouldBe(BspStructLayout.AmbientSampleStride);

        Size("dleafambientindex_t").ShouldBe(BspStructLayout.AmbientIndexStride);
    }

    [Test]
    public void TheParserAgreesWithAStructureWhoseSizeIsNotInDispute()
    {
        // **The control, and it is not decoration.** Every assertion above is only as good as the
        // layout computation behind it, and a parser that quietly returned zero for an unrecognised
        // member would make a too-small structure agree with a too-small constant. dplane_t is a
        // Vector, a float and an int: twenty bytes, by inspection, in a declaration short enough to
        // read in one line.
        CLayout plane = Layout("dplane_t");

        plane.Members.Count.ShouldBe(3);
        plane.Offset("normal").ShouldBe(0);
        plane.Offset("dist").ShouldBe(12);
        plane.Offset("type").ShouldBe(16);
        plane.Size.ShouldBe(20);
    }

    /// <summary>The size of <c>CompressedLightCube</c>, derived rather than assumed.</summary>
    private static int CubeSize()
    {
        // Six ColorRGBExp32, and that type is itself read rather than stated: three bytes and a
        // signed exponent, declared in mathlib.h.
        int colour = Size("ColorRGBExp32", "src/public/mathlib/mathlib.h");

        return Size(
            "CompressedLightCube",
            "src/public/mathlib/compressed_light_cube.h",
            new Dictionary<string, CTypeSize>(StringComparer.Ordinal)
            {
                ["ColorRGBExp32"] = new(colour, 1),
            });
    }

    /// <summary>The total size of one structure declared in <c>bspfile.h</c>.</summary>
    private static int Size(string name) => Layout(name).Size;

    /// <summary>The total size of one structure in any header.</summary>
    private static int Size(
        string name, string header, IReadOnlyDictionary<string, CTypeSize>? extra = null) =>
        Layout(name, header, extra).Size;

    /// <summary>Reads one structure's layout, failing rather than skipping when it cannot.</summary>
    /// <remarks>
    /// **The refusal has to be loud.** <see cref="CStruct"/> returns null for a structure it cannot
    /// parse, which is the right behaviour there — a guessed layout would be worse than none — but a
    /// test that treated null as "nothing to check" would report a pass for a stride nobody verified.
    /// That is the exact shape of bug this suite exists to catch, so it must not be its own.
    /// </remarks>
    private static CLayout Layout(
        string name,
        string header = BspFile,
        IReadOnlyDictionary<string, CTypeSize>? extra = null)
    {
        string text = SourceSdk.Text(header)
            ?? throw new InvalidOperationException($"{header} is missing from the SDK checkout");

        Dictionary<string, CTypeSize> composites = new(StringComparer.Ordinal)
        {
            // Three floats, and the alignment of its widest member.
            ["Vector"] = new(12, 4),
            ["QAngle"] = new(12, 4),

            // An enum, which C sizes as an int.
            ["emittype_t"] = new(4, 4),
        };

        if (extra is not null)
        {
            foreach ((string type, CTypeSize size) in extra)
            {
                composites[type] = size;
            }
        }

        // **A BSP on disk is a little-endian PC file**, and mathlib.h reverses ColorRGBExp32's
        // field order for the big-endian build. Both branches are four bytes, so the size would
        // agree either way and only the order would be wrong — which is the shape of error that
        // shows up as light of the wrong colour rather than as a failure.
        HashSet<string> defined = new(StringComparer.Ordinal) { "VALVE_LITTLE_ENDIAN" };

        CLayoutAttempt attempt = CStruct.Attempt(
            text, name, SourceSdk.Constants(header), composites, pointerBytes: null, defined);

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived from {header}, so its stride is " +
                $"unchecked rather than correct. Stopped at: {attempt.Refused}");
    }
}
