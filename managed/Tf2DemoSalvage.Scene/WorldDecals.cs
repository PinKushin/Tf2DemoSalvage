using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A decal material as the engine sizes and maps it.</summary>
/// <param name="Name">The material's path, such as <c>decals/concrete/shot1_subrect</c>.</param>
/// <param name="Width">`GetMappingWidth()`: a Subrect's <c>$Size</c>, or the base texture's width.</param>
/// <param name="Height">`GetMappingHeight()`.</param>
/// <param name="DecalScale">`$decalScale`, or 1 when the material declares none.</param>
/// <param name="Paged">`InMaterialPage()`: a Subrect, whose 0..1 coordinates are remapped into its atlas.</param>
/// <param name="PageOffset">`GetMaterialOffset()`, where the window starts in the atlas.</param>
/// <param name="PageScale">`GetMaterialScale()`, how much of the atlas it spans.</param>
/// <param name="Draws">The material actually drawn: a Subrect's atlas (<c>$Material</c>), else the decal's own.</param>
public readonly record struct DecalMaterial(
    string Name,
    int Width,
    int Height,
    float DecalScale,
    bool Paged = false,
    (float U, float V) PageOffset = default,
    (float U, float V) PageScale = default,
    string? Draws = null);

/// <summary>One corner of a drawn decal.</summary>
/// <param name="Position">In world space, already pushed off the face.</param>
/// <param name="U">Texture coordinate, remapped into the page for a Subrect.</param>
/// <param name="V">Texture coordinate down.</param>
/// <param name="LightU">The face's own lightmap coordinate at this point.</param>
/// <param name="LightV">Lightmap coordinate down.</param>
public readonly record struct DecalVertex(Vector3 Position, float U, float V, float LightU, float LightV);

/// <summary>One decal as it sits on one face: the face's polygon clipped to the decal's square.</summary>
/// <param name="Slot">Its slot in the engine's decal pool.</param>
/// <param name="Face">The face it lies on, by face index.</param>
/// <param name="Material">What it draws.</param>
/// <param name="Polygon">The clipped polygon, convex, in the face's winding.</param>
public sealed record PlacedDecal(int Slot, int Face, DecalMaterial Material, IReadOnlyList<DecalVertex> Polygon);

/// <summary>A face as the decal system sees it.</summary>
/// <param name="Index">Its index in the faces lump.</param>
/// <param name="Vertices">Its corners, in winding order.</param>
/// <param name="PlaneNormal">The plane's normal, unflipped for the face's side.</param>
/// <param name="PlaneDistance">The plane's distance.</param>
/// <param name="TextureS">`textureVecsTexelsPerWorldUnits[0]`.</param>
/// <param name="TextureT">`textureVecsTexelsPerWorldUnits[1]`.</param>
/// <param name="Mins">`CalcSurfaceExtents`' texture mins, truncated.</param>
/// <param name="Extents">Its extents: ceil(max) − mins.</param>
/// <param name="OnNode">`SURFDRAW_NODE`: the face lies on a node's plane and the leaf pass skips it.</param>
/// <param name="Displacement">`SURFDRAW_HAS_DISP`: displacements take their own path, which is not built.</param>
/// <param name="RefusesDecals">The flag both passes test with <c>0x4000</c> — see <see cref="WorldDecals"/>.</param>
/// <param name="Lighting">How to light a point on it.</param>
public sealed record DecalFace(
    int Index,
    IReadOnlyList<Vector3> Vertices,
    Vector3 PlaneNormal,
    float PlaneDistance,
    Vector4 TextureS,
    Vector4 TextureT,
    (int S, int T) Mins,
    (int S, int T) Extents,
    bool OnNode,
    bool Displacement,
    bool RefusesDecals,
    LuxelMapping Lighting)
{
    /// <summary>`CalcSurfaceExtents` (`engine.dll` `0x1800fcf80`): per axis, mins = (int)min, extent = ceil(max) − mins.</summary>
    /// <param name="vertices">The corners.</param>
    /// <param name="s">The S texture row.</param>
    /// <param name="t">The T texture row.</param>
    /// <returns>The mins and extents.</returns>
    /// <remarks>
    /// **Truncation toward zero, not floor**, as the binary does it: `(int)fVar12`. The maximum is rounded to nearest
    /// and then raised by one when that fell short of it, which is a ceiling.
    /// </remarks>
    public static ((int S, int T) Mins, (int S, int T) Extents) ExtentsOf(
        IReadOnlyList<Vector3> vertices, Vector4 s, Vector4 t)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        float lowS = 999999f;
        float highS = -99999f;
        float lowT = 999999f;
        float highT = -99999f;

        foreach (Vector3 v in vertices)
        {
            float a = (s.X * v.X) + (s.Y * v.Y) + (s.Z * v.Z) + s.W;
            float b = (t.X * v.X) + (t.Y * v.Y) + (t.Z * v.Z) + t.W;

            lowS = MathF.Min(lowS, a);
            highS = MathF.Max(highS, a);
            lowT = MathF.Min(lowT, b);
            highT = MathF.Max(highT, b);
        }

        return (((int)lowS, (int)lowT), (Ceiling(highS) - (int)lowS, Ceiling(highT) - (int)lowT));

        static int Ceiling(float value)
        {
            int rounded = (int)MathF.Round(value, MidpointRounding.ToEven);

            return rounded < value ? rounded + 1 : rounded;
        }
    }
}

/// <summary>A BSP node as the decal walk sees it.</summary>
/// <param name="Front">Child 0; negative is a leaf, <c>-(leaf + 1)</c>.</param>
/// <param name="Back">Child 1.</param>
/// <param name="Normal">The splitting plane's normal.</param>
/// <param name="Distance">Its distance.</param>
/// <param name="FirstFace">The first face lying on the plane.</param>
/// <param name="FaceCount">How many.</param>
public readonly record struct DecalNode(int Front, int Back, Vector3 Normal, float Distance, int FirstFace, int FaceCount);

/// <summary>The world as the decal system walks it: nodes, each leaf's faces, and the faces.</summary>
/// <param name="Nodes">Every node, the root first.</param>
/// <param name="LeafFaces">Each leaf's face indices, as LEAFFACES lists them.</param>
/// <param name="Faces">Faces by index; null where a face has nothing to decal.</param>
public sealed record DecalWorld(
    IReadOnlyList<DecalNode> Nodes,
    IReadOnlyList<IReadOnlyList<int>> LeafFaces,
    IReadOnlyList<DecalFace?> Faces)
{
    /// <summary>A world with nothing in it, for a map whose tree would not read.</summary>
    public static readonly DecalWorld Empty = new([], [], []);

    /// <summary>The decal system's view of a map, built once at load.</summary>
    /// <param name="tree">The BSP tree.</param>
    /// <param name="leafFaces">The LEAFFACES lump.</param>
    /// <param name="surfaces">The world's faces.</param>
    /// <returns>The world.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A node the reader cannot resolve becomes one leading to leaf 0 both ways with no faces**, so a malformed tree
    /// loses decals under that node rather than the walk throwing.
    /// </remarks>
    public static DecalWorld From(BspLeafTree tree, BspLeafFaces leafFaces, IReadOnlyList<BspSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(leafFaces);
        ArgumentNullException.ThrowIfNull(surfaces);

        DecalNode[] nodes = new DecalNode[tree.NodeCount];

        for (int index = 0; index < nodes.Length; index++)
        {
            nodes[index] = tree.Node(index) is { } node
                ? new DecalNode(
                    node.Front,
                    node.Back,
                    new Vector3(node.NormalX, node.NormalY, node.NormalZ),
                    node.Distance,
                    node.FirstFace,
                    node.FaceCount)
                : new DecalNode(-1, -1, Vector3.UnitZ, 0f, 0, 0);
        }

        int[][] leaves = new int[tree.LeafCount][];

        for (int leaf = 0; leaf < leaves.Length; leaf++)
        {
            (int first, int count) = tree.LeafFaces(leaf);

            leaves[leaf] = new int[count];

            for (int step = 0; step < count; step++)
            {
                leaves[leaf][step] = leafFaces.Face(first + step);
            }
        }

        int highest = -1;

        foreach (BspSurface surface in surfaces)
        {
            highest = Math.Max(highest, surface.FaceIndex);
        }

        DecalFace?[] faces = new DecalFace?[highest + 1];

        foreach (BspSurface surface in surfaces)
        {
            Vector3[] corners = new Vector3[surface.Vertices.Count];

            for (int corner = 0; corner < corners.Length; corner++)
            {
                SurfaceVertex vertex = surface.Vertices[corner];

                corners[corner] = new Vector3(vertex.X, vertex.Y, vertex.Z);
            }

            Vector4 s = new(surface.TextureS.X, surface.TextureS.Y, surface.TextureS.Z, surface.TextureS.Offset);
            Vector4 t = new(surface.TextureT.X, surface.TextureT.Y, surface.TextureT.Z, surface.TextureT.Offset);
            ((int, int) mins, (int, int) extents) = DecalFace.ExtentsOf(corners, s, t);

            faces[surface.FaceIndex] = new DecalFace(
                surface.FaceIndex,
                corners,
                new Vector3(surface.PlaneNormal.X, surface.PlaneNormal.Y, surface.PlaneNormal.Z),
                surface.PlaneDistance,
                s,
                t,
                mins,
                extents,
                surface.OnNode,
                surface.IsDisplacement,
                (surface.Flags & SurfaceProperties.NoDecals) != 0,
                surface.Lighting);
        }

        return new DecalWorld(nodes, leaves, faces);
    }
}

/// <summary>Decals on the world's brushes — the engine's own decal system, out of <c>engine.dll</c> (B415).</summary>
/// <remarks>
/// **Every world or brush decal source ends in one call**: `effects->DecalShoot( index, entity, model, origin,
/// angles, centre, 0, 0 )` — a `CTEWorldDecal`, a `CTEDecal` on the world, and a bullet's impact through
/// `C_BaseEntity::AddBrushModelDecal`. This is what that call does, read function by function in `engine.dll` (x64):
///
/// - **`R_DecalShoot`** (`0x180118730`): brush models only. The radius is half the larger of the material's mapping
///   width and height (integer halves), times `$decalScale`; the decal's world size is the mapping size times
///   `$decalScale`. The walk starts at the model's head node.
/// - **The node walk** (`0x180117d90`): a node whose plane is more than a radius away sends the walk down that
///   side only; otherwise both sides, and the node's own faces when its plane is within ±4 units (strict).
/// - **The leaf pass** (`0x1801175c0`): each leaf face not on a node, not refusing decals, not already decaled by
///   this shot, whose plane is within 4 units.
/// - **The face test** (`0x180118d70`): the decal's basis from the plane normal, projected through the face's
///   texture rows, rejected when it falls outside the face's texture extents.
/// - **`R_DecalCreate`** (`0x1801168b0`): an overlapping older decal may be removed first; then a pool slot —
///   `r_decals` caps the dynamic decals, the oldest going round a ring; then the face's polygon is clipped to the
///   decal's square, and a decal with nothing left is removed again.
/// - **The clip** (`0x1800bcb50`, `0x1800bd520`, `0x1800bcbd0`): u = S·v + 0.5 − dx and v = T·v + 0.5 − dy with
///   the basis scaled by `$decalScale` over the mapping size; clipped v &lt; 1, u &gt; 0, u &lt; 1, v &gt; 0, each
///   strict; each corner pushed 0.1 along the plane's normal; a paged material's coordinates remapped into its page.
///
/// **Not built, and each is filed in B415:** displacements (their own path, `0x800`), brush entities (a door's decals
/// ride the door), static props and models (studio decals), decal fade (`$decalFadeDuration`), player sprays
/// (`0x1000`, a different scale rule). **Interpolated:** that the <c>0x4000</c> face flag both passes test is the
/// BSP's <c>SURF_NODECALS</c>; the flag is set outside `Mod_LoadFaces`, and where was not found.
/// </remarks>
public sealed class WorldDecals
{
    /// <summary>`r_decal_overlap_count`'s default, "3".</summary>
    private const int OverlapCount = 3;

    /// <summary>`r_decal_overlap_area`'s default, "0.4".</summary>
    private const float OverlapArea = 0.4f;

    /// <summary>`r_decal_cover_count`'s default, "4".</summary>
    private const int CoverCount = 4;

    /// <summary>How close a face's plane must be to try it, either side: the walk's `4.0` and `-4.0`.</summary>
    private const float Reach = 4f;

    /// <summary>How far a decal is pushed off its face, along the plane's normal: `0.1`.</summary>
    private const float PushOff = 0.1f;

    /// <summary>`|n.z|` at or below this is a wall: `0.70710677`.</summary>
    private const float WallSlope = 0.70710677f;

    /// <summary>A covered decal is one this much hidden by a larger new one: `0.999`.</summary>
    private const float Covered = 0.999f;

    /// <summary>An overlap this large replaces the older decal whatever the count: `0.9`.</summary>
    private const float Replaces = 0.9f;

    /// <summary>`MAX_DECALS`: `max( 64, r_decals )` at level load.</summary>
    private const int MinimumPool = 64;

    private readonly DecalWorld _world;
    private readonly int _maximum;
    private readonly Decal?[] _slots;
    private readonly Dictionary<int, List<Decal>> _byFace = [];
    private int _dynamic;
    private int _last = -1;

    /// <summary>A decal system over one world.</summary>
    /// <param name="world">The world.</param>
    /// <param name="maximum">`r_decals`, 2048 by default; the pool is never smaller than 64.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public WorldDecals(DecalWorld world, int maximum = 2048)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;
        _maximum = maximum;
        _slots = new Decal?[Math.Max(MinimumPool, maximum)];
    }

    /// <summary>Changes every time a decal is added or removed, so a renderer knows to rebuild.</summary>
    public int Version { get; private set; }

    /// <summary>How many decals are live.</summary>
    public int Count
    {
        get
        {
            int count = 0;

            foreach (Decal? decal in _slots)
            {
                count += decal is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>Every live decal, by slot.</summary>
    /// <param name="into">Cleared, then filled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void Placed(ICollection<PlacedDecal> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (Decal? decal in _slots)
        {
            if (decal is not null)
            {
                into.Add(new PlacedDecal(decal.Slot, decal.Face.Index, decal.Material, decal.Polygon));
            }
        }
    }

    /// <summary>Removes every decal — `r_cleardecals`, and what a seek backwards needs before replaying.</summary>
    public void Clear()
    {
        Array.Clear(_slots);
        _byFace.Clear();
        _dynamic = 0;
        _last = -1;
        Version++;
    }

    /// <summary>`R_DecalShoot` on the world model, with no direction axis and no flags.</summary>
    /// <param name="material">The decal's material.</param>
    /// <param name="position">The decal's centre, in world space.</param>
    public void Shoot(DecalMaterial material, Vector3 position)
    {
        if (_world.Nodes.Count == 0 || material.Width <= 0 || material.Height <= 0)
        {
            return;
        }

        float scale = material.DecalScale > 0f ? material.DecalScale : 1f;
        float radius = Math.Max(material.Width >> 1, material.Height >> 1);
        float inverse = 1f;

        if (material.DecalScale > 0f)
        {
            inverse = 1f / scale;
            radius *= scale;
        }

        Shot shot = new(material, position, radius, inverse, (int)(material.Width / inverse), (int)(material.Height / inverse));

        Walk(0, shot);
    }

    /// <summary>The node walk, `0x180117d90`.</summary>
    private void Walk(int child, Shot shot)
    {
        while (true)
        {
            if (child < 0)
            {
                Leaf(-(child + 1), shot);
                return;
            }

            if (child >= _world.Nodes.Count)
            {
                return;
            }

            DecalNode node = _world.Nodes[child];
            float distance = Vector3.Dot(node.Normal, shot.Position) - node.Distance;

            if (distance > shot.Radius)
            {
                child = node.Front;
                continue;
            }

            if (-shot.Radius <= distance)
            {
                if (distance < Reach && distance > -Reach)
                {
                    for (int face = node.FirstFace; face < node.FirstFace + node.FaceCount; face++)
                    {
                        if (FaceAt(face) is { Displacement: false, RefusesDecals: false } onNode)
                        {
                            TryFace(onNode, shot);
                        }
                    }
                }

                Walk(node.Front, shot);
            }

            child = node.Back;
        }
    }

    /// <summary>The leaf pass, `0x1801175c0`.</summary>
    private void Leaf(int leaf, Shot shot)
    {
        if (leaf >= _world.LeafFaces.Count)
        {
            return;
        }

        foreach (int index in _world.LeafFaces[leaf])
        {
            if (FaceAt(index) is not { OnNode: false, RefusesDecals: false } face || shot.Decaled.Contains(face.Index))
            {
                continue;
            }

            if (MathF.Abs(Vector3.Dot(face.PlaneNormal, shot.Position) - face.PlaneDistance) < Reach)
            {
                TryFace(face, shot);
            }
        }
    }

    private DecalFace? FaceAt(int index) =>
        index >= 0 && index < _world.Faces.Count ? _world.Faces[index] : null;

    /// <summary>The face test, `0x180118d70`: project the decal through the face's texture rows.</summary>
    private void TryFace(DecalFace face, Shot shot)
    {
        (Vector3 s, Vector3 t) = Basis(face.PlaneNormal);

        Vector3 rowS = new(face.TextureS.X, face.TextureS.Y, face.TextureS.Z);
        Vector3 rowT = new(face.TextureT.X, face.TextureT.Y, face.TextureT.Z);

        float spanS = MathF.Abs(Vector3.Dot(rowS, s) * shot.Width) + MathF.Abs(Vector3.Dot(rowS, t) * shot.Height);
        float atS = (Vector3.Dot(rowS, shot.Position) + face.TextureS.W - face.Mins.S) + (spanS * -0.5f);
        float spanT = MathF.Abs(Vector3.Dot(rowT, s) * shot.Width) + MathF.Abs(Vector3.Dot(rowT, t) * shot.Height);
        float atT = (Vector3.Dot(rowT, shot.Position) + face.TextureT.W - face.Mins.T) + (spanT * -0.5f);

        if (atS <= -spanS || atT <= -spanT || face.Extents.S + spanS < atS || face.Extents.T + spanT < atT)
        {
            return;
        }

        Create(face, shot, s, t);
    }

    /// <summary>`R_DecalCreate`, `0x1801168b0`.</summary>
    private void Create(DecalFace face, Shot shot, Vector3 s, Vector3 t)
    {
        if (Overlapping(face, shot, s, t) is { } older)
        {
            Remove(older);
        }

        int slot = Slot();

        // `R_SetupDecalTextureSpaceBasis`, `0x1800bd1c0`: the basis scaled by m_scale over the mapping size, and the
        // centre's own projection kept as the offset.
        float perS = shot.Inverse / shot.Material.Width;
        float perT = shot.Inverse / shot.Material.Height;
        Vector3 scaledS = s * perS;
        Vector3 scaledT = t * perT;

        Decal decal = new(
            slot,
            face,
            shot.Material,
            scaledS,
            scaledT,
            Vector3.Dot(scaledS, shot.Position),
            Vector3.Dot(scaledT, shot.Position),
            shot.Inverse);

        _slots[slot] = decal;
        _dynamic++;

        List<DecalVertex> polygon = Clip(face, decal);

        if (polygon.Count == 0)
        {
            Remove(decal);
            return;
        }

        decal.Polygon = polygon;

        if (!_byFace.TryGetValue(face.Index, out List<Decal>? onFace))
        {
            onFace = [];
            _byFace[face.Index] = onFace;
        }

        onFace.Add(decal);
        shot.Decaled.Add(face.Index);
        Version++;
    }

    /// <summary>
    /// The pool, `0x1801168b0`'s first half: the first empty slot under the `r_decals` cap, else the ring —
    /// `R_FindDynamicDecalSlot`, from the slot after the last one it took.
    /// </summary>
    private int Slot()
    {
        if (Math.Min(_maximum, _slots.Length) > _dynamic)
        {
            for (int index = 0; index < _slots.Length; index++)
            {
                if (_slots[index] is null)
                {
                    return index;
                }
            }
        }

        int start = _last + 1;

        if (start >= _slots.Length || start < 0)
        {
            start = 0;
        }

        int at = start;
        int taken = 0;

        do
        {
            if (_slots[at] is not null)
            {
                taken = at;
                break;
            }

            at++;

            if (at >= _slots.Length)
            {
                at = 0;
            }
        }
        while (at != start);

        if (_slots[taken] is { } evicted)
        {
            Remove(evicted);
        }

        _last = taken;

        return taken;
    }

    /// <summary>`R_DecalUnlink`: out of its slot and off its face.</summary>
    private void Remove(Decal decal)
    {
        if (_slots[decal.Slot] != decal)
        {
            return;
        }

        _slots[decal.Slot] = null;
        _dynamic--;

        if (_byFace.TryGetValue(decal.Face.Index, out List<Decal>? onFace))
        {
            onFace.Remove(decal);
        }

        Version++;
    }

    /// <summary>
    /// The overlap pass, `0x180116b90`: how much of each older decal on the face the new one covers, measured in the
    /// older decal's own texture space.
    /// </summary>
    /// <remarks>
    /// A much smaller older decal (its width under the new one's half width) that is all but covered (over 0.999) is
    /// queued, and beyond `r_decal_cover_count` of those the oldest go. Otherwise an overlap over
    /// `r_decal_overlap_area` counts; the one with the largest overlap-times-width is returned for removal when there
    /// are `r_decal_overlap_count` of them, or when it is itself at least 0.9 covered.
    /// </remarks>
    private Decal? Overlapping(DecalFace face, Shot shot, Vector3 s, Vector3 t)
    {
        if (!_byFace.TryGetValue(face.Index, out List<Decal>? onFace) || onFace.Count == 0)
        {
            return null;
        }

        float halfWidth = shot.Material.Width * (1f / shot.Inverse) * 0.5f;
        float halfHeight = shot.Material.Height * (1f / shot.Inverse);
        Vector3 alongT = t * (halfHeight * 0.5f);

        Decal? candidate = null;
        float best = 0f;
        bool replaces = false;
        int counted = 0;
        List<Decal> covered = [];

        foreach (Decal older in onFace)
        {
            Vector3 low = shot.Position - (s * halfWidth);
            Vector3 high = shot.Position + (s * halfWidth);

            float uHigh = MathF.Min((Vector3.Dot(high, older.S) - older.Dx) + 0.5f, 1f);
            float uLow = MathF.Max((Vector3.Dot(low, older.S) - older.Dx) + 0.5f, 0f);
            float vHigh = MathF.Min((Vector3.Dot(shot.Position + alongT, older.T) - older.Dy) + 0.5f, 1f);
            float vLow = MathF.Max((Vector3.Dot(shot.Position - alongT, older.T) - older.Dy) + 0.5f, 0f);

            if (0f > uHigh - uLow || 0f > vHigh - vLow)
            {
                continue;
            }

            float area = (vHigh - vLow) * (uHigh - uLow);
            float olderWidth = older.Material.Width / older.Inverse;

            if (olderWidth < halfWidth)
            {
                if (Covered < area)
                {
                    covered.Add(older);
                }
            }
            else if (OverlapArea < area)
            {
                counted++;

                float weighted = area * olderWidth;

                if (candidate is null || best < weighted)
                {
                    replaces = Replaces <= area;
                    candidate = older;
                    best = weighted;
                }
            }
        }

        for (int index = 0; index < covered.Count - CoverCount; index++)
        {
            Remove(covered[index]);
        }

        return candidate is not null && (counted >= OverlapCount || replaces) ? candidate : null;
    }

    /// <summary>The clip, `0x1800bd520` then `0x1800bcbd0`.</summary>
    private static List<DecalVertex> Clip(DecalFace face, Decal decal)
    {
        List<(Vector3 P, float U, float V)> polygon = new(face.Vertices.Count);

        foreach (Vector3 corner in face.Vertices)
        {
            polygon.Add((
                corner,
                Vector3.Dot(decal.S, corner) + (0.5f - decal.Dx),
                Vector3.Dot(decal.T, corner) + (0.5f - decal.Dy)));
        }

        polygon = Edge(polygon, static p => p.V < 1f, static (a, b) => (1f - a.V) / (b.V - a.V));
        polygon = Edge(polygon, static p => p.U > 0f, static (a, b) => (0f - a.U) / (b.U - a.U));
        polygon = Edge(polygon, static p => p.U < 1f, static (a, b) => (1f - a.U) / (b.U - a.U));
        polygon = Edge(polygon, static p => p.V > 0f, static (a, b) => (0f - a.V) / (b.V - a.V));

        List<DecalVertex> placed = new(polygon.Count);

        foreach ((Vector3 p, float u, float v) in polygon)
        {
            Vector3 lifted = p + (face.PlaneNormal * PushOff);

            (float mappedU, float mappedV) = decal.Material.Paged
                ? ((u * decal.Material.PageScale.U) + decal.Material.PageOffset.U,
                   (v * decal.Material.PageScale.V) + decal.Material.PageOffset.V)
                : (u, v);

            (float lightU, float lightV) = face.Lighting.Project(lifted.X, lifted.Y, lifted.Z);

            placed.Add(new DecalVertex(lifted, mappedU, mappedV, lightU, lightV));
        }

        return placed;
    }

    /// <summary>One Sutherland–Hodgman edge, in the binary's own order: the previous corner, then this one.</summary>
    private static List<(Vector3 P, float U, float V)> Edge(
        List<(Vector3 P, float U, float V)> polygon,
        Func<(Vector3 P, float U, float V), bool> inside,
        Func<(Vector3 P, float U, float V), (Vector3 P, float U, float V), float> crossing)
    {
        List<(Vector3 P, float U, float V)> kept = new(polygon.Count + 1);

        for (int index = 0; index < polygon.Count; index++)
        {
            (Vector3 P, float U, float V) current = polygon[index];
            (Vector3 P, float U, float V) previous = polygon[(index + polygon.Count - 1) % polygon.Count];

            bool currentIn = inside(current);
            bool previousIn = inside(previous);

            if (currentIn != previousIn)
            {
                (Vector3 P, float U, float V) from = currentIn ? previous : current;
                (Vector3 P, float U, float V) to = currentIn ? current : previous;
                float f = crossing(from, to);

                kept.Add((
                    from.P + ((to.P - from.P) * f),
                    from.U + ((to.U - from.U) * f),
                    from.V + ((to.V - from.V) * f)));
            }

            if (currentIn)
            {
                kept.Add(current);
            }
        }

        return kept;
    }

    /// <summary>`R_DecalComputeBasis`, `0x1800bc6d0`, with no direction axis: walls take t = (0 0 −1).</summary>
    /// <param name="normal">The face plane's normal.</param>
    /// <returns>The unit S and T axes.</returns>
    internal static (Vector3 S, Vector3 T) Basis(Vector3 normal)
    {
        Vector3 s;
        Vector3 t;

        if (MathF.Abs(normal.Z) <= WallSlope)
        {
            t = new Vector3(0f, 0f, -1f);
            s = Vector3.Cross(normal, t);
            t = Vector3.Cross(s, normal);
        }
        else
        {
            s = new Vector3(1f, 0f, 0f);
            t = Vector3.Cross(s, normal);
            s = Vector3.Cross(normal, t);
        }

        (float sx, float sy, float sz) = VectorMath.Normalized(s.X, s.Y, s.Z);
        (float tx, float ty, float tz) = VectorMath.Normalized(t.X, t.Y, t.Z);

        return (new Vector3(sx, sy, sz), new Vector3(tx, ty, tz));
    }

    /// <summary>One shot's `decalinfo_t`, and the faces it has already decaled.</summary>
    private sealed record Shot(DecalMaterial Material, Vector3 Position, float Radius, float Inverse, int Width, int Height)
    {
        public HashSet<int> Decaled { get; } = [];
    }

    /// <summary>A live decal: where it sits and how it is mapped.</summary>
    private sealed record Decal(
        int Slot, DecalFace Face, DecalMaterial Material, Vector3 S, Vector3 T, float Dx, float Dy, float Inverse)
    {
        public IReadOnlyList<DecalVertex> Polygon { get; set; } = [];
    }
}
