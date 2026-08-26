namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Which slot of a BSP's directory each kind of data lives in.
/// </summary>
/// <remarks>
/// **One place, because these were scattered and duplicated.** The same numbers appeared in file
/// after file — texinfo as 6 in three readers, leafs as 10 in two — and a magic number written more
/// than once is a magic number that can disagree with itself. Worse, a wrong one does not throw: a
/// lump index off by one reads another lump's bytes as its own, which is entirely valid data with
/// entirely the wrong meaning, and the first sign is a picture that looks strange.
///
/// **Named from Valve's own constants in <c>public/bspfile.h</c>**, and asserted against them by
/// <c>BspLumpTests</c>: the engine declares every one of these, so they are checkable rather than
/// remembered. That test is the reason this file is worth having beyond tidiness.
///
/// **The HDR pairs are the subtle part.** Lighting, faces and leaf ambient each exist twice, and a
/// map compiled for HDR carries its real data in the second while the first holds something stale or
/// empty. Reading the LDR one from an HDR map is how a correctly lit map draws black, and nothing
/// about it errors.
/// </remarks>
internal static class BspLumpIndex
{
    /// <summary>Map entities, as KeyValues text.</summary>
    public const int Entities = 0;

    /// <summary>Plane equations, shared by faces and nodes.</summary>
    public const int Planes = 1;

    /// <summary>Which material each face wears, and its reflectivity.</summary>
    public const int Texdata = 2;

    /// <summary>World vertex positions.</summary>
    public const int Vertexes = 3;

    /// <summary>The PVS. Not read: a viewer that ignores it draws more than the engine, never less.</summary>
    public const int Visibility = 4;

    /// <summary>Interior nodes of the BSP tree.</summary>
    public const int Nodes = 5;

    /// <summary>Texture and lightmap projection per face.</summary>
    public const int Texinfo = 6;

    /// <summary>World faces, in the LDR set.</summary>
    public const int Faces = 7;

    /// <summary>Lightmap samples, LDR.</summary>
    public const int Lighting = 8;

    /// <summary>Leaves of the BSP tree.</summary>
    public const int Leafs = 10;

    /// <summary>Vertex pairs making up face edges.</summary>
    public const int Edges = 12;

    /// <summary>Signed indices into <see cref="Edges"/>, whose sign gives the direction.</summary>
    public const int Surfedges = 13;

    /// <summary>Brush models: the world is model 0, and every <c>*N</c> entity follows.</summary>
    public const int Models = 14;

    /// <summary>The lights the map was compiled with, including the sun.</summary>
    public const int WorldLights = 15;

    /// <summary>Face indices per leaf.</summary>
    public const int LeafFaces = 16;

    /// <summary>Displacement descriptions — the terrain.</summary>
    public const int DispInfo = 26;

    /// <summary>Displacement vertex offsets.</summary>
    public const int DispVerts = 33;

    /// <summary>Per-vertex normals, for smoothed lighting and a tangent basis.</summary>
    /// <remarks>
    /// **Not the same as the face's plane normal, despite what the compiler first writes.** `vbsp`
    /// fills this with `dplanes[f->planenum].normal` and says why in a comment —
    /// *"this doesn't do an exhaustive vertex normal match because the vrad does it"*
    /// (`src/utils/vbsp/normals.cpp:38`). By the time a map ships, **vrad has replaced them** with
    /// true smoothed normals wherever a smoothing group applies.
    ///
    /// So the two agree on flat unsmoothed brushwork and nowhere else, which is why "derive it from
    /// the plane" is not a substitute (D93, B194).
    /// </remarks>
    public const int VertNormals = 30;

    /// <summary>Which normal each face vertex uses; <c>dfaces</c> reference these.</summary>
    /// <remarks><c>unsigned short</c> each, indexing <see cref="VertNormals"/>.</remarks>
    public const int VertNormalIndices = 31;

    /// <summary>Game lumps: static props (<c>sprp</c>) and detail props (<c>dprp</c>) live here.</summary>
    public const int GameLump = 35;

    /// <summary>The map's embedded content, searched before the game's.</summary>
    public const int PakFile = 40;

    /// <summary>Cubemap positions. Not read; <c>$envmap</c> is unimplemented (B55).</summary>
    public const int Cubemaps = 42;

    /// <summary>Material names, as one run of text.</summary>
    public const int TexdataStringData = 43;

    /// <summary>Offsets into <see cref="TexdataStringData"/>.</summary>
    public const int TexdataStringTable = 44;

    /// <summary>Authored decals — the stripes and signage painted onto the world.</summary>
    public const int Overlays = 45;

    /// <summary>Index into <see cref="LeafAmbientLightingHdr"/>.</summary>
    public const int LeafAmbientIndexHdr = 51;

    /// <summary>Index into <see cref="LeafAmbientLighting"/>.</summary>
    public const int LeafAmbientIndex = 52;

    /// <summary>Lightmap samples, HDR. Where an HDR map's real lighting is.</summary>
    public const int LightingHdr = 53;

    /// <summary>World faces, in the HDR set.</summary>
    /// <remarks>
    /// **58, and it was written here as 54 from memory.** Caught by <c>BspLumpTests</c> minutes
    /// after this file was created, which is the whole argument for that test: the number came from
    /// someone recalling a header rather than reading it, looked entirely reasonable next to
    /// LIGHTING_HDR at 53, and would have read another lump's bytes as faces.
    /// </remarks>
    public const int FacesHdr = 58;

    /// <summary>The ambient cubes a model is lit by, HDR.</summary>
    public const int LeafAmbientLightingHdr = 55;

    /// <summary>The ambient cubes a model is lit by, LDR.</summary>
    public const int LeafAmbientLighting = 56;
}
