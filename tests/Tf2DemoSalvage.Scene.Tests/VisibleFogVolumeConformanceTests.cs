using System;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>R_GetVisibleFogVolume</c>, engine.dll <c>0x1800e0080</c>, and its walk <c>0x1800e0f70</c> — read in disassembly
/// (the engine is closed).
/// </summary>
/// <remarks>
/// One node on the plane x = 0: leaf 1 in front (x &gt; 0), leaf 0 behind. Leaf 0 is water volume 0, whose surface is
/// at z = 64 under texinfo 7; leaf 1 is dry. The distance table gives leaf 0 twelve and leaf 1 thirty-four, so which
/// leaf a distance came from is visible in the answer.
/// </remarks>
public sealed class VisibleFogVolumeConformanceTests
{
    private static readonly BspWater Water = new([new BspWaterVolume(64f, -100f, 7)], [12, 34]);

    private static BspLeafTree Tree(int dryContents, int wetContents = 0x20)
    {
        byte[] node = new byte[32];

        BitConverter.TryWriteBytes(node.AsSpan(4), -2);
        BitConverter.TryWriteBytes(node.AsSpan(8), -1);

        for (int axis = 0; axis < 3; axis++)
        {
            BitConverter.TryWriteBytes(node.AsSpan(12 + (axis * 2)), (short)-512);
            BitConverter.TryWriteBytes(node.AsSpan(18 + (axis * 2)), (short)512);
        }

        byte[] plane = new byte[20];

        BitConverter.TryWriteBytes(plane.AsSpan(0), 1f);

        byte[] leaves = new byte[64];

        Leaf(0, wetContents, 0);
        Leaf(1, dryContents, -1);

        return BspLeafTree.FromLumps(node, plane, leaves);

        void Leaf(int index, int contents, short water)
        {
            Span<byte> leaf = leaves.AsSpan(index * 32);

            BitConverter.TryWriteBytes(leaf, contents);

            for (int axis = 0; axis < 3; axis++)
            {
                BitConverter.TryWriteBytes(leaf[(8 + (axis * 2))..], (short)-512);
                BitConverter.TryWriteBytes(leaf[(14 + (axis * 2))..], (short)512);
            }

            BitConverter.TryWriteBytes(leaf[28..], water);
        }
    }

    private static FogVolumeInfo Find(
        BspLeafTree tree, float x, Func<int, bool>? visible = null, bool inView = true) =>
        VisibleFogVolume.Find(tree, Water, (x, 0f, 100f), visible ?? (_ => true), (_, _) => inView);

    [Test]
    public void Find_EyeInAWaterLeaf_IsInsideThatVolume()
    {
        // 0x1800e01a3-0x1800e01e8: the leaf's own ID, eye in, its surfaceZ.
        Find(Tree(dryContents: 0), x: -10f).ShouldBe(new FogVolumeInfo(0, 0, true, 12f, 64f, 7));
    }

    [Test]
    public void Find_DryLeafMarkedToTest_WalksToTheVisibleWaterLeaf()
    {
        // 0x1800e01f2 bit 8, then the walk; the distance stays the EYE's leaf's (0x1800e031f indexes by r14).
        Find(Tree(dryContents: VisibleFogVolume.ContentsTestFogVolume), x: 10f)
            .ShouldBe(new FogVolumeInfo(0, 0, false, 34f, 64f, 7));
    }

    [Test]
    public void Find_DryLeafNotMarked_SeesNoVolume()
    {
        // 0x1800e01f7 → 0x1800e02f0.
        Find(Tree(dryContents: 0), x: 10f).Volume.ShouldBe(-1);
    }

    [Test]
    public void Find_WaterLeafOutsideTheVisibleSet_IsNotFound()
    {
        // 0x1800e0f89 — the leaf's visframe.
        Find(Tree(dryContents: VisibleFogVolume.ContentsTestFogVolume), x: 10f, visible: leaf => leaf != 0)
            .Volume.ShouldBe(-1);
    }

    [Test]
    public void Find_WaterLeafOutsideTheFrustum_IsNotFound()
    {
        // 0x1800e0fac — the frustum test on the box.
        Find(Tree(dryContents: VisibleFogVolume.ContentsTestFogVolume), x: 10f, inView: false).Volume.ShouldBe(-1);
    }

    [Test]
    public void BoxIntersectsVolume_ABoxReachingTheWaterLeaf_IsTrueOnlyForItsId()
    {
        // DoesBoxIntersectWaterVolume, IVRenderView slot 34 (0x18012ea20): every leaf the box reaches is offered to
        // 0x18012e9f0, which answers "found" when the leaf's water data ID is the one asked for.
        BspLeafTree tree = Tree(dryContents: 0);

        VisibleFogVolume.BoxIntersectsVolume(tree, (-20f, -5f, 0f), (-10f, 5f, 10f), 0).ShouldBeTrue();
        VisibleFogVolume.BoxIntersectsVolume(tree, (10f, -5f, 0f), (20f, 5f, 10f), 0).ShouldBeFalse();
        VisibleFogVolume.BoxIntersectsVolume(tree, (-20f, -5f, 0f), (-10f, 5f, 10f), 1).ShouldBeFalse();
    }

    [Test]
    public void Find_SlimeLeaf_IsSkipped()
    {
        // 0x1800e0fcd — CONTENTS_SLIME (0x10) is passed over.
        Find(Tree(dryContents: VisibleFogVolume.ContentsTestFogVolume, wetContents: BspLeafTree.ContentsSlime), x: 10f)
            .Volume.ShouldBe(-1);
    }
}
