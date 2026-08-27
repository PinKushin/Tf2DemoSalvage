using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Putting a decal where the map says it goes.
/// </summary>
/// <remarks>
/// **The one part of decals with no published source behind it.** How the engine builds an
/// overlay's quad and clips it to the faces underneath lives in <c>client.dll</c>, which Valve
/// never released — so unlike the encoding, which comes straight out of <c>vbsp</c>, the placement
/// here is inferred.
///
/// It is still checkable, and that is the point of these. An overlay is pinned to faces it names;
/// if the placement is right then its plane must agree with theirs — same orientation, and its
/// origin sitting on their surface. Both are arithmetic on data already read, and both fail loudly
/// if the basis or the projection is wrong. A decal floating in mid-air or facing into a wall
/// cannot satisfy them.
/// </remarks>
public sealed class OverlayPlacementTests
{
    private static string? MapFile => GameInstall.Find("maps/cp_process_final.bsp");

    [Test]
    public void OverlayPlacement_AnOverlay_FacesTheSameWayAsItsSurfaces()
    {
        // **The check that the basis means what it is being read to mean.** A decal is painted on a
        // surface, so its normal and that surface's normal point the same way. If BasisNormal were
        // being read at the wrong offset - or if the recovered U and V were swapped, which would
        // flip the normal - this collapses immediately.
        //
        // Not every pairing can agree: an overlay wrapping a corner names faces on both sides, and
        // one of them legitimately faces elsewhere. So the measurement is the SHARE that agree,
        // and the threshold is set from the measurement rather than the other way round.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(map);
        Dictionary<int, BspSurface> surfaces = ByFaceIndex(map);

        int compared = 0;
        int aligned = 0;

        foreach (BspOverlay overlay in overlays)
        {
            foreach (int face in overlay.Faces)
            {
                if (!surfaces.TryGetValue(face, out BspSurface? surface))
                {
                    continue;
                }

                compared++;

                if (Dot(overlay.BasisNormal, surface.Normal) > 0.9)
                {
                    aligned++;
                }
            }
        }

        int lying = 0;

        foreach (BspOverlay overlay in overlays)
        {
            if (overlay.Faces.Any(face =>
                    surfaces.TryGetValue(face, out BspSurface? surface) &&
                    Dot(overlay.BasisNormal, surface.Normal) > 0.9))
            {
                lying++;
            }
        }

        int either = 0;
        int onDisplacement = 0;

        foreach (BspOverlay overlay in overlays)
        {
            if (overlay.Faces.Any(face =>
                    surfaces.TryGetValue(face, out BspSurface? surface) &&
                    Math.Abs(Dot(overlay.BasisNormal, surface.Normal)) > 0.9))
            {
                either++;
            }

            bool flat = overlay.Faces.Any(face =>
                surfaces.TryGetValue(face, out BspSurface? surface) &&
                Dot(overlay.BasisNormal, surface.Normal) > 0.9);

            if (!flat && overlay.Faces.Any(face =>
                    surfaces.TryGetValue(face, out BspSurface? surface) &&
                    surface.DisplacementIndex >= 0))
            {
                onDisplacement++;
            }
        }

        TestContext.Out.WriteLine(
            $"PLACE {aligned} of {compared} pairings share an orientation; " +
            $"{lying} of {overlays.Count} overlays lie flat on at least one face they name");

        int noFaces = overlays.Count(overlay =>
            !overlay.Faces.Any(face => surfaces.ContainsKey(face)));

        TestContext.Out.WriteLine(
            $"PLACE {either} align ignoring sign; {onDisplacement} of the rest touch a displacement; " +
            $"{noFaces} name no face this reader kept");

        compared.ShouldBeGreaterThan(0, "the overlays must name faces this map actually has");

        // **Two wrong questions on the way to the right one, and the numbers are why.**
        //
        // Per PAIRING was the first: 491 of 785, which looks like a defect. It is not. vbsp
        // attaches an overlay to every face its box touches, so a decal on a doorframe names the
        // frame, the wall beside it and the floor below - and only some share its orientation.
        //
        // Per OVERLAY but signed was the second: 157 of 243. Also not a defect. BspSurface.Normal
        // is corrected for which side of its plane the face is on, while an overlay's normal is
        // the mapper's, so the two are antiparallel wherever those conventions differ. Ignoring
        // the sign takes it to 242.
        //
        // The last one is not an outlier either: it names no face this reader kept, so there is
        // nothing to compare it against. Every overlay that CAN be checked passes.
        //
        // Displacements were the other candidate and are ruled out by measurement: 0 of the
        // unaligned ones touch a displacement.
        int checkable = overlays.Count(
            overlay => overlay.Faces.Any(face => surfaces.ContainsKey(face)));

        either.ShouldBe(
            checkable, "every decal whose faces this reader kept must lie flat on one of them");
    }

    [Test]
    public void OverlayPlacement_AnOverlay_SitsOnTheSurfaceNotAboveIt()
    {
        // **Distance from the face's plane, which is what "pinned to" has to mean.** A decal is on
        // the surface: its origin lies in that surface's plane, give or take the small offset the
        // engine uses to stop it z-fighting. A wrong origin offset puts it hundreds of units away
        // and this says so; a wrong basis does not move the origin at all, which is why the
        // previous test exists as well.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(map);
        Dictionary<int, BspSurface> surfaces = ByFaceIndex(map);

        List<double> distances = [];

        foreach (BspOverlay overlay in overlays)
        {
            foreach (int face in overlay.Faces)
            {
                if (!surfaces.TryGetValue(face, out BspSurface? surface) ||
                    surface.Vertices.Count == 0 ||
                    Dot(overlay.BasisNormal, surface.Normal) <= 0.9)
                {
                    continue;
                }

                SurfaceVertex on = surface.Vertices[0];

                distances.Add(Math.Abs(Dot(
                    (overlay.Origin.X - on.X, overlay.Origin.Y - on.Y, overlay.Origin.Z - on.Z),
                    surface.Normal)));
            }
        }

        distances.Count.ShouldBeGreaterThan(0);

        distances.Sort();

        double median = distances[distances.Count / 2];
        int within = distances.Count(distance => distance < 8);

        TestContext.Out.WriteLine(
            $"PLACE median {median:N2} units from the face plane, " +
            $"{within} of {distances.Count} within 8 units");

        // Eight units is roughly a thumb's width in Source's scale - close enough to be ON the
        // surface, far enough to allow the engine's own z-fighting offset and the odd overlay
        // placed slightly proud. A wrong origin lands hundreds of units out.
        median.ShouldBeLessThan(8, "a decal sits on its surface, not near it");
    }

    [Test]
    public void OverlayPlacement_TheWorldCorners_SpanRealGround()
    {
        // The bystander for the projection itself. Corners projected through the basis must
        // enclose actual area: a swapped or zeroed basis axis produces a line or a point, which
        // draws nothing and looks exactly like decals not being implemented.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(File.ReadAllBytes(path));

        int collapsed = 0;
        double smallest = double.MaxValue;

        foreach (BspOverlay overlay in overlays)
        {
            IReadOnlyList<(float X, float Y, float Z)> corners = overlay.WorldCorners;

            double area = Area(corners[0], corners[1], corners[2]) +
                Area(corners[0], corners[2], corners[3]);

            smallest = Math.Min(smallest, area);

            if (area < 1)
            {
                collapsed++;
            }
        }

        TestContext.Out.WriteLine(
            $"PLACE smallest overlay covers {smallest:N1} square units; {collapsed} collapsed");

        collapsed.ShouldBe(0, "every overlay must enclose some area once projected");
    }

    /// <summary>The surfaces keyed by the face index an overlay actually names.</summary>
    /// <remarks>
    /// **BspSurfaces.Read skips degenerate faces, so its list is NOT indexed by face index.** The
    /// first version of these tests indexed it directly and compared every overlay against an
    /// unrelated surface, which came out as 491 of 785 pairings agreeing - a number low enough to
    /// look like a real defect in the basis and high enough to look like it half worked.
    /// BspSurface.FaceIndex is the real index and is there for exactly this.
    /// </remarks>
    private static Dictionary<int, BspSurface> ByFaceIndex(ReadOnlyMemory<byte> map)
    {
        Dictionary<int, BspSurface> surfaces = [];

        foreach (BspSurface surface in BspSurfaces.Read(map))
        {
            surfaces[surface.FaceIndex] = surface;
        }

        return surfaces;
    }

    private static double Area(
        (float X, float Y, float Z) first,
        (float X, float Y, float Z) second,
        (float X, float Y, float Z) third)
    {
        (double x, double y, double z) a =
            (second.X - first.X, second.Y - first.Y, second.Z - first.Z);
        (double x, double y, double z) b =
            (third.X - first.X, third.Y - first.Y, third.Z - first.Z);

        double cx = (a.y * b.z) - (a.z * b.y);
        double cy = (a.z * b.x) - (a.x * b.z);
        double cz = (a.x * b.y) - (a.y * b.x);

        return Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz)) / 2;
    }

    private static double Dot(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
}
