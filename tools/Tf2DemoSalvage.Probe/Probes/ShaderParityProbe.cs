using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What the map's materials ask for that this renderer does not yet do.
/// </summary>
/// <remarks>
/// **Parity with the engine is a finite list, and this is how to find it.** Rather than guess which
/// Source features matter, count what the map's own materials declare, weighted by the world area
/// they cover - the same method that found tools/toolsblack covering 4.8 million units while a
/// brightness ranking put a 4,096 pixel tool texture at the top.
///
/// **A probe since D213**; it was a test in `Rendering.Tests` that asserted only that its report was not empty. The
/// control is the shader table: a map with brushwork lists <c>LightmappedGeneric</c>.
/// </remarks>
public sealed class ShaderParityProbe : IProbe
{
    private static readonly string[] Features =
    [
        "$bumpmap", "$envmap", "$detail", "$selfillum", "$phong", "$translucent",
        "$alphatest", "$additive", "$basetexture2", "$surfaceprop",
    ];

    /// <inheritdoc/>
    public string Name => "shader-parity";

    /// <inheritdoc/>
    public string Summary => "a map's world materials by shader and by feature key, weighted by face area: shader-parity [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string name = arguments.Count > 0 ? arguments[0] : "cp_process_f12";

        if (locator.Find(name) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map named '{name}', or no game.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        GameArchives archives = GameArchives.Open(folder);
        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(map);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        Dictionary<int, double> areaByMaterial = [];

        foreach (BspSurface surface in surfaces.Where(surface => surface.Vertices.Count >= 3))
        {
            areaByMaterial[surface.MaterialIndex] =
                areaByMaterial.GetValueOrDefault(surface.MaterialIndex) + Area(surface);
        }

        Dictionary<string, (int Count, double Area)> byShader = [];
        Dictionary<string, (int Count, double Area)> byFeature = [];

        for (int index = 0; index < materials.Count; index++)
        {
            byte[]? vmt = pak.ReadFile("materials/" + materials[index].Name + ".vmt")
                ?? archives.Read("materials/" + materials[index].Name + ".vmt");

            if (vmt is null)
            {
                continue;
            }

            VmtMaterial material;

            try
            {
                material = VmtMaterial.Parse(vmt);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            double area = areaByMaterial.GetValueOrDefault(index);

            Add(byShader, material.Shader, area);

            foreach (string key in Features.Where(key => material.Value(key) is not null))
            {
                Add(byFeature, key, area);
            }
        }

        output.WriteLine($"{name}: {materials.Count} materials; shaders by area");

        foreach ((string shader, (int count, double area)) in byShader.OrderByDescending(e => e.Value.Area))
        {
            output.WriteLine($"  {area,16:N0}  {count,4} materials  {shader}");
        }

        output.WriteLine("features by area");

        foreach ((string feature, (int count, double area)) in byFeature.OrderByDescending(e => e.Value.Area))
        {
            output.WriteLine($"  {area,16:N0}  {count,4} materials  {feature}");
        }
    }

    private static void Add(Dictionary<string, (int Count, double Area)> into, string key, double area)
    {
        (int count, double total) = into.GetValueOrDefault(key);

        into[key] = (count + 1, total + area);
    }

    private static double Area(BspSurface surface)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (SurfaceVertex vertex in surface.Vertices)
        {
            minX = Math.Min(minX, vertex.X);
            minY = Math.Min(minY, vertex.Y);
            maxX = Math.Max(maxX, vertex.X);
            maxY = Math.Max(maxY, vertex.Y);
        }

        return (maxX - minX) * (maxY - minY);
    }
}
