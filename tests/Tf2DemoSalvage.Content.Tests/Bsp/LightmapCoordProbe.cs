using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class LightmapCoordProbe
{
    [Test]
    public void LightmapCoords_FacesOutsideTheirOwnLightmap_AreReported()
    {
        string path = GameInstall.RequireFile("maps/cp_process_final.bsp");

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        // Model 0 is worldspawn; every later model is a brush entity - a door, a lift, a control
        // point - drawn without walking the tree. dmodel_t is 48 bytes with firstface at 40.
        BspHeader header = BspHeader.Parse(map.Span);
        ReadOnlySpan<byte> models = BspLumpData.ReadStructures(map, header.Lump(14), 48, "models").Span;
        int worldFaces = BitConverter.ToInt32(models.Slice(44, 4));
        int worldFirst = BitConverter.ToInt32(models.Slice(40, 4));

        TestContext.Out.WriteLine(
            $"COORD worldspawn owns faces {worldFirst}..{worldFirst + worldFaces}, of {surfaces.Count}; " +
            $"{models.Length / 48} models total");

        int outsideEntities = 0;
        int outsideLit = 0;
        int lit = 0;
        IReadOnlyList<BspLightmap> lightmaps = BspLightmaps.Read(map);

        int outside = 0;
        int outsideDisplacements = 0;
        int outsideBrushes = 0;
        int displacements = 0;
        int total = 0;
        double worst = 0;
        int badVertices = 0;
        int vertices = 0;

        foreach (BspSurface surface in surfaces)
        {
            total++;

            bool isLit = surface.FaceIndex < lightmaps.Count && !lightmaps[surface.FaceIndex].IsEmpty;

            if (isLit)
            {
                lit++;
            }

            if (surface.IsDisplacement)
            {
                displacements++;
            }

            bool any = false;

            foreach (SurfaceVertex vertex in surface.Vertices)
            {
                vertices++;

                double over = Math.Max(
                    Math.Max(-vertex.LightU, vertex.LightU - 1),
                    Math.Max(-vertex.LightV, vertex.LightV - 1));

                if (over > 0.001)
                {
                    any = true;
                    badVertices++;
                    worst = Math.Max(worst, over);
                }
            }

            if (any)
            {
                outside++;

                if (surface.IsDisplacement)
                {
                    outsideDisplacements++;
                }
                else
                {
                    outsideBrushes++;
                }

                if (surface.FaceIndex >= worldFirst + worldFaces)
                {
                    outsideEntities++;
                }

                if (isLit)
                {
                    outsideLit++;
                }
            }
        }

        TestContext.Out.WriteLine(
            $"COORD faces {outside}/{total} ({outside * 100.0 / total:F1}%) have a corner outside [0,1]");
        TestContext.Out.WriteLine(
            $"COORD vertices {badVertices}/{vertices} ({badVertices * 100.0 / vertices:F1}%), worst overshoot {worst:F3}");

        TestContext.Out.WriteLine(
            $"COORD of those, {outsideDisplacements} are displacements (of {displacements}) and {outsideBrushes} are brushwork");

        TestContext.Out.WriteLine(
            $"COORD and {outsideEntities} of the {outside} belong to BRUSH ENTITIES, not worldspawn");

        TestContext.Out.WriteLine(
            $"COORD of the {outside}, {outsideLit} are on LIT faces (of {lit} lit) - the rest have no lightmap and land on the white texel by design");

        surfaces.ShouldNotBeEmpty();
    }
}
