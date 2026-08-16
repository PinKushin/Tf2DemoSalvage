namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How big each structure is in the two files that carry a model's geometry.
/// </summary>
/// <remarks>
/// **A model is three files and only one of them holds triangles.** The <c>.mdl</c> names the parts,
/// the <c>.vvd</c> holds the vertices, and the <c>.vtx</c> holds the strips that index them. Every
/// one of these numbers is a stride in a nested walk — body part, model, LOD, mesh, strip group,
/// strip — so a single wrong one puts every level below it out of phase and the indices that come
/// back are real numbers pointing at the wrong vertices.
///
/// **VTX is byte-packed, and that is why its sizes look wrong.** <c>optimize.h:31</c> opens with
/// <c>#pragma pack(1)</c>, so <c>StripHeader_t</c> is 27 bytes — four ints, a short, a byte, two more
/// ints — where natural alignment would make it 28. Reading it as 28 walks one byte further into
/// every strip after the first.
///
/// Checked against <c>public/optimize.h</c> and <c>public/studio.h</c> by <c>VertexFileStructTests</c>,
/// with the exception noted on the topology sizes below.
/// </remarks>
internal static class VertexFileLayout
{
    /// <summary>The VTX version this reader targets: <c>OPTIMIZED_MODEL_FILE_VERSION</c>.</summary>
    public const int VtxVersion = 7;

    /// <summary>Bytes per <c>FileHeader_t</c>.</summary>
    public const int VtxHeaderStride = 36;

    /// <summary>Bytes per <c>BodyPartHeader_t</c>: a count and an offset.</summary>
    /// <remarks>
    /// **Every VTX name here is prefixed and the reason is a measured hazard.** The VTX file mirrors
    /// the MDL's nesting with the same words for different structures — a body part is 16 bytes in
    /// the MDL and 8 here, a model 148 against 8, a mesh 116 against 9, a vertex 48 against 9. Four
    /// pairs of plausible numbers under four identical names, in two files a single reader walks
    /// together.
    /// </remarks>
    public const int VtxBodyPartStride = 8;

    /// <summary>Bytes per <c>ModelHeader_t</c>: a LOD count and an offset.</summary>
    public const int VtxModelStride = 8;

    /// <summary>Bytes per <c>ModelLODHeader_t</c>: mesh count, offset, and a switch distance.</summary>
    public const int VtxLodStride = 12;

    /// <summary>Bytes per <c>MeshHeader_t</c>: strip group count, offset, and flags.</summary>
    public const int VtxMeshStride = 9;

    /// <summary>Bytes per <c>Vertex_t</c>: three bone weights' indices, a count, an id, three bones.</summary>
    public const int VtxVertexStride = 9;

    /// <summary>Byte offset of <c>origMeshVertID</c> inside a <c>Vertex_t</c>.</summary>
    /// <remarks>
    /// **The field that turns a strip index into a vertex.** A VTX vertex is not a vertex — it is a
    /// reference to one in the VVD, and this is the reference. Reading it two bytes early returns
    /// the bone count and the model collapses to a point.
    /// </remarks>
    public const int VtxVertexOriginalIdOffset = 4;

    /// <summary>Bytes per <c>StripGroupHeader_t</c> as the published SDK declares it.</summary>
    public const int VtxStripGroupStride = 25;

    /// <summary>Bytes per <c>StripHeader_t</c> as the published SDK declares it.</summary>
    public const int VtxStripStride = 27;

    /// <summary>Bytes per <c>StripGroupHeader_t</c> once the topology fields exist.</summary>
    /// <remarks>
    /// **Not derivable from source-sdk-2013, and this says so rather than implying otherwise.**
    /// Later builds add <c>numTopologyIndices</c> and <c>topologyOffset</c> to both strip
    /// structures — eight bytes each — under a define the published headers do not carry. So these
    /// two constants are arithmetic on the checked ones (25 + 8, 27 + 8) plus the observation that
    /// real TF2 VTX files parse cleanly at the larger size, which is a measurement rather than a
    /// citation.
    ///
    /// <c>VertexFileStructTests</c> asserts the published pair and the eight-byte relationship, and
    /// cannot assert the fields themselves. Recorded as a gap so it is not mistaken for covered.
    /// </remarks>
    public const int VtxStripGroupStrideWithTopology = 33;

    /// <summary>Bytes per <c>StripHeader_t</c> once the topology fields exist.</summary>
    public const int VtxStripStrideWithTopology = 35;

    /// <summary>The VVD version this reader targets: <c>MODEL_VERTEX_FILE_VERSION</c>.</summary>
    public const int VvdVersion = 4;

    /// <summary><c>IDSV</c>, the VVD's identifier.</summary>
    public const int VvdIdentifier = 0x56534449;

    /// <summary>Bytes per <c>vertexFileHeader_t</c>.</summary>
    public const int VvdHeaderStride = 64;

    /// <summary>Bytes per <c>vertexFileFixup_t</c>: a LOD, a source vertex, and a count.</summary>
    /// <remarks>
    /// **Fixups are why a VVD cannot be read as a flat array.** When a model has several LODs the
    /// vertices are stored once and reordered per LOD by this table; ignoring it reads LOD 0's
    /// vertices in the file's order, which is a different order.
    /// </remarks>
    public const int FixupStride = 12;

    /// <summary>Levels of detail a model may declare: <c>MAX_NUM_LODS</c>.</summary>
    public const int MaximumLods = 8;
}
