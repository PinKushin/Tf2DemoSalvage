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
            byte[]? vmt = pak.ReadFile("materials/" + materials[index].Name + ".vmt")
                ?? archives.Read("materials/" + materials[index].Name + ".vmt");

            if (vmt is not null && VmtMaterial.Parse(vmt).Shader.Equals(arguments[1], StringComparison.OrdinalIgnoreCase))
            {
                wanted.Add(index);
                output.WriteLine($"material {index}: {materials[index].Name}");
            }
        }

        // The control: how many faces the map has at all, so an empty answer is told apart from an unread map.
        output.WriteLine($"{surfaces.Count} faces, {models.Count} models, sky area {skyArea}");

        foreach (BspSurface surface in surfaces.Where(surface => wanted.Contains(surface.MaterialIndex) && surface.Vertices.Count >= 3))
        {
            int owner = models.Select((model, index) => (model, index))
                .FirstOrDefault(pair => surface.FaceIndex >= pair.model.FirstFace && surface.FaceIndex < pair.model.FirstFace + pair.model.FaceCount).index;
            double cx = surface.Vertices.Average(vertex => vertex.X);
            double cy = surface.Vertices.Average(vertex => vertex.Y);
            double cz = surface.Vertices.Average(vertex => vertex.Z);
            // A brush entity's faces are in its own frame when it has an origin: place them where the entity stands.
            BspEntity? entity = owner > 0
                ? level.Entities.FirstOrDefault(candidate => candidate.TryGetValue("model", out string model) && model == "*" + owner.ToString(CultureInfo.InvariantCulture))
                : null;

            if (entity is not null && entity.TryGetValue("origin", out string originText) &&
                originText.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 3 } parts)
            {
                cx += double.Parse(parts[0], CultureInfo.InvariantCulture);
                cy += double.Parse(parts[1], CultureInfo.InvariantCulture);
                cz += double.Parse(parts[2], CultureInfo.InvariantCulture);
            }

            string who = owner == 0 ? "world" : "no entity";

            if (entity is not null)
            {
                who = string.Join(" ", entity.Values.Select(pair => $"{pair.Key}={pair.Value}"));
            }
            int area = level.Leaves is { } leaves ? leaves.AreaAt((float)cx, (float)cy, (float)cz) : -1;
            (float nx, float ny, float nz) = surface.Normal;

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
