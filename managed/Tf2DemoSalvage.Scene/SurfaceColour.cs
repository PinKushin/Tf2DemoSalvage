using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A material's low-res image — the VTF thumbnail `CTexture::LoadLowResTexture` keeps — and its mapping size.</summary>
/// <param name="Rgba">The thumbnail, four bytes a texel.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
/// <param name="MappingWidth">`GetMappingWidth()`, which the texture coordinate is divided by.</param>
/// <param name="MappingHeight">`GetMappingHeight()`.</param>
public sealed record SurfaceThumbnail(byte[] Rgba, int Width, int Height, int MappingWidth, int MappingHeight);

/// <summary>`GetColorForSurface` for a trace into the world's brushes: the colour impact debris is tinted (B415).</summary>
/// <remarks>
/// **Three closed functions, read in the binaries:**
///
/// - **`R_LightVec`** (`engine.dll` `0x1800d40a0`, walk `0x1800d4b00`, face test `0x1800d3990`): the ray walks the tree
///   near side first. Where it crosses a node's plane it tries that node's faces, skipping sky (kept aside) and water,
///   and returns the first that takes it. In a leaf it tries the faces not on a node — neither nodraw nor water nor
///   displacement — whose plane the ray meets from the front, keeping each that takes it nearer than the best so far.
///   A face takes the point when it lies inside the face's lightmap rectangle, or, for a face with no lightmap, inside
///   the lightmap bounds of its corners. Each face that takes it adds its light — its per-style AVERAGE, because
///   `r_avglight` defaults to 1 — and sets the texture coordinate: `(p · textureVecs + w) / GetMappingWidth()`.
/// - **`GetLowResColorSample`** (`materialsystem.dll` `0x180039c10`, through `CMaterial`'s representative texture):
///   the thumbnail sampled bilinearly with wrap, bytes over 255, no gamma conversion.
/// - **`GetColorForSurface`** itself (`c_impact_effects.cpp:96`, published): `pow( diffuse, 1/2.2 ) · base`, the ray
///   run to 1.1 times the distance to the hit.
///
/// **Water is skipped in both passes.** The node pass tests `SURFDRAW_SKY | SURFDRAW_WATERSURFACE` (`0x4`, `0x10000`)
/// and the leaf pass `0x10812` (water, displacement, nodraw, node), read from `0x1800d4b00`. The engine sets the water
/// flag from the material; here a texinfo's `SURF_WARP` (`bspflags.h`, `0x0008`), which vbsp gives water faces, stands
/// in for it — an interpolation.
///
/// **Displacements come after the walk** (`0x1800d40a0`): each leaf the walk enters lists its displacements
/// (`0x1800d3640`), and each is then ray-tested up to the nearest fraction taken so far. A hit ADDS the luxel under it
/// to whatever the brush faces added, and its face and texture coordinate replace theirs.
///
/// **Each style is scaled by its running value** (<see cref="StyleScale"/>). Static props never reach here —
/// `GetColorForSurface` sends a prop hit to `GetStaticPropMaterialColorAndLighting` instead. When no face takes the
/// ray, or the face has no thumbnail, the engine's base colour is an uninitialised local; zero is used.
/// </remarks>
public sealed class SurfaceColour
{
    private readonly DecalWorld _world;
    private readonly BspLightSamples _samples;
    private readonly Func<int, SurfaceThumbnail?> _thumbnail;

    /// <summary>A resolver over one map.</summary>
    /// <param name="world">The tree, each leaf's faces and the faces.</param>
    /// <param name="samples">The map's lightmap data.</param>
    /// <param name="thumbnail">A texdata's low-res image, or null.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public SurfaceColour(DecalWorld world, BspLightSamples samples, Func<int, SurfaceThumbnail?> thumbnail)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(thumbnail);

        _world = world;
        _samples = samples;
        _thumbnail = thumbnail;
    }

    /// <summary>`d_lightstylevalue[ style ] / 264`: what each style's light is scaled by; one, as stored, unless set.</summary>
    public Func<int, float> StyleScale { get; init; } = static _ => 1f;

    /// <summary>`GetColorForSurface` for a world trace from <paramref name="start"/> that stopped at <paramref name="end"/>.</summary>
    /// <param name="start">`trace.startpos`.</param>
    /// <param name="end">`trace.endpos`.</param>
    /// <returns>The colour, gamma-corrected, nominally 0 to 1.</returns>
    public (float R, float G, float B) At(Vector3 start, Vector3 end)
    {
        Walk walk = new(start, (end - start) * 1.1f);
        DecalFace? found = _world.Nodes.Count == 0 ? null : Node(0, 0f, 1f, ref walk);

        foreach (int index in walk.Displacements)
        {
            found = Displacement(_world.RayDisplacements[index], ref walk) ?? found;
        }

        if (found is not { } face)
        {
            return default;
        }

        (float r, float g, float b) = _thumbnail(face.Texdata) is { } image ? Sample(image, walk.S, walk.T) : default;

        return Final(walk.Light, (r, g, b));
    }

    /// <summary>`GetColorForSurface` for a trace that stopped on a static prop.</summary>
    /// <param name="lighting">The model lighting at the hit.</param>
    /// <param name="sun">The sun there, or null.</param>
    /// <param name="end">`trace.endpos`.</param>
    /// <param name="normal">`trace.plane.normal`.</param>
    /// <returns>The colour, gamma-corrected.</returns>
    /// <remarks>
    /// `GetStaticPropMaterialColorAndLighting` (`engine.dll` `0x1802051e0`) hands the prop's model to
    /// `GetModelMaterialColorAndLighting` (`0x1801c9f10`), whose studio branch sets the base colour to a flat 0.5 gray, not the
    /// model's material, and the light to
    /// <see cref="StudioPointLighting"/> at the hit along its normal.
    /// </remarks>
    public static (float R, float G, float B) OfStaticProp(PointLighting lighting, SunLight? sun, Vector3 end, Vector3 normal) =>
        Final(StudioPointLighting.At(lighting, sun, end, normal), (0.5f, 0.5f, 0.5f));

    /// <summary>`pow( diffuse, 1/2.2 ) · base`, per channel.</summary>
    private static (float R, float G, float B) Final(Vector3 light, (float R, float G, float B) colour) =>
        (Gamma(light.X) * colour.R, Gamma(light.Y) * colour.G, Gamma(light.Z) * colour.B);

    private static float Gamma(float linear) => MathF.Pow(linear, 1f / 2.2f);

    /// <summary>`CTexture::GetLowResColorSample`: bilinear over the thumbnail, wrapping, bytes over 255.</summary>
    /// <param name="image">The thumbnail.</param>
    /// <param name="s">Across, in texture repeats.</param>
    /// <param name="t">Down.</param>
    /// <returns>The colour, not gamma-converted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    public static (float R, float G, float B) Sample(SurfaceThumbnail image, float s, float t)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length < image.Width * image.Height * 4)
        {
            return default;
        }

        // `if ( s < 0 ) s += 1 − (int)s`, then only the fraction is used.
        if (s < 0f)
        {
            s += 1f - (int)s;
        }

        if (t < 0f)
        {
            t += 1f - (int)t;
        }

        float across = (s - (int)s) * image.Width;
        float down = (t - (int)t) * image.Height;
        int x = (int)across;
        int y = (int)down;
        int x1 = (x + 1) % image.Width;
        int y1 = (y + 1) % image.Height;
        float fx = across - x;
        float fy = down - y;

        (float R, float G, float B) a = Texel(image, x, y);
        (float R, float G, float B) b = Texel(image, x1, y);
        (float R, float G, float B) c = Texel(image, x, y1);
        (float R, float G, float B) d = Texel(image, x1, y1);

        return (
            (((a.R * (1f - fx)) + (b.R * fx)) * (1f - fy)) + (((c.R * (1f - fx)) + (d.R * fx)) * fy),
            (((a.G * (1f - fx)) + (b.G * fx)) * (1f - fy)) + (((c.G * (1f - fx)) + (d.G * fx)) * fy),
            (((a.B * (1f - fx)) + (b.B * fx)) * (1f - fy)) + (((c.B * (1f - fx)) + (d.B * fx)) * fy));

        static (float R, float G, float B) Texel(SurfaceThumbnail image, int x, int y)
        {
            int at = ((y * image.Width) + x) * 4;

            return (image.Rgba[at] * (1f / 255f), image.Rgba[at + 1] * (1f / 255f), image.Rgba[at + 2] * (1f / 255f));
        }
    }

    /// <summary>`0x1800d4b00`'s node branch.</summary>
    private DecalFace? Node(int node, float from, float to, ref Walk walk)
    {
        while (true)
        {
            if (node < 0)
            {
                return Leaf(-node - 1, from, to, ref walk);
            }

            if (node >= _world.Nodes.Count)
            {
                return null;
            }

            DecalNode plane = _world.Nodes[node];
            float along = Vector3.Dot(plane.Normal, walk.Delta);
            float at = Vector3.Dot(plane.Normal, walk.Start) - plane.Distance;
            float near = (along * from) + at;
            float far = (along * to) + at;
            int nearChild = near < 0f ? plane.Back : plane.Front;

            if ((far < 0f) == (near < 0f))
            {
                node = nearChild;
                continue;
            }

            float fraction = near / (near - far);
            float middle = ((1f - fraction) * from) + (fraction * to);

            if (Node(nearChild, from, middle, ref walk) is { } found)
            {
                return found;
            }

            for (int each = 0; each < plane.FaceCount; each++)
            {
                if (Face(plane.FirstFace + each) is { } face &&
                    (face.Flags & (SurfaceProperties.Sky | SurfaceProperties.Sky2D | SurfaceProperties.Warp)) == 0 &&
                    Takes(face, middle, ref walk))
                {
                    return face;
                }
            }

            node = near < 0f ? plane.Front : plane.Back;
            from = middle;
        }
    }

    /// <summary>`0x1800d4b00`'s leaf branch: the faces not on a node, met from the front.</summary>
    private DecalFace? Leaf(int leaf, float from, float to, ref Walk walk)
    {
        if (leaf < 0 || leaf >= _world.LeafFaces.Count)
        {
            return null;
        }

        // `0x1800d3640`: the leaf's displacements join the walk's list, each once.
        if (leaf < _world.LeafDisplacements.Count)
        {
            foreach (int index in _world.LeafDisplacements[leaf])
            {
                if (!walk.Displacements.Contains(index))
                {
                    walk.Displacements.Add(index);
                }
            }
        }

        DecalFace? found = null;

        foreach (int index in _world.LeafFaces[leaf])
        {
            if (Face(index) is not { } face ||
                face.OnNode ||
                face.Displacement ||
                (face.Flags & (SurfaceProperties.NoDraw | SurfaceProperties.Warp)) != 0)
            {
                continue;
            }

            float along = Vector3.Dot(face.PlaneNormal, walk.Delta);

            if (along > 0f)
            {
                continue;
            }

            float at = Vector3.Dot(face.PlaneNormal, walk.Start) - face.PlaneDistance;
            float near = (along * from) + at;
            float far = (along * to) + at;

            if ((far < 0f) == (near < 0f))
            {
                continue;
            }

            // **The fraction within this leaf's piece of the ray, compared with the best over the WHOLE ray** — the
            // binary's own comparison, reproduced.
            float fraction = near / (near - far);

            if (fraction < walk.Best && Takes(face, ((1f - fraction) * from) + (fraction * to), ref walk))
            {
                found = face;
            }
        }

        return found;
    }

    /// <summary>`0x1800d3990`: whether a face takes the point at a fraction of the ray, and what it reports if so.</summary>
    private bool Takes(DecalFace face, float fraction, ref Walk walk)
    {
        if ((face.Flags & SurfaceProperties.NoLight) != 0)
        {
            return false;
        }

        Vector3 point = walk.Start + (walk.Delta * fraction);
        LuxelMapping lighting = face.Lighting;
        float s = Project(lighting.Across, point);
        float t = Project(lighting.Down, point);

        if (s < lighting.MinU || t < lighting.MinV)
        {
            return false;
        }

        float ds = s - lighting.MinU;
        float dt = t - lighting.MinV;
        int extentS = lighting.Width - 1;
        int extentT = lighting.Height - 1;
        float reachT;

        if (extentS == 0 && extentT == 0)
        {
            // No lightmap: the bound is the corners' lightmap reach, seeded — as the binary seeds it — with the mins.
            float reachS = lighting.MinU;

            reachT = lighting.MinV;

            foreach (Vector3 corner in face.Vertices)
            {
                reachS = MathF.Max(reachS, Project(lighting.Across, corner) - lighting.MinU);
                reachT = MathF.Max(reachT, Project(lighting.Down, corner) - lighting.MinV);
            }

            if (reachS < ds)
            {
                return false;
            }
        }
        else
        {
            if (extentS < ds)
            {
                return false;
            }

            reachT = extentT;
        }

        if (dt > reachT)
        {
            return false;
        }

        walk.Best = fraction;

        if (_thumbnail(face.Texdata) is { MappingWidth: > 0, MappingHeight: > 0 } image)
        {
            walk.S = (Vector3.Dot(new Vector3(face.TextureS.X, face.TextureS.Y, face.TextureS.Z), point) + face.TextureS.W) /
                     image.MappingWidth;
            walk.T = (Vector3.Dot(new Vector3(face.TextureT.X, face.TextureT.Y, face.TextureT.Z), point) + face.TextureT.W) /
                     image.MappingHeight;
        }

        (float r, float g, float b) = _samples.Average(face.Index, styles: true, StyleScale);

        walk.Light += new Vector3(r, g, b);

        return true;

        static float Project((float X, float Y, float Z, float Offset) row, Vector3 p) =>
            (row.X * p.X) + (row.Y * p.Y) + (row.Z * p.Z) + row.Offset;
    }

    /// <summary>
    /// A displacement the walk listed: `CDispInfo`'s ray test (`0x1800c2dd0`, `AABBTree_Ray`) over the ray up to the
    /// nearest fraction taken so far, then, on a hit, the luxel under it (`0x1800d37d0`) and the texture coordinate
    /// (`0x1800bfc90`).
    /// </summary>
    private DecalFace? Displacement(RayDisplacement displacement, ref Walk walk)
    {
        if (displacement.NoRay || Face(displacement.Face) is not { } face)
        {
            return null;
        }

        DisplacementCollisionTree tree = displacement.Tree;
        Vector3 delta = walk.Delta * walk.Best;
        float nearest = 1f;
        (int A, int B, int C) hit = default;
        (float U, float V) at = default;
        bool struck = false;

        // `AABBTree_TreeTrisRayBarycentricTest` (`dispcoll_common.cpp:598`): each triangle as ( 0, 2, 1 ), strictly nearer.
        foreach ((int a, int b, int c) in tree.Triangles)
        {
            if (Barycentric(walk.Start, delta, tree.Vertices[a], tree.Vertices[c], tree.Vertices[b]) is { } found &&
                found.U >= 0f && found.V >= 0f && found.U + found.V <= 1f &&
                found.T > 0f && found.T < nearest)
            {
                nearest = found.T;
                hit = (a, b, c);
                at = (found.U, found.V);
                struck = true;
            }
        }

        if (!struck)
        {
            return null;
        }

        walk.Best *= nearest;

        // The hit's place on the grid, from its three corners' ( column, row ) as `P = v0 + u·( v1 − v0 ) + v·( v2 − v0 )`
        // over ( a, c, b ).
        int side = tree.Side;
        float span = side - 1;
        float column = Grid(hit.A % side, hit.C % side, hit.B % side) / span;
        float row = Grid(hit.A / side, hit.C / side, hit.B / side) / span;
        LuxelMapping lighting = face.Lighting;

        (float red, float green, float blue) = _samples.Luxel(
            face.Index,
            (int)(column * (lighting.Width - 1)),
            (int)(row * (lighting.Height - 1)),
            lighting.Width,
            lighting.Height,
            (face.Flags & SurfaceProperties.BumpLight) != 0,
            styles: true,
            StyleScale);

        walk.Light += new Vector3(red, green, blue);

        if (_thumbnail(face.Texdata) is { MappingWidth: > 0, MappingHeight: > 0 } image && tree.Corners.Count == 4)
        {
            IReadOnlyList<Vector3> corners = tree.Corners;
            Vector3 flat = Vector3.Lerp(
                Vector3.Lerp(corners[0], corners[1], row), Vector3.Lerp(corners[3], corners[2], row), column);

            walk.S = (Vector3.Dot(new Vector3(face.TextureS.X, face.TextureS.Y, face.TextureS.Z), flat) + face.TextureS.W) /
                     image.MappingWidth;
            walk.T = (Vector3.Dot(new Vector3(face.TextureT.X, face.TextureT.Y, face.TextureT.Z), flat) + face.TextureT.W) /
                     image.MappingHeight;
        }

        return face;

        float Grid(int first, int second, int third) => first + (at.U * (second - first)) + (at.V * (third - first));
    }

    /// <summary>`ComputeIntersectionBarycentricCoordinates` (`collisionutils.cpp:140`) for a ray, no box offset.</summary>
    private static (float U, float V, float T)? Barycentric(Vector3 start, Vector3 delta, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 edge1 = v2 - v1;
        Vector3 edge2 = v3 - v1;
        Vector3 dirCrossEdge2 = Vector3.Cross(delta, edge2);
        float denom = Vector3.Dot(dirCrossEdge2, edge1);

        if (MathF.Abs(denom) < 1e-6f)
        {
            return null;
        }

        denom = 1f / denom;

        Vector3 org = start - v1;
        float u = Vector3.Dot(dirCrossEdge2, org) * denom;
        Vector3 orgCrossEdge1 = Vector3.Cross(org, edge1);
        float v = Vector3.Dot(orgCrossEdge1, delta) * denom;
        float t = Vector3.Dot(orgCrossEdge1, edge2) * denom;

        return t is < 0f or > 1f ? null : (u, v, t);
    }

    private DecalFace? Face(int index) =>
        index >= 0 && index < _world.Faces.Count ? _world.Faces[index] : null;

    /// <summary>The walk's state: the ray, the nearest fraction taken, and what the faces that took it reported.</summary>
    private struct Walk(Vector3 start, Vector3 delta)
    {
        public readonly Vector3 Start = start;
        public readonly Vector3 Delta = delta;
        public readonly List<int> Displacements = [];
        public float Best = 1f;
        public Vector3 Light;
        public float S;
        public float T;
    }
}
