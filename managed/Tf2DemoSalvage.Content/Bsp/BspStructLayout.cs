namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// How big each BSP structure is, and where its fields sit inside one.
/// </summary>
/// <remarks>
/// **A stride and an offset fail the same silent way a lump index does**, and there are far more of
/// them. Reading a face's texinfo index from byte 12 instead of byte 10 returns a number; that number
/// indexes a real texinfo; the map draws with the wrong material on some surfaces and nothing
/// reports anything. The only thing separating a correct reader from that one is a number nobody can
/// check by looking at it.
///
/// **These were spread across nine files and duplicated between them** — <c>FaceStride = 56</c> in
/// three, <c>TexinfoStride = 72</c> in four, <c>PlaneStride = 20</c> in three — which is nine chances
/// to fix one and miss the rest. One place, and <c>BspStructTests</c> derives every value below from
/// the structure declarations in <c>public/bspfile.h</c>: the sizes are computed from the members
/// Valve declares, not compared against numbers typed a second time.
///
/// **Two entries used to sit here as uncoverable and neither survived being looked at**, which is
/// worth recording because both exclusions sounded like properties of the format and were properties
/// of the reader.
///
/// <c>ddispinfo_t</c> was excluded because it embeds <c>CDispNeighbor</c>, "a class rather than a
/// struct". C++ makes those identical for layout — they differ only in default access — so the
/// obstacle was one keyword missing from a regex. <c>DisplacementConformanceTests</c> now derives
/// the whole chain: a sub-neighbour is six bytes including its trailing pad, a neighbour is two of
/// those, and the record ends at 176.
///
/// The static prop lump was excluded for having "a per-version layout". It has four, all declared,
/// and the versions only append — so <c>StaticPropConformanceTests</c> checks the property the
/// reader actually depends on: origin, angles and prop type sit at the same offsets in every one of
/// them, which is what makes reading without dispatching on the version correct rather than lucky.
/// </remarks>
internal static class BspStructLayout
{
    /// <summary>Bytes per <c>dplane_t</c>: a normal, a distance, and a type.</summary>
    public const int PlaneStride = 20;

    /// <summary>Bytes per <c>dvertex_t</c>: one <c>Vector</c>.</summary>
    public const int VertexStride = 12;

    /// <summary>Bytes per <c>dedge_t</c>: two vertex indices.</summary>
    public const int EdgeStride = 4;

    /// <summary>Bytes per surfedge: one signed index whose sign gives the direction.</summary>
    /// <remarks>
    /// Not a struct — the lump is a plain <c>int</c> array — so the layout test cannot derive it. It
    /// is here for the readers rather than for the check, and named so that is visible.
    /// </remarks>
    public const int SurfedgeStride = 4;

    /// <summary>Bytes per <c>dnode_t</c>.</summary>
    public const int NodeStride = 32;

    /// <summary>Bytes per <c>dface_t</c>.</summary>
    public const int FaceStride = 56;

    /// <summary>Bytes per <c>texinfo_t</c>.</summary>
    public const int TexinfoStride = 72;

    /// <summary>Bytes per <c>dtexdata_t</c>.</summary>
    public const int TexdataStride = 32;

    /// <summary>Bytes per entry in the texdata string table: one int offset.</summary>
    public const int StringTableStride = 4;

    /// <summary>Bytes per <c>dmodel_t</c>.</summary>
    public const int ModelStride = 48;

    /// <summary>Bytes per <c>dworldlight_t</c>.</summary>
    public const int WorldLightStride = 88;

    /// <summary>Where <c>dworldlight_t.stopdot</c> sits: the start of a spotlight's penumbra.</summary>
    public const int WorldLightStopDotOffset = 48;

    /// <summary>Where <c>dworldlight_t.stopdot2</c> sits: the end of the penumbra.</summary>
    public const int WorldLightStopDot2Offset = 52;

    /// <summary>Where <c>dworldlight_t.exponent</c> sits.</summary>
    public const int WorldLightExponentOffset = 56;

    /// <summary>Where <c>dworldlight_t.radius</c> sits: the cutoff distance.</summary>
    public const int WorldLightRadiusOffset = 60;

    /// <summary>
    /// Where <c>dworldlight_t.constant_attn</c> sits, first of the three falloff terms.
    /// </summary>
    /// <remarks>
    /// Valve states the falloff inline in <c>bspfile.h</c>, so it is read rather than derived:
    /// <c>1 / (constant_attn + linear_attn * dist + quadratic_attn * dist^2)</c>, for
    /// <c>emit_spotlight</c> and <c>emit_point</c>. The linear and quadratic terms follow at four
    /// byte intervals.
    /// </remarks>
    public const int WorldLightConstantAttenuationOffset = 64;

    /// <summary>Where <c>dworldlight_t.linear_attn</c> sits.</summary>
    public const int WorldLightLinearAttenuationOffset = 68;

    /// <summary>Where <c>dworldlight_t.quadratic_attn</c> sits.</summary>
    public const int WorldLightQuadraticAttenuationOffset = 72;

    /// <summary>Where <c>dworldlight_t.flags</c> sits, holding the <c>DWL_FLAGS_</c> bits.</summary>
    public const int WorldLightFlagsOffset = 76;

    /// <summary>Bytes per <c>doverlay_t</c>.</summary>
    public const int OverlayStride = 352;

    /// <summary>Bytes per <c>dleaf_t</c> once the ambient cube was moved out, in lump version 1.</summary>
    public const int LeafStride = 32;

    /// <summary>Bytes per <c>dleaf_t</c> while the ambient cube was still inline, in version 0.</summary>
    /// <remarks>
    /// **Not derivable from the header, because the field is commented out there.** <c>bspfile.h</c>
    /// leaves <c>// CompressedLightCube m_AmbientLighting;</c> as a comment with a note that it was
    /// removed for version 1, so the declaration describes only the newer shape. 32 plus a 24-byte
    /// cube is 56, which is arithmetic on two facts the header does state.
    /// </remarks>
    public const int LeafStrideWithCube = 56;

    /// <summary>Bytes per <c>dleafambientlighting_t</c>: a cube and a position.</summary>
    public const int AmbientSampleStride = 28;

    /// <summary>Bytes per <c>dleafambientindex_t</c>: a count and a first index.</summary>
    public const int AmbientIndexStride = 4;

    /// <summary>Bytes per <c>ddispinfo_t</c>.</summary>
    /// <remarks>
    /// Not covered by the layout test: <c>ddispinfo_t</c> embeds <c>CDispNeighbor</c> and
    /// <c>CDispCornerNeighbors</c>, which are classes with methods rather than plain structures.
    /// </remarks>
    public const int DispInfoStride = 176;

    /// <summary>Bytes per <c>CDispVert</c>: a direction, a distance, and an alpha.</summary>
    public const int DispVertStride = 20;

    /// <summary>Byte offset of <c>texinfo</c> inside a <c>dface_t</c>.</summary>
    public const int FaceTexinfoOffset = 10;

    /// <summary>Byte offset of <c>dispinfo</c> inside a <c>dface_t</c>, −1 when the face is flat.</summary>
    public const int FaceDisplacementOffset = 12;

    /// <summary>Byte offset of <c>styles</c> inside a <c>dface_t</c>: four lightstyle slots.</summary>
    public const int FaceStylesOffset = 16;

    /// <summary>Byte offset of <c>lightofs</c> inside a <c>dface_t</c>, −1 when unlit.</summary>
    public const int FaceLightOffset = 20;

    /// <summary>Byte offset of <c>m_LightmapTextureMinsInLuxels</c> inside a <c>dface_t</c>.</summary>
    /// <remarks>
    /// **The field whose absence lights every surface with the wrong patch.** The lightmap vectors
    /// are shared by every face on a texinfo; these mins are what place one face inside its own
    /// lightmap, so forgetting them produces a lit map rather than an error.
    /// </remarks>
    public const int FaceLuxelMinsOffset = 28;

    /// <summary>Byte offset of <c>m_LightmapTextureSizeInLuxels</c> inside a <c>dface_t</c>.</summary>
    public const int FaceLuxelSizeOffset = 36;

    /// <summary>Byte offset of <c>lightmapVecsLuxelsPerWorldUnits</c> inside a <c>texinfo_t</c>.</summary>
    public const int TexinfoLightmapVecsOffset = 32;

    /// <summary>Byte offset of <c>flags</c> inside a <c>texinfo_t</c>: the <c>SURF_*</c> bits.</summary>
    public const int TexinfoFlagsOffset = 64;

    /// <summary>Byte offset of <c>texdata</c> inside a <c>texinfo_t</c>.</summary>
    public const int TexinfoTexdataOffset = 68;

    /// <summary>Byte offset of <c>firstface</c> inside a <c>dmodel_t</c>.</summary>
    public const int ModelFirstFaceOffset = 40;

    /// <summary>Byte offset of <c>nTexInfo</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayTexinfoOffset = 4;

    /// <summary>Byte offset of <c>m_nFaceCountAndRenderOrder</c> inside a <c>doverlay_t</c>.</summary>
    /// <remarks>The top two bits are the render order; the rest is the count.</remarks>
    public const int OverlayFaceCountOffset = 6;

    /// <summary>Byte offset of <c>aFaces</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayFacesOffset = 8;

    /// <summary>Byte offset of <c>flU</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayUOffset = 264;

    /// <summary>Byte offset of <c>flV</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayVOffset = 272;

    /// <summary>Byte offset of <c>vecUVPoints</c> inside a <c>doverlay_t</c>: four corners.</summary>
    public const int OverlayCornersOffset = 280;

    /// <summary>Byte offset of <c>vecOrigin</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayOriginOffset = 328;

    /// <summary>Byte offset of <c>vecBasisNormal</c> inside a <c>doverlay_t</c>.</summary>
    public const int OverlayBasisNormalOffset = 340;

    /// <summary>Byte offset of <c>startPosition</c> inside a <c>ddispinfo_t</c>.</summary>
    public const int DispStartPositionOffset = 0;

    /// <summary>Byte offset of <c>m_iDispVertStart</c> inside a <c>ddispinfo_t</c>.</summary>
    public const int DispVertexStartOffset = 12;

    /// <summary>Byte offset of <c>power</c> inside a <c>ddispinfo_t</c>.</summary>
    public const int DispPowerOffset = 20;
}
