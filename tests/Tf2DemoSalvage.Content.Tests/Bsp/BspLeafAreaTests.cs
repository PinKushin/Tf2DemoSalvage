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

    /// <remarks>
    /// **The flags are the HIGH seven bits of the same short, and <c>bspfile.h:788</c> says so in
    /// capitals**: *"NOTE: Only 7-bits stored!!!"*. A reader that returned the whole short would
    /// give an area's value as flags — which for area 5 is 5, a number that looks like a plausible
    /// flag set.
    /// </remarks>
    [Test]
    public void Flags_ForALeafWithAnAreaAndFlags_ReadsTheFlagsAlone()
    {
        BspLeafTree tree = WithAreas((Leaf: 2, Area: 5, Flags: 0x05));

        tree.Flags(2).ShouldBe(0x05, "the area below them is not part of the flags");
    }

    [Test]
    public void Flags_ForALeafWithNoFlags_IsZeroHoweverLargeTheArea()
    {
        // The control for the test above: a big area must not leak upward into the flags either.
        BspLeafTree tree = WithAreas((Leaf: 1, Area: 511, Flags: 0));

        tree.Flags(1).ShouldBe(0);
    }

    /// <remarks>
    /// **A leaf carries at most ONE of the two sky flags**, because vrad stops at the first sky
    /// face in the leaf and chooses on that face alone (`BuildVisForLightEnvironment`,
    /// <c>lightmap.cpp:1355</c>). So this is a choice rather than a set, and 3D wins if both
    /// somehow appear — which is the order `SkyboxVisibility_t` itself is declared in.
    /// </remarks>
    [Test]
    public void SkyboxVisibleFrom_ALeafFlaggedForThreeDimensionalSky_SaysSo()
    {
        // LEAF_FLAGS_SKY is 0x01 (bspfile.h:789).
        BspLeafTree tree = WithAreas((Leaf: 1, Area: 0, Flags: 0x01), (Leaf: 2, Area: 0, Flags: 0));

        tree.SkyboxVisibleFrom(0f, 0f, 10f).ShouldBe(SkyboxVisibility.ThreeDimensional);
    }

    [Test]
    public void SkyboxVisibleFrom_ALeafFlaggedForTwoDimensionalSky_IsNotTheThreeDimensionalOne()
    {
        // LEAF_FLAGS_SKY2D is 0x04, and SURF_SKY2D's own definition says it draws the flat sky but
        // NOT the 3D room (bspflags.h:81) — so conflating the two would draw the room in every map
        // whose sky is 2D only.
        BspLeafTree tree = WithAreas((Leaf: 1, Area: 0, Flags: 0x04), (Leaf: 2, Area: 0, Flags: 0));

        tree.SkyboxVisibleFrom(0f, 0f, 10f).ShouldBe(SkyboxVisibility.TwoDimensional);
    }

    [Test]
    public void SkyboxVisibleFrom_ALeafWithNeitherFlag_SeesNoSky()
    {
        // The control. Without it, an implementation returning ThreeDimensional unconditionally
        // would pass both cases above.
        BspLeafTree tree = WithAreas((Leaf: 1, Area: 0, Flags: 0x02), (Leaf: 2, Area: 0, Flags: 0));

        tree.SkyboxVisibleFrom(0f, 0f, 10f).ShouldBe(
            SkyboxVisibility.None, "LEAF_FLAGS_RADIAL is not a sky flag");
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

        // Children written as `-leaf - 1`: above the z = 0 plane is leaf 1, below it leaf 2. The
        // sky tests read a point at z = 10, which lands in leaf 1.
        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -3);

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
