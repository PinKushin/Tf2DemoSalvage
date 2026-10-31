using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every brush face whose material uses one shader: where it is, who owns it, and a camera that frames it.</summary>
/// <remarks>
/// **Through the viewer's own readers**: <c>BspSurfaces</c> for the faces (normal corrected for the side), the models
/// lump for which brush model owns a face, and <c>MapLevel</c>'s leaf tree for its AREA, compared with the
/// <c>sky_camera</c>'s area as <c>MapLevel.Culling</c> does to find the 3D skybox room. The material's shader is read
/// from the map's pakfile first and then the game, as <c>shader-parity</c> does. The area printed is the polygon's
/// true area, not a bounding box.
/// </remarks>
public sealed class ShaderFacesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "shader-faces";

    /// <inheritdoc/>
    public string Summary =>
        "brush faces whose material uses a shader, with owner, area, sky-room flag and a framing camera: shader-faces <map> <shader> [distance=200]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        float distance = 200f;

        if (arguments.Count < 2 ||
            (arguments.Count > 2 && !float.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out distance)))
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map named '{arguments[0]}', or no game.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(mapPath);
        ReadOnlyMemory<byte> map = bytes;
        PakFile pak = PakFile.ReadFrom(map);
        GameArchives archives = GameArchives.Open(folder);
        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(map);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);
        IReadOnlyList<BspModel> models = BspModels.Read(map);
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        int skyArea = BspEntities.SkyCamera(level.Entities) is { } sky && level.Leaves is { } tree
            ? tree.AreaAt(sky.Origin.X, sky.Origin.Y, sky.Origin.Z)
            : -1;

        HashSet<int> wanted = [];

        for (int index = 0; index < materials.Count; index++)
        {
            byte[]? vmt = Find(pak, archives, "materials/" + materials[index].Name + ".vmt");

            // Through a Patch's include, as the material system applies it on every load (VmtMaterial.Load, B390).
            if (vmt is not null &&
                VmtMaterial.Load(vmt, path => Find(pak, archives, path)).Shader.Equals(arguments[1], StringComparison.OrdinalIgnoreCase))
            {
                wanted.Add(index);
                output.WriteLine($"material {index}: {materials[index].Name}");
            }
        }

        // The control: how many faces the map has at all, so an empty answer is told apart from an unread map.
        output.WriteLine($"{surfaces.Count} faces, {models.Count} models, sky area {skyArea}");

        foreach (BspSurface surface in surfaces.Where(surface => wanted.Contains(surface.MaterialIndex) && surface.Vertices.Count >= 3))
        {
            // −1 when no model's range holds the face: never defaulted to the world.
            int owner = -1;

            for (int index = 0; index < models.Count; index++)
            {
                if (surface.FaceIndex >= models[index].FirstFace && surface.FaceIndex < models[index].FirstFace + models[index].FaceCount)
                {
                    owner = index;
                    break;
                }
            }

            double cx = surface.Vertices.Average(vertex => vertex.X);
            double cy = surface.Vertices.Average(vertex => vertex.Y);
            double cz = surface.Vertices.Average(vertex => vertex.Z);
            double nx = surface.Normal.X, ny = surface.Normal.Y, nz = surface.Normal.Z;

            // A brush entity's faces are in its own frame: turned by its `angles`, then placed at its `origin`.
            BspEntity? entity = owner > 0
                ? level.Entities.FirstOrDefault(candidate => candidate.TryGetValue("model", out string model) && model == "*" + owner.ToString(CultureInfo.InvariantCulture))
                : null;

            if (entity is not null && Vector(entity, "angles") is { } angles)
            {
                (cx, cy, cz) = Rotate(angles, cx, cy, cz);
                (nx, ny, nz) = Rotate(angles, nx, ny, nz);
            }

            if (entity is not null && Vector(entity, "origin") is { } origin)
            {
                cx += origin.X;
                cy += origin.Y;
                cz += origin.Z;
            }

            string who = owner switch
            {
                0 => "world",
                < 0 => "in no model's face range",
                _ => "no entity names it",
            };

            if (entity is not null)
            {
                who = string.Join(" ", entity.Values.Select(pair => $"{pair.Key}={pair.Value}"));
            }

            // The area one unit off the face along its normal, not on the plane, where a leaf boundary may lie.
            int area = level.Leaves is { } leaves ? leaves.AreaAt((float)(cx + nx), (float)(cy + ny), (float)(cz + nz)) : -1;

            // Back along the normal by the distance, looking at the centre.
            double ex = cx + (nx * distance), ey = cy + (ny * distance), ez = cz + (nz * distance);
            double dx = cx - ex, dy = cy - ey, dz = cz - ez;
            double yaw = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            double pitch = -Math.Atan2(dz, Math.Sqrt((dx * dx) + (dy * dy))) * 180.0 / Math.PI;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"face {surface.FaceIndex} material {surface.MaterialIndex} model {owner} ({who}) area-id {area}{(area == skyArea && skyArea >= 0 ? " SKY" : string.Empty)} flags {surface.Flags} disp {surface.DisplacementIndex} polygon {Area(surface):0} at ({cx:0} {cy:0} {cz:0}) normal ({nx:0.00} {ny:0.00} {nz:0.00})  TF2VIEW_CAMERA=\"{ex:0} {ey:0} {ez:0} {pitch:0} {yaw:0}\""));
        }
    }

    private static byte[]? Find(PakFile pak, GameArchives archives, string path) => pak.ReadFile(path) ?? archives.Read(path);

    /// <summary>An entity's three-number key (origin, angles), or null when absent or unreadable.</summary>
    private static (double X, double Y, double Z)? Vector(BspEntity entity, string key)
    {
        if (!entity.TryGetValue(key, out string text) ||
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not { Length: 3 } parts ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
        {
            return null;
        }

        return (a, b, c);
    }

    /// <summary>A vector turned by Source angles (pitch, yaw, roll), as <c>AngleMatrix</c> builds the rotation (mathlib.cpp).</summary>
    private static (double X, double Y, double Z) Rotate((double X, double Y, double Z) angles, double x, double y, double z)
    {
        double p = angles.X * Math.PI / 180.0, w = angles.Y * Math.PI / 180.0, r = angles.Z * Math.PI / 180.0;
        double sp = Math.Sin(p), cp = Math.Cos(p), sy = Math.Sin(w), cy = Math.Cos(w), sr = Math.Sin(r), cr = Math.Cos(r);

        // Columns: forward, left, up (AngleMatrix's matrix[.][0..2]).
        double m00 = cp * cy, m01 = (sr * sp * cy) - (cr * sy), m02 = (cr * sp * cy) + (sr * sy);
        double m10 = cp * sy, m11 = (sr * sp * sy) + (cr * cy), m12 = (cr * sp * sy) - (sr * cy);
        double m20 = -sp, m21 = sr * cp, m22 = cr * cp;

        return ((m00 * x) + (m01 * y) + (m02 * z), (m10 * x) + (m11 * y) + (m12 * z), (m20 * x) + (m21 * y) + (m22 * z));
    }

    /// <summary>The polygon's area, by the cross products of its fan.</summary>
    private static double Area(BspSurface surface)
    {
        SurfaceVertex a = surface.Vertices[0];
        double x = 0, y = 0, z = 0;

        for (int corner = 1; corner + 1 < surface.Vertices.Count; corner++)
        {
            SurfaceVertex b = surface.Vertices[corner];
            SurfaceVertex c = surface.Vertices[corner + 1];
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            double vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;

            x += (uy * vz) - (uz * vy);
            y += (uz * vx) - (ux * vz);
            z += (ux * vy) - (uy * vx);
        }

        return Math.Sqrt((x * x) + (y * y) + (z * z)) / 2.0;
    }
}
