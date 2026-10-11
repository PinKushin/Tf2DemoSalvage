using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The world face under one pixel of a capture: its material, its flags, its light styles and its lightmap.</summary>
/// <remarks>
/// **For the golden comparison** (`docs/findings/76-the-golden-comparison.md`): a tile that disagrees with TF2 is a
/// place on a surface, and the first question is which surface. The ray is the capture's own — the camera string the
/// viewer was given, the pixel, the picture's size — and the hit is the map's own trace (`MapLevel.Trace`), so the
/// face named is the brush or terrain face there.
///
/// **What it cannot see is a model.** The trace is the world's: a static prop or an entity in front of the wall is not
/// hit, and the face behind it is reported. `map-near` and `world-near` list what stands near the point printed.
///
/// **The field of view is the horizontal one at 4:3**, as Source states it; a wider picture sees more to each side
/// (Hor+), so 90 on a 16:9 capture is 106.26 across.
///
/// <code>
///   pixel-face cp_process_f12 146 -1831.6 728 -28.7 -36.5 462 150
/// </code>
///
/// The control is the sky or the floor: a pixel of plain floor names a face whose normal is +Z.
/// </remarks>
public sealed class PixelFaceProbe : IProbe
{
    private static readonly string[] Keys =
    [
        "$basetexture", "$bumpmap", "$ssbump", "$envmap", "$envmaptint", "$envmapmask", "$selfillum", "$detail",
        "$reflectivity", "$surfaceprop", "%compilenolight", "%keywords",
    ];

    /// <inheritdoc/>
    public string Name => "pixel-face";

    /// <inheritdoc/>
    public string Summary =>
        "the world face under a capture's pixel, with material, flags, styles and lightmap: " +
        "pixel-face <map> <x y z pitch yaw> <px> <py> [width height] [fov]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count < 8 || locator.Find(arguments[0]) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        Vector3 eye = new(Number(arguments[1]), Number(arguments[2]), Number(arguments[3]));
        float width = arguments.Count > 9 ? Number(arguments[8]) : 1280f;
        float height = arguments.Count > 9 ? Number(arguments[9]) : 720f;
        float fov = arguments.Count > 10 ? Number(arguments[10]) : 90f;
        Vector3 ray = Ray(Number(arguments[4]), Number(arguments[5]), Number(arguments[6]), Number(arguments[7]), width, height, fov);

        byte[] bytes = File.ReadAllBytes(path);
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        Vector3 far = eye + (ray * 16384f);
        float fraction = level.Trace((eye.X, eye.Y, eye.Z), (far.X, far.Y, far.Z), 0f).Fraction;

        if (fraction >= 1f)
        {
            output.WriteLine("The ray leaves the map without striking the world.");
            return;
        }

        Vector3 hit = eye + ((far - eye) * fraction);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"ray ({ray.X:0.0000} {ray.Y:0.0000} {ray.Z:0.0000}) strikes the world at ({hit.X:0.0} {hit.Y:0.0} {hit.Z:0.0}), " +
            $"{Vector3.Distance(eye, hit):0.0} units out"));

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);
        IReadOnlyList<BspFaceLighting> lighting = BspLightmaps.ReadAll(bytes);
        IReadOnlyList<string> materials = [.. BspMaterials.Read(bytes).Select(material => material.Name)];
        PakFile pak = PakFile.ReadFrom(bytes);
        GameArchives? archives = locator.FindGameFolder() is { } folder ? GameArchives.Open(folder) : null;
        int found = 0;

        for (int index = 0; index < surfaces.Count; index++)
        {
            BspSurface face = surfaces[index];

            if (face.IsDisplacement || face.Vertices.Count < 3 || !Contains(face, hit))
            {
                continue;
            }

            found++;

            string material = face.MaterialIndex >= 0 && face.MaterialIndex < materials.Count
                ? materials[face.MaterialIndex]
                : $"material {face.MaterialIndex}";

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"face {face.FaceIndex}: '{material}' normal ({face.Normal.X:0.00} {face.Normal.Y:0.00} {face.Normal.Z:0.00}) " +
                $"flags [{face.Flags}] luxels {face.LuxelWidth}x{face.LuxelHeight} styles {face.Style0} {face.Style1}"));

            if (index < lighting.Count)
            {
                Light(output, lighting[index]);
                AtPoint(output, face, lighting[index], hit);
            }

            if ((pak.ReadFile($"materials/{material}.vmt") ?? archives?.Read($"materials/{material}.vmt")) is { } vmt)
            {
                VmtMaterial parsed = VmtMaterial.Parse(vmt);

                output.WriteLine($"  shader {parsed.Shader}: " + string.Join(
                    "  ", Keys.Where(key => parsed.Value(key) is not null).Select(key => $"{key} '{parsed.Value(key)}'")));
            }
        }

        output.WriteLine($"{found} world faces contain that point (terrain faces are not searched)");
    }

    /// <summary>The face's light as the atlas is handed it: the flat set and, when bumped, the three directional ones.</summary>
    private static void Light(TextWriter output, BspFaceLighting light)
    {
        BspLightmap[] sets = light.IsBumped ? [light.Flat, .. light.Directional] : [light.Flat];
        string[] names = ["flat", "bump 0", "bump 1", "bump 2"];

        for (int set = 0; set < sets.Length; set++)
        {
            ReadOnlySpan<byte> texels = sets[set].Pixels.Span;
            Vector3 sum = Vector3.Zero;
            Vector3 low = new(float.MaxValue);
            Vector3 high = new(float.MinValue);
            int count = 0;

            for (int at = 0; at + 4 <= texels.Length; at += 4)
            {
                // `Load` answers in sample units, where 255 is a light of one.
                (float red, float green, float blue) = BspLightmaps.Load(texels.Slice(at, 4));
                Vector3 luxel = new Vector3(red, green, blue) / 255f;

                sum += luxel;
                low = Vector3.Min(low, luxel);
                high = Vector3.Max(high, luxel);
                count++;
            }

            if (count > 0)
            {
                sum /= count;
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  light {names[set],-6} linear mean ({sum.X:0.000} {sum.Y:0.000} {sum.Z:0.000}) " +
                    $"min ({low.X:0.000} {low.Y:0.000} {low.Z:0.000}) max ({high.X:0.000} {high.Y:0.000} {high.Z:0.000}) over {count} luxels"));
            }
        }
    }

    /// <summary>
    /// The flat light at the point itself: the face's own luxel coordinate there (<see cref="LuxelMapping.Project"/>),
    /// the four luxels about it and their bilinear blend, with luxel centres on the integers as the engine's
    /// <c>+ 0.5</c> puts them.
    /// </summary>
    private static void AtPoint(TextWriter output, BspSurface face, BspFaceLighting light, Vector3 hit)
    {
        int width = face.LuxelWidth;
        int height = face.LuxelHeight;
        ReadOnlyMemory<byte> texels = light.Flat.Pixels;

        if (width <= 0 || height <= 0 || texels.Length < width * height * 4)
        {
            return;
        }

        // The face's own coordinate runs 0 on its first luxel to 1 on its last (`LuxelMapping.Coordinate`).
        (float u, float v) = face.Lighting.Project(hit.X, hit.Y, hit.Z);
        float across = u * (width - 1);
        float down = v * (height - 1);
        int left = Math.Clamp((int)MathF.Floor(across), 0, width - 1);
        int top = Math.Clamp((int)MathF.Floor(down), 0, height - 1);
        int right = Math.Min(left + 1, width - 1);
        int bottom = Math.Min(top + 1, height - 1);
        float fx = Math.Clamp(across - left, 0f, 1f);
        float fy = Math.Clamp(down - top, 0f, 1f);

        Vector3 Luxel(int x, int y)
        {
            (float red, float green, float blue) = BspLightmaps.Load(texels.Span.Slice(((y * width) + x) * 4, 4));

            return new Vector3(red, green, blue) / 255f;
        }

        Vector3 blended = Vector3.Lerp(
            Vector3.Lerp(Luxel(left, top), Luxel(right, top), fx),
            Vector3.Lerp(Luxel(left, bottom), Luxel(right, bottom), fx),
            fy);

        // The control on the coordinate: each corner's own lightmap coordinate, as the vertex carries it to the
        // renderer, beside this projection of the same corner. They are one mapping or the line above means nothing.
        output.WriteLine("  corners, luxel coordinate carried by the vertex | projected here: " + string.Join("  ", face.Vertices.Select(vertex =>
        {
            (float cu, float cv) = face.Lighting.Project(vertex.X, vertex.Y, vertex.Z);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"({vertex.LightU * (width - 1):0.00}, {vertex.LightV * (height - 1):0.00} | {cu * (width - 1):0.00}, {cv * (height - 1):0.00})");
        })));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  at the point: luxel ({across:0.00}, {down:0.00}) of {face.Lighting.Width}x{face.Lighting.Height}; " +
            $"green of the four about it {Luxel(left, top).Y:0.000} {Luxel(right, top).Y:0.000} / " +
            $"{Luxel(left, bottom).Y:0.000} {Luxel(right, bottom).Y:0.000}; bilinear ({blended.X:0.000} {blended.Y:0.000} {blended.Z:0.000})"));
    }

    /// <summary>Whether a point lies on the face's plane and inside its polygon, to within a unit.</summary>
    private static bool Contains(BspSurface face, Vector3 point)
    {
        Vector3 normal = new(face.Normal.X, face.Normal.Y, face.Normal.Z);
        Vector3 first = At(face.Vertices[0]);

        if (MathF.Abs(Vector3.Dot(normal, point - first)) > 1f)
        {
            return false;
        }

        int sign = 0;

        for (int index = 0; index < face.Vertices.Count; index++)
        {
            Vector3 from = At(face.Vertices[index]);
            Vector3 to = At(face.Vertices[(index + 1) % face.Vertices.Count]);
            float side = Vector3.Dot(Vector3.Cross(to - from, point - from), normal);

            if (MathF.Abs(side) < 1f)
            {
                continue;
            }

            int now = Math.Sign(side);

            if (sign != 0 && now != sign)
            {
                return false;
            }

            sign = now;
        }

        return true;
    }

    private static Vector3 At(SurfaceVertex vertex) => new(vertex.X, vertex.Y, vertex.Z);

    /// <summary>The world direction through a pixel: <c>AngleVectors</c>, and a 4:3 horizontal field of view widened to the picture.</summary>
    private static Vector3 Ray(float pitch, float yaw, float pixelX, float pixelY, float width, float height, float fov)
    {
        (float sp, float cp) = MathF.SinCos(float.DegreesToRadians(pitch));
        (float sy, float cy) = MathF.SinCos(float.DegreesToRadians(yaw));

        Vector3 forward = new(cp * cy, cp * sy, -sp);
        Vector3 right = new(sy, -cy, 0f);
        Vector3 up = new(sp * cy, sp * sy, cp);

        float aspect = width / height;
        float tanAcross = MathF.Tan(float.DegreesToRadians(fov) / 2f) * (aspect / (4f / 3f));
        float across = (((pixelX + 0.5f) / width) * 2f) - 1f;
        float down = (((pixelY + 0.5f) / height) * 2f) - 1f;

        return Vector3.Normalize(forward + (right * (across * tanAcross)) - (up * (down * tanAcross / aspect)));
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
