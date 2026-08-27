using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// How much a map's geometry changed between eras, read with this project's own parser.
/// </summary>
/// <remarks>
/// **Kept because a file hash cannot answer this.** All eight of the 2008 build's maps differ from
/// their modern counterparts by hash, and cp_badlands alone went 65 MB to 26 MB - which is lump
/// compression arriving, not two thirds of the map being deleted. Only reading the geometry
/// separates a recompile from a rebuild.
///
/// Findings are written up in docs/findings/10-maps.md; the short version is that ctf_well lost a
/// fifth of its faces, cp_granary gained 7%, and three maps grew identical bounds - the signature
/// of a 3D skybox added after 2008.
///
/// Reports rather than asserts, deliberately. There is no correct answer for how much a map should
/// change; the value is the measurement, and turning it into a threshold would invent one.
/// </remarks>
public sealed class EraGeometryProbe
{
    [Test]
    public void EraGeometry_TheCorpus_IsReported()
    {
        string old = "F:/tf2-builds/tf2-2008/tf/maps";
        string now = Path.Combine(GameInstall.Require(), "maps");

        if (!Directory.Exists(old) || !Directory.Exists(now))
        {
            Assert.Ignore("builds missing");
            return;
        }

        foreach (string file in Directory.GetFiles(old, "*.bsp").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            string modern = Path.Combine(now, name);

            if (!File.Exists(modern))
            {
                continue;
            }

            try
            {
                (int faces, int verts, string bounds) a = Measure(File.ReadAllBytes(file));
                (int faces, int verts, string bounds) b = Measure(File.ReadAllBytes(modern));

                TestContext.Out.WriteLine(
                    $"ERA {name,-22} faces {a.faces,6} -> {b.faces,6}   verts {a.verts,7} -> {b.verts,7}");
                TestContext.Out.WriteLine($"ERA     bounds {a.bounds}  ->  {b.bounds}");
            }
            catch (Exception failure) when (failure is InvalidDataException or IOException or ArgumentException)
            {
                TestContext.Out.WriteLine($"ERA {name,-22} FAILED {failure.GetType().Name}: {failure.Message}");
            }
        }

        Assert.Pass();
    }

    private static (int Faces, int Vertices, string Bounds) Measure(ReadOnlyMemory<byte> map)
    {
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int vertices = 0;

        foreach (BspSurface surface in surfaces)
        {
            vertices += surface.Vertices.Count;

            foreach (SurfaceVertex vertex in surface.Vertices)
            {
                minX = Math.Min(minX, vertex.X);
                minY = Math.Min(minY, vertex.Y);
                maxX = Math.Max(maxX, vertex.X);
                maxY = Math.Max(maxY, vertex.Y);
            }
        }

        return (surfaces.Count, vertices, $"[{minX:F0},{minY:F0} {maxX:F0},{maxY:F0}]");
    }
}
