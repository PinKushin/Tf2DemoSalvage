using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>A brush entity the frame drew, as a shadow receiver.</summary>
/// <param name="Entity">Its render origin and angles, and its model's bounds — <c>GetRenderBounds</c> for a brush model.</param>
/// <param name="FirstFace">Its model's first surface.</param>
/// <param name="FaceCount">How many surfaces its model has.</param>
public readonly record struct ShadowBrush(ShadowEntity Entity, int FirstFace, int FaceCount);

/// <summary>One corner of a drawn shadow decal.</summary>
/// <param name="Position">In world space, already lifted off its surface.</param>
/// <param name="TexCoord">Where it samples: the cell's origin plus its span times (u, v), or (u, v) itself on the blob.</param>
/// <param name="Fade">The vertex alpha the <c>Shadow</c> shader subtracts from the coverage, 0 to 1 in 1/255 steps.</param>
/// <param name="Blob">Whether it is a blob's — `decals/simpleshadow` rather than the atlas (<see cref="CastShadow.Blob"/>).</param>
public readonly record struct ShadowVertex(Vector3 Position, Vector2 TexCoord, float Fade, bool Blob = false);

/// <summary>
/// The world surfaces each render-to-texture shadow lands on, as drawable triangles — <c>CShadowMgr::ProjectShadow</c>'s
/// leaf walk and <c>RenderShadows</c>' decal build (`engine.dll` `FUN_180127de0`, `FUN_1801264f0`, `FUN_1801233f0`,
/// `FUN_1801238a0`, `FUN_180121970`; finding 107).
/// </summary>
/// <remarks>
/// **The engine's two halves.** <see cref="Project(CastShadow)"/> is <c>ProjectShadow</c>: the leaves the shadow's swept box
/// reaches; each leaf's faces once, through <see cref="ShadowReceiver.Takes"/>; each leaf's displacements once, by their box
/// against the shadow's sphere and their base face's bits — every surface that passes linked to the shadow.
/// <see cref="Build"/> is <c>RenderShadows</c>: only the surfaces the view drew, each linked shadow cut by
/// <see cref="ShadowDecal.Clip"/> (a displacement triangle by triangle). Each corner's texture coordinate is the cell's
/// origin plus its span times (u, v), and its alpha is <see cref="ShadowDecal.Fade"/> of its depth; polygons are fans.
///
/// **Brush entities** (<see cref="Begin(IReadOnlyList{ShadowBrush})"/>, <see cref="BuildBrushes"/>) are the client's
/// half: `CClientLeafSystem::ProjectShadow` offers the shadow once to each brush entity in its leaves, and
/// `AddShadowToReceiver` drops it on its own caster and by <see cref="ShadowReceiverCull"/>; then `AddShadowToBrushModel`
/// links its model's surfaces (<see cref="ShadowReceiver.TakesOnBrushModel"/>). The engine cuts those in the model's frame
/// and draws them under its matrix; here the surfaces are placed first and cut in the world, the same arithmetic up to
/// rounding.
///
/// **A gap, filed in finding 107**: the decals are built for the MAIN view's surfaces and drawn in every view, where the
/// engine builds each view's own (a reflection misses a shadow on a surface the main view did not draw).
/// </remarks>
public sealed class ShadowReceivers
{
    /// <summary>`MAX_FALLOFF_AMOUNT`, the falloff every render-to-texture shadow is projected with.</summary>
    private const float FalloffAmount = ShadowProjection.MaxFalloffAmount;

    private readonly BspLeafTree _tree;
    private readonly DecalWorld _world;
    private readonly IReadOnlyList<SurfaceShadowBits> _bits;
    private readonly Dictionary<int, DecalDisplacement> _displacementOf = [];
    private readonly List<int> _leaves = [];
    private readonly HashSet<int> _faces = [];
    private readonly HashSet<int> _displacements = [];
    private readonly Vector3[] _triangle = new Vector3[3];

    /// <summary>Each surface's shadows this frame, by face index — the engine's per-surface shadow-decal list.</summary>
    private readonly List<CastShadow>?[] _shadowsOn;

    /// <summary>The surfaces with any shadow this frame, in the order they were first linked.</summary>
    private readonly List<int> _linked = [];

    /// <summary>The frame's brush entities, the leaves each stands in, and each surface a shadow links on one.</summary>
    private IReadOnlyList<ShadowBrush> _brushes = [];
    private readonly List<List<int>> _brushLeaves = [];
    private readonly Dictionary<int, (ShadowEntity Entity, List<int> Leaves)> _filed = [];
    private readonly List<(CastShadow Shadow, int Brush, int Face)> _brushLinks = [];
    private readonly List<Vector3> _placed = [];

    /// <summary>The leaves the shadow being projected reaches, marked with <see cref="_mark"/>.</summary>
    private readonly int[] _leafMark;
    private int _mark;

    /// <summary>A map's receivers.</summary>
    /// <param name="tree">Its BSP tree, which the swept box walks.</param>
    /// <param name="world">The decal system's view of it: each leaf's faces and displacements.</param>
    /// <param name="bits">Each face's bits, by face index.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ShadowReceivers(BspLeafTree tree, DecalWorld world, IReadOnlyList<SurfaceShadowBits> bits)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(bits);

        _tree = tree;
        _world = world;
        _bits = bits;
        _shadowsOn = new List<CastShadow>?[world.Faces.Count];
        _leafMark = new int[tree.LeafCount];

        foreach (DecalDisplacement displacement in world.Displacements)
        {
            _displacementOf[displacement.Face] = displacement;
        }
    }

    /// <summary>A map's receivers, each face's bits read from its surface and its material.</summary>
    /// <param name="tree">Its BSP tree.</param>
    /// <param name="world">The decal system's view of it.</param>
    /// <param name="surfaces">Its faces, for each one's dynamic-shadows bit.</param>
    /// <param name="material">Each texdata index's material bits: no-cull, translucent, alpha-tested.</param>
    /// <returns>The receivers.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ShadowReceivers ForMap(
        BspLeafTree tree, DecalWorld world, IReadOnlyList<BspSurface> surfaces, Func<int, SurfaceShadowBits> material)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(material);

        // Every face's texinfo and material bits — a brush model's too, whose surfaces are not among the world's drawn
        // ones — and the dynamic-shadows bit from each face the world draws, the only test that reads it.
        SurfaceShadowBits[] bits = new SurfaceShadowBits[world.Faces.Count];

        for (int index = 0; index < bits.Length; index++)
        {
            if (world.Faces[index] is { } face)
            {
                bits[index] = BitsOf(false, face.Flags, material(face.Texdata));
            }
        }

        foreach (BspSurface surface in surfaces)
        {
            if (surface.DynamicShadows && surface.FaceIndex >= 0 && surface.FaceIndex < bits.Length)
            {
                bits[surface.FaceIndex] |= SurfaceShadowBits.DynamicShadows;
            }
        }

        return new ShadowReceivers(tree, world, bits);
    }

    /// <summary>
    /// A face's bits, as `Mod_LoadFaces` (`FUN_180102a40`) and the material pass (`FUN_1800fb5d0`) set them: the
    /// dynamic-shadows bit; texinfo <c>SURF_NOSHADOWS</c>; <c>SURF_TRANS</c> or a translucent material; and the
    /// material's alpha test and two-sidedness.
    /// </summary>
    /// <param name="dynamicShadows">The face's <c>m_NumPrims</c> top bit, inverted.</param>
    /// <param name="flags">Its texinfo's flags.</param>
    /// <param name="material">Its material's no-cull, translucent and alpha-test bits.</param>
    /// <returns>The bits <see cref="ShadowReceiver.Takes"/> reads.</returns>
    public static SurfaceShadowBits BitsOf(bool dynamicShadows, SurfaceProperties flags, SurfaceShadowBits material)
    {
        SurfaceShadowBits bits = material & (SurfaceShadowBits.NoCull | SurfaceShadowBits.Translucent | SurfaceShadowBits.AlphaTest);

        if (dynamicShadows)
        {
            bits |= SurfaceShadowBits.DynamicShadows;
        }

        if ((flags & SurfaceProperties.NoShadows) != 0)
        {
            bits |= SurfaceShadowBits.NoShadows;
        }

        if ((flags & SurfaceProperties.Translucent) != 0)
        {
            bits |= SurfaceShadowBits.Translucent;
        }

        if ((flags & BspFace.NotDrawnSurfaces) != 0)
        {
            bits |= SurfaceShadowBits.NoDraw;
        }

        return bits;
    }

    /// <summary>
    /// A world material's bits: <c>IsTwoSided</c>, <c>IsTranslucent</c> and <c>IsAlphaTested</c>.
    /// </summary>
    /// <param name="texture">The material, or null for one that did not resolve.</param>
    /// <returns>Its bits; none for an unresolved one, which the engine draws opaque with its error material.</returns>
    /// <remarks>
    /// **Translucent is any blended material** — interpolated: the material system marks a material translucent when its
    /// shader enables blending or its alpha modulation is below one, so additive and modulate count with alpha blending.
    /// </remarks>
    public static SurfaceShadowBits MaterialBits(MapTexture? texture)
    {
        if (texture is not { } t)
        {
            return SurfaceShadowBits.None;
        }

        SurfaceShadowBits bits = SurfaceShadowBits.None;

        if (t.IsNoCull)
        {
            bits |= SurfaceShadowBits.NoCull;
        }

        if (t.IsTranslucent || t.IsAdditive || t.IsModulate || t.Modulation is { Alpha: < 1f })
        {
            bits |= SurfaceShadowBits.Translucent;
        }

        if (t.IsTransparent)
        {
            bits |= SurfaceShadowBits.AlphaTest;
        }

        return bits;
    }

    /// <summary>A new frame with no brush entities: every surface's shadow list emptied.</summary>
    public void Begin() => Begin([]);

    /// <summary>
    /// A new frame: every surface's shadow list emptied, and the frame's brush entities filed by the leaves their placed
    /// bounds reach — <c>CalcRenderableWorldSpaceAABB</c>, as the leaf system files a renderable.
    /// </summary>
    /// <param name="brushes">The brush entities the frame drew that can receive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brushes"/> is null.</exception>
    public void Begin(IReadOnlyList<ShadowBrush> brushes)
    {
        ArgumentNullException.ThrowIfNull(brushes);

        foreach (int face in _linked)
        {
            _shadowsOn[face]!.Clear();
        }

        _linked.Clear();
        _brushLinks.Clear();
        _brushLeaves.Clear();
        _brushes = brushes;

        for (int index = 0; index < brushes.Count; index++)
        {
            _brushLeaves.Add(LeavesOf(brushes[index].Entity));
        }
    }

    /// <summary>
    /// The leaves a brush entity stands in — refiled only when its pose or bounds change, as the leaf system refiles a
    /// renderable when it moves (`RenderableChanged`).
    /// </summary>
    private List<int> LeavesOf(ShadowEntity entity)
    {
        bool known = _filed.TryGetValue(entity.EntityIndex, out (ShadowEntity Entity, List<int> Leaves) filed);

        if (known && filed.Entity == entity)
        {
            return filed.Leaves;
        }

        List<int> leaves = known ? filed.Leaves : [];
        (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) = WorldSpaceBounds.Placed(
            new StudioBox(entity.Mins.X, entity.Mins.Y, entity.Mins.Z, entity.Maxs.X, entity.Maxs.Y, entity.Maxs.Z),
            (entity.RenderOrigin.X, entity.RenderOrigin.Y, entity.RenderOrigin.Z),
            entity.RenderAngles);

        leaves.Clear();
        _tree.EnumerateLeavesInBox((minX, minY, minZ), (maxX, maxY, maxZ), leaves);
        _filed[entity.EntityIndex] = (entity, leaves);

        return leaves;
    }

    /// <summary>Adds one shadow's decals to a triangle list.</summary>
    /// <param name="shadow">The shadow.</param>
    /// <param name="into">The triangle list, three corners a triangle.</param>
    /// <remarks>One shadow on every surface it reaches: <see cref="Begin()"/>, <see cref="Project(CastShadow)"/>, and a build
    /// as for a map that cannot be culled, where every surface draws.</remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Project(CastShadow shadow, ICollection<ShadowVertex> into)
    {
        Begin();
        Project(shadow);
        Build(null, null, into);
    }

    /// <summary>
    /// Adds one shadow to every surface it lands on — <c>CShadowMgr::ProjectShadow</c>'s leaf walk (`FUN_180127de0`,
    /// `FUN_1801264f0`), each surface through `FUN_1801233f0` and linked by `FUN_180121d50`. A displacement links to its
    /// base face. Nothing is cut here; <see cref="Build"/> does that for the surfaces a view draws.
    /// </summary>
    /// <param name="shadow">The shadow.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shadow"/> is null.</exception>
    public void Project(CastShadow shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);

        ShadowProjection p = shadow.Projection;
        Vector3 delta = p.Direction * p.MaxHeight;
        float half = p.Size.Length() * 0.5f;

        _leaves.Clear();
        _faces.Clear();
        _displacements.Clear();
        _tree.EnumerateLeavesAlongSweptBox((p.Origin.X, p.Origin.Y, p.Origin.Z), (delta.X, delta.Y, delta.Z), (half, half, half), _leaves);

        foreach (int leaf in _leaves)
        {
            if (leaf < 0 || leaf >= _world.LeafFaces.Count)
            {
                continue;
            }

            foreach (int index in _world.LeafFaces[leaf])
            {
                if (index < 0 || index >= _bits.Count || !_faces.Add(index) || _world.Faces[index] is not { } face ||
                    face.Displacement || !ShadowReceiver.Takes(p, face.PlaneNormal, face.PlaneDistance, _bits[index]))
                {
                    continue;
                }

                Link(index, shadow);
            }

            if (leaf < _world.LeafDisplacements.Count)
            {
                foreach (int ray in _world.LeafDisplacements[leaf])
                {
                    if (ray < _world.RayDisplacements.Count && _displacements.Add(ray))
                    {
                        ProjectDisplacement(_world.RayDisplacements[ray].Face, shadow);
                    }
                }
            }
        }

        if (_brushes.Count > 0)
        {
            ProjectOnBrushes(shadow);
        }
    }

    /// <summary>
    /// The shadow's linked surfaces on the brush entities, each placed where its entity stands and cut — the brush model's
    /// <c>RenderProjectedTextures</c> (`FUN_1800e05d0` → `FUN_18012a530`), drawn after the entity's own surfaces and decals.
    /// </summary>
    /// <param name="into">The triangle list, three corners a triangle, in the world.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void BuildBrushes(ICollection<ShadowVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach ((CastShadow shadow, int brush, int face) in _brushLinks)
        {
            ShadowEntity entity = _brushes[brush].Entity;
            DecalFace surface = _world.Faces[face]!;
            (Vector3 forward, Vector3 left, Vector3 up) = ShadowProjection.Basis(entity.RenderAngles);

            _placed.Clear();

            for (int corner = 0; corner < surface.Vertices.Count; corner++)
            {
                _placed.Add(entity.RenderOrigin + Rotate(surface.Vertices[corner], forward, left, up));
            }

            Emit(ShadowDecal.Clip(_placed, Rotate(surface.PlaneNormal, forward, left, up), shadow.Projection), shadow, into);
        }
    }

    /// <summary>
    /// The brush entities in any leaf the shadow reaches — each once, as `CClientLeafSystem::ProjectShadow`'s enumeration
    /// count makes it — through `AddShadowToReceiver` and `AddShadowToBrushModel`.
    /// </summary>
    private void ProjectOnBrushes(CastShadow shadow)
    {
        _mark++;

        foreach (int leaf in _leaves)
        {
            if (leaf >= 0 && leaf < _leafMark.Length)
            {
                _leafMark[leaf] = _mark;
            }
        }

        for (int brush = 0; brush < _brushes.Count; brush++)
        {
            if (Reached(_brushLeaves[brush]))
            {
                ProjectOnBrush(shadow, brush);
            }
        }
    }

    /// <summary>Whether any of these leaves carries the current mark.</summary>
    private bool Reached(List<int> leaves)
    {
        foreach (int leaf in leaves)
        {
            if (leaf >= 0 && leaf < _leafMark.Length && _leafMark[leaf] == _mark)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// `AddShadowToReceiver` for a brush model (`clientshadowmgr.cpp:3439-3460`) — never its own caster, and not culled —
    /// then `AddShadowToBrushModel` (`FUN_180121f70`): the direction into the model's frame (`VectorIRotate` by its angles,
    /// never its origin) and every surface of its model through <see cref="ShadowReceiver.TakesOnBrushModel"/>.
    /// </summary>
    private void ProjectOnBrush(CastShadow shadow, int brush)
    {
        ShadowBrush receiver = _brushes[brush];

        if (receiver.Entity.EntityIndex == shadow.Source.EntityIndex ||
            ShadowReceiverCull.Culls(shadow.Projection, receiver.Entity, shadow.Source))
        {
            return;
        }

        (Vector3 forward, Vector3 left, Vector3 up) = ShadowProjection.Basis(receiver.Entity.RenderAngles);
        Vector3 direction = shadow.Projection.Direction;
        Vector3 local = new(Vector3.Dot(forward, direction), Vector3.Dot(left, direction), Vector3.Dot(up, direction));
        int end = Math.Min(receiver.FirstFace + receiver.FaceCount, Math.Min(_world.Faces.Count, _bits.Count));

        for (int face = Math.Max(receiver.FirstFace, 0); face < end; face++)
        {
            if (_world.Faces[face] is { } surface && ShadowReceiver.TakesOnBrushModel(local, surface.PlaneNormal, _bits[face]))
            {
                _brushLinks.Add((shadow, brush, face));
            }
        }
    }

    /// <summary>A vector in a frame's axes placed in the world: <c>VectorRotate</c> by <c>AngleMatrix</c>.</summary>
    private static Vector3 Rotate(Vector3 local, Vector3 forward, Vector3 left, Vector3 up) =>
        (local.X * forward) + (local.Y * left) + (local.Z * up);

    /// <summary>
    /// Cuts every linked shadow to the surfaces a view draws — <c>RenderShadows</c> (`FUN_18012ae80`), which builds decals
    /// only on the surfaces <c>R_DrawWorldLists</c> handed it as it drew them (vtable slot 0xc8), in that order.
    /// </summary>
    /// <param name="surfaces">The faces the view drew, in draw order; null for a map that cannot be culled, where every
    /// linked surface is drawn.</param>
    /// <param name="displacements">The displacements the view's walk reached; those in view are drawn.</param>
    /// <param name="into">The triangle list, three corners a triangle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void Build(IReadOnlyList<int>? surfaces, IReadOnlyList<ReachedDisplacement>? displacements, ICollection<ShadowVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (surfaces is null)
        {
            foreach (int face in _linked)
            {
                BuildOn(face, into);
            }

            return;
        }

        // A displacement is built from the displacement list, never from a surface list that names its base face too: a
        // second build would multiply the frame by the shadow twice.
        foreach (int face in surfaces)
        {
            if (face >= 0 && face < _world.Faces.Count && _world.Faces[face] is { Displacement: false })
            {
                BuildOn(face, into);
            }
        }

        foreach (ReachedDisplacement displacement in displacements ?? [])
        {
            if (displacement.InView)
            {
                BuildOn(displacement.Face, into);
            }
        }
    }

    /// <summary>`FUN_180121d50`: the shadow on the surface's list, the surface on the frame's.</summary>
    private void Link(int face, CastShadow shadow)
    {
        List<CastShadow> list = _shadowsOn[face] ??= [];

        if (list.Count == 0)
        {
            _linked.Add(face);
        }

        list.Add(shadow);
    }

    /// <summary>Each shadow linked to one surface, cut to it — a face's polygon, or a displacement's triangles.</summary>
    private void BuildOn(int face, ICollection<ShadowVertex> into)
    {
        if (face < 0 || face >= _shadowsOn.Length || _shadowsOn[face] is not { Count: > 0 } shadows || _world.Faces[face] is not { } surface)
        {
            return;
        }

        foreach (CastShadow shadow in shadows)
        {
            if (surface.Displacement)
            {
                BuildDisplacement(face, shadow, into);
            }
            else
            {
                Emit(ShadowDecal.Clip(surface.Vertices, surface.PlaneNormal, shadow.Projection), shadow, into);
            }
        }
    }

    /// <summary>
    /// One displacement — `FUN_1801264f0`'s second loop: its base face's <c>NOSHADOWS</c>, its box against the shadow's
    /// sphere, the base face's dynamic-shadows bit and <c>flags &amp; 0x3020</c>; the shadow is then linked to the base face.
    /// </summary>
    private void ProjectDisplacement(int face, CastShadow shadow)
    {
        if (face < 0 || face >= _bits.Count || !_displacementOf.TryGetValue(face, out DecalDisplacement? displacement))
        {
            return;
        }

        SurfaceShadowBits bits = _bits[face];
        ShadowProjection p = shadow.Projection;

        if ((bits & SurfaceShadowBits.NoShadows) != 0 ||
            !ShadowReceiverCull.BoxMeetsSphere(displacement.Mins, displacement.Maxs, p.SphereCentre, p.SphereRadius) ||
            (bits & SurfaceShadowBits.DynamicShadows) == 0 ||
            (bits & (SurfaceShadowBits.Translucent | SurfaceShadowBits.AlphaTest | SurfaceShadowBits.NoShadows)) != 0)
        {
            return;
        }

        Link(face, shadow);
    }

    /// <summary>A displacement's decal: each triangle as `FUN_1800c3bb0` hands it over (`FUN_180127810`), lifted, clipped like a face.</summary>
    /// <remarks>
    /// **Node by node** (`FUN_1800c57b0` → `FUN_1800c2520`, `FUN_1800c25b0`, `FUN_1800c7660`): a leaf node — a 3×3 patch —
    /// is reached when its nine unlifted corners' texture-space box meets the shadow's (<see cref="LeafMeets"/>), a larger
    /// node when any quarter does, and each reached node's own triangles are cut. Each corner is lifted 0.1 along ITS OWN
    /// normal first (`FUN_1800c3bb0`, `disp+0xb0`).
    /// </remarks>
    private void BuildDisplacement(int face, CastShadow shadow, ICollection<ShadowVertex> into)
    {
        if (!_displacementOf.TryGetValue(face, out DecalDisplacement? displacement) || displacement.Mesh is not { } mesh)
        {
            return;
        }

        ShadowProjection p = shadow.Projection;
        int leaves = (mesh.Side - 1) / 2;

        if (_leafBits.Length < leaves * leaves)
        {
            _leafBits = new bool[leaves * leaves];
        }

        for (int row = 0; row < leaves; row++)
        {
            for (int column = 0; column < leaves; column++)
            {
                _leafBits[(row * leaves) + column] = LeafMeets(mesh, (2 * column) + 1, (2 * row) + 1, p);
            }
        }

        foreach (DisplacementNode node in mesh.Nodes)
        {
            if (node.Count == 0 || !NodeMeets(node.X, node.Y, node.Reach, leaves))
            {
                continue;
            }

            for (int first = node.First; first + 2 < node.First + node.Count; first += 3)
            {
                // **Valve's winding, clockwise from the front as a face's**: the tesselation is stored reversed for the
                // physics mesh (`DisplacementTesselation`), and the decal pass culls back faces.
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = mesh.Indices[first + 2 - corner];
                    _triangle[corner] = mesh.Positions[vertex] + (mesh.Normals[vertex] * ShadowDecal.Lift);
                }

                Emit(ShadowDecal.Clip(_triangle, Vector3.Zero, p), shadow, into);
            }
        }
    }

    /// <summary>Each leaf node's bit for the displacement being built, row-major by leaf.</summary>
    private bool[] _leafBits = [];

    /// <summary>A node's bit: a leaf's own, or the OR of its four quarters' (`FUN_1800c25b0`'s interior case).</summary>
    private bool NodeMeets(int x, int y, int reach, int leaves)
    {
        if (reach <= 1)
        {
            return _leafBits[(((y - 1) / 2) * leaves) + ((x - 1) / 2)];
        }

        int half = reach / 2;

        return NodeMeets(x + half, y + half, half, leaves) || NodeMeets(x - half, y + half, half, leaves) ||
            NodeMeets(x - half, y - half, half, leaves) || NodeMeets(x + half, y - half, half, leaves);
    }

    /// <summary>
    /// `FUN_1800c25b0`'s leaf: the 3×3 patch around a centre through the shadow's world-to-texture matrix, kept when its box
    /// meets the unit square and the depths from 0 to the max height — every test non-strict.
    /// </summary>
    private static bool LeafMeets(DisplacementMesh mesh, int x, int y, ShadowProjection p)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        for (int row = y - 1; row <= y + 1; row++)
        {
            for (int column = x - 1; column <= x + 1; column++)
            {
                Vector3 texture = p.WorldToTexture(mesh.Positions[(row * mesh.Side) + column]);

                min = Vector3.Min(min, texture);
                max = Vector3.Max(max, texture);
            }
        }

        return min.X <= 1f && max.X >= 0f && min.Y <= 1f && max.Y >= 0f && max.Z >= 0f && min.Z <= p.MaxHeight;
    }

    /// <summary>A clipped polygon as fan triangles (0, i + 1, i + 2), each corner mapped into the cell and faded.</summary>
    private static void Emit(IReadOnlyList<ShadowDecalVertex>? polygon, CastShadow shadow, ICollection<ShadowVertex> into)
    {
        if (polygon is not { Count: >= 3 })
        {
            return;
        }

        for (int i = 0; i + 2 < polygon.Count; i++)
        {
            into.Add(Corner(polygon[0], shadow));
            into.Add(Corner(polygon[i + 1], shadow));
            into.Add(Corner(polygon[i + 2], shadow));
        }
    }

    private static ShadowVertex Corner(ShadowDecalVertex vertex, CastShadow shadow)
    {
        ShadowProjection p = shadow.Projection;
        Vector2 uv = shadow.TexOrigin + (shadow.TexSize * new Vector2(vertex.Texture.X, vertex.Texture.Y));
        byte fade = ShadowDecal.Fade(vertex.Texture.Z, p.FalloffStart, p.MaxHeight, FalloffAmount, 0);

        return new ShadowVertex(vertex.Position, uv, fade / 255f, shadow.Blob);
    }
}
