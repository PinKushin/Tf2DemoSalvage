using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Reading a map's decals.
/// </summary>
/// <remarks>
/// **A decal is an overlay: a quad pinned to the faces underneath it.** Signs, scorch marks,
/// arrows painted on a floor, the numbers on a control point. 243 of them on cp_process_final, and
/// none currently drawn — which is why the map's floors are bare where the real one has markings.
///
/// The struct is confirmed by arithmetic before any of it is read: the field order from
/// <c>bsplib.cpp</c>'s byteswap descriptor sums to exactly 352 bytes, and the lump's decompressed
/// length divides by 352 exactly 243 times. A wrong field offset would still parse; a wrong stride
/// could not.
/// </remarks>
public sealed class BspOverlaysTests
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
    public void Read_FindsEveryOverlayTheLumpHoldsRoomFor()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(File.ReadAllBytes(path));

        // The lump is 85,536 decompressed bytes at 352 each. Anything other than 243 means the
        // stride is wrong, and a wrong stride walks every field off its neighbour.
        overlays.Count.ShouldBe(243);
    }

    [Test]
    public void Read_EveryOverlayNamesAMaterialAndAtMostSixtyFourFaces()
    {
        // **The face count shares its sixteen bits with the render order**, top two bits for the
        // order and the rest for the count. Reading the whole field as a count gives values in the
        // tens of thousands for anything with a non-zero order, and 64 is the array's size - so
        // this is the assertion that catches a missing mask.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(File.ReadAllBytes(path));

        foreach (BspOverlay overlay in overlays)
        {
            overlay.FaceCount.ShouldBeInRange(0, 64, $"overlay {overlay.Id} face count");
            overlay.RenderOrder.ShouldBeInRange(0, 3, $"overlay {overlay.Id} render order");
            overlay.TexInfo.ShouldBeGreaterThanOrEqualTo((short)0);
            overlay.Faces.Count.ShouldBe(overlay.FaceCount);
        }

        TestContext.Out.WriteLine(
            $"OVERLAY {overlays.Sum(o => o.FaceCount)} face references across {overlays.Count} overlays");
    }

    [Test]
    public void Read_TheCornersSurroundTheOriginRatherThanRepeatingIt()
    {
        // **Four identical corners is what a wrong offset produces**, and it would draw nothing
        // rather than draw wrongly - which is indistinguishable from "decals not implemented yet".
        // A real overlay has extent, so its corners must differ from each other.
        //
        // This was weaker than it looked when the corners were read as three-dimensional points:
        // their z components carry the basis rather than a coordinate, so two corners at the same
        // place still differed in z and the check passed on that alone. Comparing the two
        // dimensions that are actually positions is the fix.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(File.ReadAllBytes(path));

        int degenerate = overlays.Count(overlay =>
            overlay.Corners.Distinct().Count() < 4);

        TestContext.Out.WriteLine($"OVERLAY {degenerate} of {overlays.Count} have repeated corners");

        degenerate.ShouldBe(0, "every overlay is a quad with four distinct corners");
    }

    [Test]
    public void Read_TheBasisIsRecoveredFromTheCornersAndIsOrthonormal()
    {
        // **The check that the smuggled basis was decoded rather than merely read.** vbsp packs
        // BasisU into the unused z of the first three corners and a flip flag into the fourth, so
        // U is assembled from three separate structs' worth of bytes. If any of those offsets is
        // wrong, U is not a unit vector and is not perpendicular to the normal.
        //
        // Both properties hold by construction for a real basis and hold for almost nothing else,
        // which is what makes this able to fail.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        foreach (BspOverlay overlay in BspOverlays.Read(File.ReadAllBytes(path)))
        {
            Length(overlay.BasisU).ShouldBeInRange(0.99, 1.01, $"overlay {overlay.Id} U length");
            Length(overlay.BasisV).ShouldBeInRange(0.99, 1.01, $"overlay {overlay.Id} V length");

            Dot(overlay.BasisU, overlay.BasisNormal)
                .ShouldBeInRange(-0.01, 0.01, $"overlay {overlay.Id} U must be perpendicular to N");
            Dot(overlay.BasisU, overlay.BasisV)
                .ShouldBeInRange(-0.01, 0.01, $"overlay {overlay.Id} U must be perpendicular to V");
        }
    }

    private static double Length((float X, float Y, float Z) vector) =>
        Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));

    private static double Dot(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    [Test]
    public void Read_TheBasisNormalIsAUnitVector()
    {
        // **The strongest check available without drawing anything.** A normal is normalised by
        // construction, so if this field is really the basis normal its length is one. Read at the
        // wrong offset it lands on a world position - coordinates in the thousands - or on part of
        // a UV point, and neither has length one. It pins the tail of the struct the way the
        // stride pins its size.
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        foreach (BspOverlay overlay in BspOverlays.Read(File.ReadAllBytes(path)))
        {
            (float x, float y, float z) = overlay.BasisNormal;

            double length = Math.Sqrt((x * x) + (y * y) + (z * z));

            length.ShouldBeInRange(0.99, 1.01, $"overlay {overlay.Id} basis normal length");
        }
    }
}
