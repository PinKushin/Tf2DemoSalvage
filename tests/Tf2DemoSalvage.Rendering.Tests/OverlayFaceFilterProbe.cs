using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// How many of the faces an overlay names are refused by the renderer's orientation test.
/// </summary>
/// <remarks>
/// **The measurement B68's own lesson demands before another renderer guess.** That entry closed
/// with the title "an overlay's face list is what to CLIP against, not a list to choose from", and
/// records four wrong renderer theories killed by measurements. `MapWorld` still applies
/// <c>|dot(overlay.BasisNormal, face.Normal)| &gt; 0.9</c> before clipping, which chooses.
///
/// vbsp puts no such condition on the list. <c>Overlay_AddFaceToLists</c>
/// (<c>utils/vbsp/overlay.cpp:171</c>) adds a face because it came from a SIDE the mapper assigned
/// the overlay to, and tests nothing about its normal:
///
/// <code>
/// mapoverlay_t *pMapOverlay = &amp;g_aMapOverlays.Element( pSide->aOverlayIds[iOverlayId] );
/// if ( pMapOverlay )
/// {
///     if( pMapOverlay->aFaceList.Find( iFace ) == -1 )
///     {
///         pMapOverlay->aFaceList.AddToTail( iFace );
///     }
/// }
/// </code>
///
/// So the list is authoritative. What is NOT yet established is whether the filter actually costs
/// anything on a real map — a filter that refuses nothing is a tidiness problem, and one that
/// refuses hundreds of faces is the defect the owner is looking at. This probe answers that, and
/// prints the dot products of what it refuses: values near zero are genuinely perpendicular walls
/// the mapper chose on purpose, values near the threshold would be a different story.
/// </remarks>
public sealed class OverlayFaceFilterProbe
{
    private const string MapPath =
        "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/maps/cp_process_f12.bsp";

    /// <summary>The renderer's current threshold, copied so this measures what ships.</summary>
    private const float Threshold = 0.9f;

    [Test]
    public void OverlayFaces_TheOrientationFilter_ReportsWhatItRefuses()
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

        int named = 0;
        int kept = 0;
        int refused = 0;
        int missing = 0;
        int overlaysLosingFaces = 0;
        int overlaysLosingEverything = 0;

        List<float> refusedDots = [];
        Dictionary<string, int> refusedByMaterial = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspOverlay overlay in overlays)
        {
            int keptHere = 0;
            int refusedHere = 0;

            foreach (int face in overlay.Faces)
            {
                named++;

                if (!byFace.TryGetValue(face, out BspSurface? piece))
                {
                    missing++;
                    continue;
                }

                float dot = Math.Abs(
                    (overlay.BasisNormal.X * piece.Normal.X) +
                    (overlay.BasisNormal.Y * piece.Normal.Y) +
                    (overlay.BasisNormal.Z * piece.Normal.Z));

                if (dot > Threshold)
                {
                    kept++;
                    keptHere++;
                    continue;
                }

                refused++;
                refusedHere++;
                refusedDots.Add(dot);
            }

            if (refusedHere > 0)
            {
                overlaysLosingFaces++;

                string name = overlay.MaterialIndex >= 0 && overlay.MaterialIndex < materials.Count
                    ? materials[overlay.MaterialIndex].Name
                    : $"material {overlay.MaterialIndex}";

                refusedByMaterial[name] = refusedByMaterial.GetValueOrDefault(name) + refusedHere;

                if (keptHere == 0)
                {
                    overlaysLosingEverything++;
                }
            }
        }

        TestContext.Out.WriteLine(
            $"FILTER {overlays.Count} overlays name {named} faces: {kept} kept, {refused} refused, " +
            $"{missing} not in the surface list");

        TestContext.Out.WriteLine(
            $"FILTER {overlaysLosingFaces} overlays lose at least one face; " +
            $"{overlaysLosingEverything} lose every face and draw nothing at all");

        if (refusedDots.Count > 0)
        {
            refusedDots.Sort();

            TestContext.Out.WriteLine(
                $"FILTER refused |dot|: min {refusedDots[0]:0.####}, " +
                $"median {refusedDots[refusedDots.Count / 2]:0.####}, " +
                $"max {refusedDots[^1]:0.####}");

            // Near zero means a perpendicular wall the mapper chose deliberately. Near the
            // threshold would mean the number is merely mistuned, which is a different fix.
            TestContext.Out.WriteLine(
                $"FILTER refused within 0.1 of the threshold: " +
                $"{refusedDots.Count(dot => dot > Threshold - 0.1f)}");

            // **The distribution decides how much removing the filter actually recovers**, and the
            // two ends need different treatment. A face at 45 degrees projects onto its own plane
            // perfectly well; one at 90 degrees projects to a line, so it draws nothing whatever
            // the filter says. Bucketed rather than averaged: a mean of 0.7 could be all-45 or
            // half-perpendicular-half-coplanar, and those are different findings.
            foreach ((string label, Func<float, bool> inBucket) in new (string, Func<float, bool>)[]
            {
                ("perpendicular  |dot| < 0.05", dot => dot < 0.05f),
                ("steep          0.05 - 0.35", dot => dot is >= 0.05f and < 0.35f),
                ("45 degree-ish  0.35 - 0.80", dot => dot is >= 0.35f and < 0.80f),
                ("near-coplanar  0.80 - 0.90", dot => dot >= 0.80f),
            })
            {
                TestContext.Out.WriteLine(
                    $"FILTER   {label}: {refusedDots.Count(inBucket)} faces");
            }
        }

        foreach ((string name, int count) in
            refusedByMaterial.OrderByDescending(pair => pair.Value).Take(12))
        {
            TestContext.Out.WriteLine($"FILTER   {name}: {count} faces refused");
        }

        // The instrument before its answer: no overlays, or none naming faces, would make every
        // number above a zero that means nothing.
        overlays.Count.ShouldBeGreaterThan(0);
        named.ShouldBeGreaterThan(0);
    }
}
