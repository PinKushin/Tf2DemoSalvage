using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>A point's contents on the world: its leaf's <c>contents</c>, the first <c>int</c> of <c>dleaf_t</c>.</summary>
/// <remarks>
/// What <c>enginetrace-&gt;GetPointContents</c> answers for the world, and what `FireBullet` asks at both ends of a shot to
/// decide whether the bullet entered water (`tf_player_shared.cpp:10513`).
/// </remarks>
public sealed class BspLeafContentsTests
{
    /// <summary>`CONTENTS_WATER`.</summary>
    private const int Water = 0x20;

    [Test]
    public void ContentsAt_APointInAWaterLeaf_IsWater()
    {
        BspLeafTree tree = WithContents(above: 0, below: Water);

        tree.ContentsAt(0f, 0f, -10f).ShouldBe(Water);
        tree.ContentsAt(0f, 0f, 10f).ShouldBe(0, "the leaf above the plane holds nothing");
    }

    [Test]
    public void ContentsAt_AMapWithNoTree_IsNothing() =>
        BspLeafTree.FromLumps(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty).ContentsAt(0f, 0f, 0f).ShouldBe(0);

    /// <summary>One split at z = 0: leaf 1 above, leaf 2 below, each with the contents given.</summary>
    private static BspLeafTree WithContents(int above, int below)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -3);

        byte[] lump = new byte[128];

        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(32), above);
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(64), below);

        return BspLeafTree.FromLumps(node, plane, lump);
    }
}
