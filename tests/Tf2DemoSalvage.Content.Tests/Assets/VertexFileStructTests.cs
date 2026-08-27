using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every VTX and VVD structure size, derived from the engine's own declaration of it.
/// </summary>
/// <remarks>
/// **These are strides in a six-level nested walk** — body part, model, LOD, mesh, strip group,
/// strip — so one wrong number puts every level below it out of phase. What comes back is not an
/// error: it is a set of real indices pointing at the wrong vertices, which draws a model made of
/// the right triangles in the wrong places.
///
/// **The packing is the interesting part.** optimize.h wraps its declarations in
/// <c>#pragma pack(1)</c>, so these structures are byte-packed and several of them end on odd sizes:
/// <c>StripHeader_t</c> is 27, <c>MeshHeader_t</c> is 9. Natural alignment would round both up, and
/// the reader would then walk past the end of every array by one byte per element.
/// </remarks>
public sealed class VertexFileStructTests
{
    /// <summary>Where the engine declares the VTX structures.</summary>
    private const string VtxFile = "src/public/optimize.h";

    /// <summary>Where the engine declares the VVD structures.</summary>
    private const string VvdFile = "src/public/studio.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheVtxNestingStrides_MatchTheirDeclarations()
    {
        Vtx("FileHeader_t").Size.ShouldBe(VertexFileLayout.VtxHeaderStride);
        Vtx("BodyPartHeader_t").Size.ShouldBe(VertexFileLayout.VtxBodyPartStride);
        Vtx("ModelHeader_t").Size.ShouldBe(VertexFileLayout.VtxModelStride);
        Vtx("ModelLODHeader_t").Size.ShouldBe(VertexFileLayout.VtxLodStride);
        Vtx("MeshHeader_t").Size.ShouldBe(VertexFileLayout.VtxMeshStride);
    }

    [Test]
    public void TheVertexLayout_MatchesItsDeclaration()
    {
        CLayout vertex = Vtx("Vertex_t");

        vertex.Size.ShouldBe(VertexFileLayout.VtxVertexStride);
        vertex.Offset("origMeshVertID").ShouldBe(VertexFileLayout.VtxVertexOriginalIdOffset);
    }

    [Test]
    public void TheStripStrides_MatchTheirPublishedDeclarations()
    {
        Vtx("StripGroupHeader_t").Size.ShouldBe(VertexFileLayout.VtxStripGroupStride);
        Vtx("StripHeader_t").Size.ShouldBe(VertexFileLayout.VtxStripStride);
    }

    [Test]
    public void VertexFileStructs_TheTopologyVariants_AreEightBytesLarger()
    {
        // **The gap this suite cannot close, asserted for what it can.** Later builds add
        // numTopologyIndices and topologyOffset to both strip structures under a define the
        // published headers do not carry, so the larger sizes are not derivable from source. What
        // IS checkable is the relationship: two ints, on each of the two structures.
        (VertexFileLayout.VtxStripGroupStrideWithTopology - VertexFileLayout.VtxStripGroupStride)
            .ShouldBe(8, "numTopologyIndices and topologyOffset, four bytes each");

        (VertexFileLayout.VtxStripStrideWithTopology - VertexFileLayout.VtxStripStride)
            .ShouldBe(8, "the same pair on the strip");
    }

    [Test]
    public void VertexFileStructs_ThePacking_ExplainsTheOddSizes()
    {
        // **The control for the pack(1) argument, and it is a real distinction.** StripHeader_t ends
        // with a short and a byte among ints; packed it is 27, naturally aligned it is 28. If the
        // parser silently ignored packing, this test would be the only thing that noticed — every
        // other assertion above would just be comparing two wrong numbers.
        int packed = Vtx("StripHeader_t").Size;

        int natural = CStruct.Layout(
            SourceSdk.Text(VtxFile)!, "StripHeader_t", SourceSdk.Constants(VtxFile), null, 4)!.Size;

        packed.ShouldBe(27);
        natural.ShouldBe(28, "without pack(1) the compiler would round this up");
        packed.ShouldNotBe(natural);
    }

    [Test]
    public void VertexFileStructs_TheVtxVersion_IsTheOneTheReaderTargets()
    {
        SourceSdk.Constants(VtxFile)["OPTIMIZED_MODEL_FILE_VERSION"]
            .ShouldBe(VertexFileLayout.VtxVersion);
    }

    [Test]
    public void TheVvdHeaderAndFixupStrides_MatchTheirDeclarations()
    {
        CLayout header = Layout(VvdFile, "vertexFileHeader_t", pack: null);

        header.Size.ShouldBe(VertexFileLayout.VvdHeaderStride);
        Layout(VvdFile, "vertexFileFixup_t", pack: null).Size
            .ShouldBe(VertexFileLayout.FixupStride);
    }

    [Test]
    public void VertexFileStructs_TheVvdVersionAndLodCount_AreTheEngines()
    {
        IReadOnlyDictionary<string, int> studio = SourceSdk.Constants(VvdFile);

        studio["MODEL_VERTEX_FILE_VERSION"].ShouldBe(VertexFileLayout.VvdVersion);
        studio["MAX_NUM_LODS"].ShouldBe(VertexFileLayout.MaximumLods);
    }

    /// <summary>Reads a byte-packed VTX structure.</summary>
    private static CLayout Vtx(string name) => Layout(VtxFile, name, pack: 1);

    /// <summary>Named integers a header can see, including the one it includes.</summary>
    /// <remarks>
    /// **optimize.h does not declare its own array bound.** <c>Vertex_t</c> is
    /// <c>unsigned char boneWeightIndex[MAX_NUM_BONES_PER_VERT]</c> and that macro lives in
    /// studio.h, which optimize.h includes — so a VTX structure cannot be sized from optimize.h
    /// alone. Stated here rather than worked around, because it is the same fact the reader depends
    /// on: three bones per vertex is a studio limit that the strip format inherits.
    /// </remarks>
    private static Dictionary<string, int> Constants(string header)
    {
        Dictionary<string, int> merged = new(SourceSdk.Constants(VvdFile), StringComparer.Ordinal);

        foreach ((string name, int value) in SourceSdk.Constants(header))
        {
            merged[name] = value;
        }

        return merged;
    }

    /// <summary>Reads one structure, failing rather than skipping when it cannot.</summary>
    private static CLayout Layout(string header, string name, int? pack)
    {
        string text = SourceSdk.Text(header)
            ?? throw new InvalidOperationException($"{header} is missing from the SDK checkout");

        Dictionary<string, CTypeSize> composites = new(StringComparer.Ordinal)
        {
            ["Vector"] = new(12, 4),
            ["Vector2D"] = new(8, 4),
            ["Vector4D"] = new(16, 4),
            ["Quaternion"] = new(16, 4),
            ["RadianEuler"] = new(12, 4),
            ["matrix3x4_t"] = new(48, 4),
        };

        CLayoutAttempt attempt = CStruct.Attempt(
            text, name, Constants(header), composites, pointerBytes: 4, pack: pack);

        return attempt.Layout
            ?? throw new InvalidOperationException(
                $"the layout of {name} could not be derived from {header}, so its stride is " +
                $"unchecked rather than correct. Stopped at: {attempt.Refused}");
    }
}
