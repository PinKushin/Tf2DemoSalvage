using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A leaf's AREA, which is how the engine draws a 3D skybox apart from the world it sits outside.
/// </summary>
/// <remarks>
/// **<c>dleaf_t</c> packs two fields into one <c>short</c> at offset 6** — <c>area:9</c> then
/// <c>flags:7</c> — so the area is the low nine bits and the flags the high seven. Reading the
/// whole short as the area is a plausible-looking bug: it agrees for every leaf whose flags happen
/// to be zero, which on a map with no leaf water or sky flags is most of them.
///
/// **This is the field <c>CSkyboxView</c> filters on** (<c>viewrender.cpp:4877</c>):
///
/// <code>
///   tmpbits[m_pSky3dParams-&gt;area&gt;&gt;3] |= 1 &lt;&lt; (m_pSky3dParams-&gt;area&amp;7);
///   *areabits = tmpbits;
/// </code>
///
/// — the sky pass sets exactly one area bit, so it draws the miniature room and nothing else, and
/// the main pass draws everything else and not the room. Without it a TF2 map's skybox scenery is
/// simply present in the world at its literal size and position, which is what this viewer does
/// today (B152).
/// </remarks>
public sealed class BspLeafAreaTests
{
    [Test]
    public void Area_ForALeafWithNoFlags_IsTheValueWritten()
    {
        BspLeafTree tree = WithAreas((Leaf: 0, Area: 0, Flags: 0), (Leaf: 2, Area: 5, Flags: 0));

        tree.Area(2).ShouldBe(5);
        tree.Area(0).ShouldBe(0);
    }

    /// <remarks>
    /// **The case that separates a nine-bit read from a sixteen-bit one.** With flags zero the two
    /// agree, so a fixture that set only the area would pass against either — the wrong-condition
    /// fault, where correct and broken predict the same observation.
    /// </remarks>
    [Test]
    public void Area_ForALeafWithFlagsSet_IgnoresTheFlags()
    {
        // flags 0x7F is every flag bit; read as a whole short that gives 0xFF05, not 5.
        BspLeafTree tree = WithAreas((Leaf: 2, Area: 5, Flags: 0x7F));

        tree.Area(2).ShouldBe(5, "the flags live above the area and are not part of it");
    }

    [Test]
    public void Area_ForTheLargestAreaANineBitFieldHolds_ReadsItWhole()
    {
        // 511 is 0x1FF: every area bit set, which a mask that was one bit short would report as 255.
        BspLeafTree tree = WithAreas((Leaf: 1, Area: 511, Flags: 0x7F));

        tree.Area(1).ShouldBe(511);
    }

    [Test]
    public void Area_ForALeafThatDoesNotExist_IsMinusOne()
    {
        BspLeafTree tree = WithAreas((Leaf: 0, Area: 3, Flags: 0));

        tree.Area(-1).ShouldBe(-1);
        tree.Area(99).ShouldBe(-1, "past the lump is not area zero");
    }

    /// <summary>A tree whose leaf lump carries the given areas and flags.</summary>
    /// <remarks>
    /// The node and plane are the same one-split shape the other leaf-tree tests use; only the leaf
    /// lump matters here, and it is written at the real stride so the offsets are the file's.
    /// </remarks>
    private static BspLeafTree WithAreas(params (int Leaf, int Area, int Flags)[] leaves)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -3);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1);

        byte[] lump = new byte[128];

        foreach ((int leaf, int area, int flags) in leaves)
        {
            // area:9 then flags:7, little-endian, so the flags occupy the high seven bits.
            BinaryPrimitives.WriteInt16LittleEndian(
                lump.AsSpan((leaf * 32) + 6), (short)((area & 0x1FF) | (flags << 9)));
        }

        return BspLeafTree.FromLumps(node, plane, lump);
    }
}
