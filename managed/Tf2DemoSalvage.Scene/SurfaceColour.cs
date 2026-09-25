using System;
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
/// **Not built:** displacements and static props (`R_LightVec` also tests them) and light styles other than their
/// level-start value of 264. When no face takes the ray, or the face has no thumbnail, the engine's base colour is an
/// uninitialised local; zero is used.
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

    /// <summary>`GetColorForSurface` for a world trace from <paramref name="start"/> that stopped at <paramref name="end"/>.</summary>
    /// <param name="start">`trace.startpos`.</param>
    /// <param name="end">`trace.endpos`.</param>
    /// <returns>The colour, gamma-corrected, nominally 0 to 1.</returns>
    public (float R, float G, float B) At(Vector3 start, Vector3 end)
    {
        Walk walk = new(start, (end - start) * 1.1f);

        if (_world.Nodes.Count == 0 || Node(0, 0f, 1f, ref walk) is not { } face)
        {
            return default;
        }

        (float r, float g, float b) = _thumbnail(face.Texdata) is { } image ? Sample(image, walk.S, walk.T) : default;

        return (Gamma(walk.Light.X) * r, Gamma(walk.Light.Y) * g, Gamma(walk.Light.Z) * b);

        static float Gamma(float linear) => MathF.Pow(linear, 1f / 2.2f);
    }

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

        (float r, float g, float b) = _samples.Average(face.Index, styles: true, static _ => 1f);

        walk.Light += new Vector3(r, g, b);

        return true;

        static float Project((float X, float Y, float Z, float Offset) row, Vector3 p) =>
            (row.X * p.X) + (row.Y * p.Y) + (row.Z * p.Z) + row.Offset;
    }

    private DecalFace? Face(int index) =>
        index >= 0 && index < _world.Faces.Count ? _world.Faces[index] : null;

    /// <summary>The walk's state: the ray, the nearest fraction taken, and what the faces that took it reported.</summary>
    private struct Walk(Vector3 start, Vector3 delta)
    {
        public readonly Vector3 Start = start;
        public readonly Vector3 Delta = delta;
        public float Best = 1f;
        public Vector3 Light;
        public float S;
        public float T;
    }
}
