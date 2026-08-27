using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Does an overlay's quad actually span the faces it names?
/// </summary>
/// <remarks>
/// **The measurement that separates two explanations for a broken wall stripe**, and they need
/// opposite fixes.
///
/// The owner reports the red and blue stripes on cp_process arriving as trapezoid segments with
/// gaps between them, on a band that should run most of the way across the map. After removing the
/// orientation filter the builder attempts all 634 named faces and 428 produce a fragment, so 206
/// clip away to nothing. Either:
///
/// - the quad is RIGHT and those faces genuinely fall outside it, in which case the gaps are a
///   clipping fault; or
/// - the quad is TOO SMALL — read wrong, scaled wrong, or built from the wrong corners — in which
///   case the clipping is fine and the input is not.
///
/// Comparing the quad's own length along BasisU against the extent of the faces it names tells them
/// apart. vbsp only lists faces generated from sides the mapper assigned, so a quad covering its
/// list is the expected shape; a quad a fraction of the list's extent is a reader defect.
/// </remarks>
public sealed class OverlayQuadExtentProbe
{
    /// <summary>The authored f12 map, when it has been placed in the install.</summary>
    private static string? MapPath => GameInstall.Find("maps/cp_process_f12.bsp");

    [Test]
    public void OverlayQuads_AgainstTheFacesTheyName_AreReported()
    {
        if (!File.Exists(MapPath))
        {
            Assert.Ignore("cp_process_f12.bsp is not on this machine.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(MapPath);

        Dictionary<int, BspSurface> byFace = [];

        foreach (BspSurface surface in BspSurfaces.Read(bytes))
        {
            byFace[surface.FaceIndex] = surface;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(bytes);
        IReadOnlyList<BspMaterial> materials = [.. BspMaterials.Read(bytes)];

        int reported = 0;

        foreach (BspOverlay overlay in overlays)
        {
            string name = overlay.MaterialIndex >= 0 && overlay.MaterialIndex < materials.Count
                ? materials[overlay.MaterialIndex].Name
                : string.Empty;

            if (!name.Contains("stripe", StringComparison.OrdinalIgnoreCase) || reported >= 10)
            {
                continue;
            }

            IReadOnlyList<(float X, float Y, float Z)> quad = overlay.WorldCorners;

            // The quad's own size, measured along the overlay's own axes.
            float quadAlongU = Spread(quad, overlay.BasisU);
            float quadAlongV = Spread(quad, overlay.BasisV);

            // The same measurement over every vertex of every face the overlay names.
            List<(float X, float Y, float Z)> named = [];

            foreach (int face in overlay.Faces)
            {
                if (byFace.TryGetValue(face, out BspSurface? piece))
                {
                    named.AddRange(piece.Vertices.Select(v => (v.X, v.Y, v.Z)));
                }
            }

            if (named.Count == 0)
            {
                continue;
            }

            float facesAlongU = Spread(named, overlay.BasisU);
            float facesAlongV = Spread(named, overlay.BasisV);

            TestContext.Out.WriteLine(
                $"QUAD {name} #{overlay.Id}: {overlay.Faces.Count} faces; " +
                $"quad {quadAlongU:0}x{quadAlongV:0} along U:V, " +
                $"named faces span {facesAlongU:0}x{facesAlongV:0}, " +
                $"U ratio {(facesAlongU > 0 ? quadAlongU / facesAlongU : 0):0.00}");

            reported++;
        }

        reported.ShouldBeGreaterThan(0, "no stripe overlay was measured");
    }

    /// <summary>How far a set of points spreads along one axis.</summary>
    private static float Spread(
        IReadOnlyList<(float X, float Y, float Z)> points, (float X, float Y, float Z) axis)
    {
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        foreach ((float x, float y, float z) in points)
        {
            float at = (x * axis.X) + (y * axis.Y) + (z * axis.Z);

            lowest = Math.Min(lowest, at);
            highest = Math.Max(highest, at);
        }

        return highest - lowest;
    }
}
