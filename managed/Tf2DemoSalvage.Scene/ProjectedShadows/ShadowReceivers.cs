using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>One corner of a drawn shadow decal.</summary>
/// <param name="Position">In world space, already lifted off its surface.</param>
/// <param name="TexCoord">Where it samples the atlas: the cell's origin plus its span times (u, v).</param>
/// <param name="Fade">The vertex alpha the <c>Shadow</c> shader subtracts from the coverage, 0 to 1 in 1/255 steps.</param>
public readonly record struct ShadowVertex(Vector3 Position, Vector2 TexCoord, float Fade);

/// <summary>
/// The world surfaces each render-to-texture shadow lands on, as drawable triangles — <c>CShadowMgr::ProjectShadow</c>'s
/// leaf walk and <c>RenderShadows</c>' decal build (`engine.dll` `FUN_180127de0`, `FUN_1801264f0`, `FUN_1801233f0`,
/// `FUN_1801238a0`, `FUN_180121970`; finding 107).
/// </summary>
/// <remarks>
/// **Per shadow**: the leaves its swept box reaches; each leaf's faces once, through <see cref="ShadowReceiver.Takes"/>,
/// clipped by <see cref="ShadowDecal.Clip"/>; each leaf's displacements once, by their box against the shadow's sphere,
/// their base face's bits, and every drawn triangle clipped alike. Each corner's texture coordinate is the cell's origin
/// plus its span times (u, v), and its alpha is <see cref="ShadowDecal.Fade"/> of its depth; polygons are fans.
///
/// **Two approximations, both filed in finding 107**: every surface the walk reaches is drawn, where the engine draws a
/// shadow only on the surfaces drawn this view (the depth test hides the difference on screen); and a displacement's
/// corners are lifted along their TRIANGLE's normal, where `FUN_1800c3bb0` lifts each along its own vertex normal.
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

        SurfaceShadowBits[] bits = new SurfaceShadowBits[world.Faces.Count];

        foreach (BspSurface surface in surfaces)
        {
            if (surface.FaceIndex >= 0 && surface.FaceIndex < bits.Length && world.Faces[surface.FaceIndex] is { } face)
            {
                bits[surface.FaceIndex] = BitsOf(surface.DynamicShadows, face.Flags, material(face.Texdata));
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

    /// <summary>Adds one shadow's decals to a triangle list.</summary>
    /// <param name="shadow">The shadow.</param>
    /// <param name="into">The triangle list, three corners a triangle.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Project(CastShadow shadow, ICollection<ShadowVertex> into)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        ArgumentNullException.ThrowIfNull(into);

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

                Emit(ShadowDecal.Clip(face.Vertices, face.PlaneNormal, p), shadow, into);
            }

            if (leaf < _world.LeafDisplacements.Count)
            {
                foreach (int ray in _world.LeafDisplacements[leaf])
                {
                    if (ray < _world.RayDisplacements.Count && _displacements.Add(ray))
                    {
                        ProjectDisplacement(_world.RayDisplacements[ray].Face, shadow, into);
                    }
                }
            }
        }
    }

    /// <summary>
    /// One displacement — `FUN_1801264f0`'s second loop: its base face's <c>NOSHADOWS</c>, its box against the shadow's
    /// sphere, the base face's dynamic-shadows bit and <c>flags &amp; 0x3020</c>; then each triangle as `FUN_1800c3bb0`
    /// hands it to the shadow manager (`FUN_180127810`), lifted first and clipped like a face.
    /// </summary>
    private void ProjectDisplacement(int face, CastShadow shadow, ICollection<ShadowVertex> into)
    {
        if (face < 0 || face >= _bits.Count || !_displacementOf.TryGetValue(face, out DecalDisplacement? displacement))
        {
            return;
        }

        SurfaceShadowBits bits = _bits[face];
        ShadowProjection p = shadow.Projection;

        if ((bits & SurfaceShadowBits.NoShadows) != 0 ||
            !BoxMeetsSphere(displacement.Mins, displacement.Maxs, p.SphereCentre, p.SphereRadius) ||
            (bits & SurfaceShadowBits.DynamicShadows) == 0 ||
            (bits & (SurfaceShadowBits.Translucent | SurfaceShadowBits.AlphaTest | SurfaceShadowBits.NoShadows)) != 0)
        {
            return;
        }

        IReadOnlyList<DisplacementCorner> corners = displacement.Triangles;

        for (int first = 0; first + 2 < corners.Count; first += 3)
        {
            Vector3 a = corners[first].Position;
            Vector3 b = corners[first + 1].Position;
            Vector3 c = corners[first + 2].Position;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Vector3 lift = cross.LengthSquared() > 0f ? Vector3.Normalize(cross) * ShadowDecal.Lift : Vector3.Zero;

            _triangle[0] = a + lift;
            _triangle[1] = b + lift;
            _triangle[2] = c + lift;

            Emit(ShadowDecal.Clip(_triangle, Vector3.Zero, p), shadow, into);
        }
    }

    /// <summary>`IsBoxIntersectingSphere` (`collisionutils.cpp:259-301`): the squared distance to the box under r².</summary>
    private static bool BoxMeetsSphere(Vector3 min, Vector3 max, Vector3 centre, float radius)
    {
        Vector3 nearest = Vector3.Clamp(centre, min, max);
        return Vector3.DistanceSquared(centre, nearest) < radius * radius;
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
        Vector2 uv = shadow.Cell.TexOrigin + (shadow.Cell.TexSize * new Vector2(vertex.Texture.X, vertex.Texture.Y));
        byte fade = ShadowDecal.Fade(vertex.Texture.Z, p.FalloffStart, p.MaxHeight, FalloffAmount, 0);

        return new ShadowVertex(vertex.Position, uv, fade / 255f);
    }
}
