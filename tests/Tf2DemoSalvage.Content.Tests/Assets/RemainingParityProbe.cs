using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What the renderer still does not do, weighted by how much of the map asks for it.
/// </summary>
/// <remarks>
/// **The map states the remaining work, so the list is finite and measurable.** Every material
/// names the features it wants; weighting by the world area its surfaces actually cover is what
/// separates a key used by sixty small decals from one covering a third of the map.
///
/// This is the method that put <c>tools/toolsblack</c> at 4.8 million units when a brightness
/// ranking had ranked a 4,096-pixel tool texture first, and it is what said <c>$bumpmap</c> was
/// worth more than <c>$detail</c> before either was written.
/// </remarks>
public sealed class RemainingParityProbe
{
    private static string? MapFile
    {
        get
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("TF2_FOLDER"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string map = Path.Combine(root, "maps", "cp_process_final.bsp");

                if (File.Exists(map))
                {
                    return map;
                }
            }

            return null;
        }
    }

    [Test]
    public void MaterialFeatureParity_TheMap_IsReported()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        string tf = Path.GetDirectoryName(Path.GetDirectoryName(path))!;

        ReadOnlyMemory<byte> bytes = File.ReadAllBytes(path);
        PakFile pak = PakFile.ReadFrom(bytes);

        List<VpkArchive> archives = [.. new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" }
            .Select(name => Path.Combine(tf, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        byte[]? Find(string file)
        {
            byte[]? found = pak.ReadFile(file);

            foreach (VpkArchive archive in archives)
            {
                found ??= archive.ReadFile(file);
            }

            return found;
        }

        // World area per material, from the faces themselves. A face is convex, so a fan from its
        // first corner gives its area exactly.
        Dictionary<int, double> areaByMaterial = [];

        foreach (BspSurface surface in BspSurfaces.Read(bytes))
        {
            if (!surface.IsVisible || surface.Vertices.Count < 3)
            {
                continue;
            }

            double area = 0;

            for (int index = 1; index + 1 < surface.Vertices.Count; index++)
            {
                SurfaceVertex first = surface.Vertices[0];
                SurfaceVertex second = surface.Vertices[index];
                SurfaceVertex third = surface.Vertices[index + 1];

                double ax = second.X - first.X, ay = second.Y - first.Y, az = second.Z - first.Z;
                double bx = third.X - first.X, by = third.Y - first.Y, bz = third.Z - first.Z;

                double cx = (ay * bz) - (az * by);
                double cy = (az * bx) - (ax * bz);
                double cz = (ax * by) - (ay * bx);

                area += Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz)) / 2;
            }

            areaByMaterial[surface.MaterialIndex] =
                areaByMaterial.GetValueOrDefault(surface.MaterialIndex) + area;
        }

        string[] keys =
        [
            "$bumpmap", "$detail", "$basetexture2", "$envmap", "$translucent", "$alphatest",
            "$selfillum", "$additive", "$phong", "$surfaceprop", "$nocull", "$decal",
        ];

        Dictionary<string, (int Count, double Area)> byKey = [];
        Dictionary<string, (int Count, double Area)> byShader = [];

        IReadOnlyList<BspMaterial> materials = [.. BspMaterials.Read(bytes)];

        for (int index = 0; index < materials.Count; index++)
        {
            if (Find("materials/" + materials[index].Name + ".vmt") is not { } vmt)
            {
                continue;
            }

            double area = areaByMaterial.GetValueOrDefault(index);

            VmtMaterial material = VmtMaterial.Parse(vmt);

            if (material.IsPatch && material.Include is { } include && Find(include) is { } based)
            {
                material = VmtMaterial.ApplyPatch(material, VmtMaterial.Parse(based));
            }

            (int Count, double Area) shader = byShader.GetValueOrDefault(material.Shader);
            byShader[material.Shader] = (shader.Count + 1, shader.Area + area);

            foreach (string key in keys)
            {
                if (material.Value(key) is null)
                {
                    continue;
                }

                (int Count, double Area) seen = byKey.GetValueOrDefault(key);
                byKey[key] = (seen.Count + 1, seen.Area + area);
            }
        }

        TestContext.Out.WriteLine("=== shaders by drawn area ===");

        foreach ((string name, (int count, double area)) in byShader.OrderByDescending(e => e.Value.Area))
        {
            TestContext.Out.WriteLine($"PARITY {area,14:N0}  {count,4}  {name}");
        }

        TestContext.Out.WriteLine("=== material keys by drawn area ===");

        foreach ((string name, (int count, double area)) in byKey.OrderByDescending(e => e.Value.Area))
        {
            TestContext.Out.WriteLine($"PARITY {area,14:N0}  {count,4}  {name}");
        }
    }
}
