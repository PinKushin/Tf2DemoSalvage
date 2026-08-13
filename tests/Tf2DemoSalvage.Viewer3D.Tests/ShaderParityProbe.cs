using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What the map's materials ask for that this renderer does not yet do.
/// </summary>
/// <remarks>
/// **Parity with the engine is a finite list, and this is how to find it.** Rather than guess which
/// Source features matter, count what the map's own materials declare, weighted by the world area
/// they cover - the same method that found tools/toolsblack covering 4.8 million units while a
/// brightness ranking put a 4,096 pixel tool texture at the top.
/// </remarks>
public sealed class ShaderParityProbe
{
    [Test]
    public void WhatDoTheMapsMaterialsAskFor()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
        string mapPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!Directory.Exists(tf) || !File.Exists(mapPath))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        GameArchives archives = GameArchives.Open(tf);
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

            foreach (string key in new[]
            {
                "$bumpmap", "$envmap", "$detail", "$selfillum", "$phong", "$translucent",
                "$alphatest", "$additive", "$basetexture2", "$surfaceprop",
            })
            {
                if (material.Value(key) is not null)
                {
                    Add(byFeature, key, area);
                }
            }
        }

        TestContext.Out.WriteLine("PARITY shaders by area");

        foreach ((string shader, (int count, double area)) in byShader.OrderByDescending(e => e.Value.Area))
        {
            TestContext.Out.WriteLine($"PARITY   {area,16:N0}  {count,4} materials  {shader}");
        }

        TestContext.Out.WriteLine("PARITY features by area");

        foreach ((string feature, (int count, double area)) in byFeature.OrderByDescending(e => e.Value.Area))
        {
            TestContext.Out.WriteLine($"PARITY   {area,16:N0}  {count,4} materials  {feature}");
        }

        byShader.ShouldNotBeEmpty();
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
