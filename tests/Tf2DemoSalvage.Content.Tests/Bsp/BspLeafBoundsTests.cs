using System;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A leaf's bounding box, read for mat_leafvis.
/// </summary>
/// <remarks>
/// **The assertion that makes this more than a struct read: the box must CONTAIN the point that
/// found the leaf.** Reading six shorts at a plausible offset produces plausible numbers whatever
/// the offset is — the failure mode of every layout question in this project — and a box drawn
/// somewhere other than around the camera looks like a rendering bug rather than a parsing one.
///
/// Joining `LeafAt` to `Bounds` is what closes that: the tree walk and the box come from different
/// lumps and different code, so agreement between them is evidence rather than a restatement.
/// </remarks>
public sealed class BspLeafBoundsTests
{
    private static string Map => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void Bounds_TheLeafFoundForAPoint_ContainsThatPoint()
    {
        if (!File.Exists(Map))
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree tree = BspLeafTree.Read(File.ReadAllBytes(Map));

        if (tree.IsEmpty)
        {
            Assert.Ignore("the map has no BSP tree");
            return;
        }

        // Points spread through the map rather than one, because a single sample can agree with a
        // wrong offset by luck and three spread over thousands of units cannot.
        int tested = 0;

        foreach ((float x, float y, float z) in new[]
        {
            (0f, 0f, 0f), (512f, -512f, 64f), (-1024f, 1024f, 256f), (256f, 256f, -128f),
        })
        {
            int leaf = tree.LeafAt(x, y, z);

            if (leaf < 0 || tree.Bounds(leaf) is not { } box)
            {
                continue;
            }

            tested++;

            TestContext.Out.WriteLine(
                $"LEAF {leaf} for ({x},{y},{z}) is {box.Min} .. {box.Max}");

            // **Inclusive, and the box is a frustum-culling bound**, so it is conservative rather
            // than tight — a point on the boundary is inside it.
            box.Min.X.ShouldBeLessThanOrEqualTo(x);
            box.Min.Y.ShouldBeLessThanOrEqualTo(y);
            box.Min.Z.ShouldBeLessThanOrEqualTo(z);
            box.Max.X.ShouldBeGreaterThanOrEqualTo(x);
            box.Max.Y.ShouldBeGreaterThanOrEqualTo(y);
            box.Max.Z.ShouldBeGreaterThanOrEqualTo(z);

            // A degenerate box would satisfy nothing above if the point sat exactly on it, and is
            // the shape a wrong offset most often produces: all six shorts reading as zero.
            (box.Max.X - box.Min.X).ShouldBeGreaterThan(0f);
        }

        tested.ShouldBeGreaterThan(
            0, "no point resolved to a leaf with bounds, so nothing above was measured");
    }

    [Test]
    public void Bounds_ALeafThatDoesNotExist_IsNullRatherThanZero()
    {
        if (!File.Exists(Map))
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree tree = BspLeafTree.Read(File.ReadAllBytes(Map));

        // Null, not an empty box: "there is no such leaf" and "the leaf is a point" are different
        // answers, and a caller drawing the second would draw a dot nobody could interpret.
        tree.Bounds(-1).ShouldBeNull();
        tree.Bounds(int.MaxValue).ShouldBeNull();
    }
}
