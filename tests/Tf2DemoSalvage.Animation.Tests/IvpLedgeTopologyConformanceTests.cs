using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The address arithmetic IVP walks a compact ledge's edges by (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `FUN_1800a1b50`** (`docs/findings/51`, *The edge, and a claim tested with a
/// control*). A triangle is sixteen bytes — a header word and three edge words at `+4`, `+8` and `+0xC` — so an
/// edge's address ANDed with `0xC` says which of the three it is. `DAT_180124fb8` `{0, +4, +4, −8}` steps to the
/// next edge of the triangle, `DAT_180124fc8` `{0, +8, −4, −4}` to the previous one, and an edge word's bits 16–30
/// hop that many four-byte words from the edge's own address.
///
/// **The tetrahedron's offsets are each edge's classic twin**, a fixture this file chose so the ring has a known
/// answer. Whether a shipped ledge stores the twin is not established, and the walk does not depend on it: it is
/// the same arithmetic over whatever the file holds.
/// </remarks>
public sealed class IvpLedgeTopologyConformanceTests
{
    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 0)]
    public void Next_EachSlot_IsTheTrianglesFollowingEdge(int slot, int next) =>
        Tetrahedron().Next(new IvpLedgeEdge(2, slot)).ShouldBe(new IvpLedgeEdge(2, next));

    [TestCase(0, 2)]
    [TestCase(1, 0)]
    [TestCase(2, 1)]
    public void Previous_EachSlot_IsTheTrianglesPrecedingEdge(int slot, int previous) =>
        Tetrahedron().Previous(new IvpLedgeEdge(2, slot)).ShouldBe(new IvpLedgeEdge(2, previous));

    /// <remarks>An edge's low sixteen bits are its start point, so the edge in slot 1 of `(0, 2, 3)` starts at 2.</remarks>
    [Test]
    public void Start_AnEdge_IsThePointItsSlotNames() =>
        Tetrahedron().Start(new IvpLedgeEdge(2, 1)).ShouldBe(2);

    /// <remarks>
    /// **The hop counts four-byte words from the edge's own address, both ways.** Triangle 0's first edge sits at
    /// `+4` and hops `+6` words to `+28`, triangle 1's third edge; triangle 3's first edge at `+52` hops `−7` to
    /// `+24`, triangle 1's second. A hop counted in edges rather than words, or from the triangle's start, lands
    /// elsewhere.
    /// </remarks>
    [TestCase(0, 0, 1, 2)]
    [TestCase(3, 0, 1, 1)]
    public void Hop_AnEdgesOffset_LandsThatManyWordsFromItsAddress(int triangle, int slot, int toTriangle, int toSlot) =>
        Tetrahedron().Hop(new IvpLedgeEdge(triangle, slot)).ShouldBe(new IvpLedgeEdge(toTriangle, toSlot));

    /// <remarks>
    /// **Around a point, the walk steps back to the edge that ends there, hops, and stops on the edge it began
    /// from** — so the first hop is from the start's PREVIOUS edge and the last is the start itself. From `0 → 1`
    /// the tetrahedron's ring is `0 → 2`, `0 → 3`, `0 → 1`, and each hop starts at the point walked around.
    /// </remarks>
    [Test]
    public void Ring_AroundATetrahedronsPoint_VisitsEachEdgeFromItOnceEndingWithTheFirst()
    {
        IvpLedgeTopology topology = Tetrahedron();

        List<IvpLedgeEdge> ring = [.. topology.Ring(new IvpLedgeEdge(0, 0))];

        ring.ShouldBe([new IvpLedgeEdge(2, 0), new IvpLedgeEdge(1, 0), new IvpLedgeEdge(0, 0)]);
        ring.Select(topology.Start).ShouldAllBe(point => point == 0);
        ring.Select(edge => topology.Start(topology.Next(edge))).ShouldBe([2, 3, 1]);
    }

    /// <remarks>
    /// **An offset landing on a triangle's header word is refused.** The engine would read the header as an edge;
    /// the header is not one, and a `.phy` is a stranger's file (D32). Triangle 1's second edge at `+24` hopping
    /// `−2` words reaches `+16`, triangle 1's own header.
    /// </remarks>
    [Test]
    public void Hop_OntoATrianglesHeader_IsRefused()
    {
        IvpLedgeTopology topology = new([(0, 1, 2), (0, 3, 1)], [(0, 0, 0), (0, -2, 0)], [1, 0]);

        Should.Throw<InvalidDataException>(() => topology.Hop(new IvpLedgeEdge(1, 1)));
    }

    /// <remarks>An offset past the ledge's last triangle is refused on the same grounds.</remarks>
    [Test]
    public void Hop_PastTheLedge_IsRefused()
    {
        IvpLedgeTopology topology = new([(0, 1, 2)], [(4, 0, 0)], [0]);

        Should.Throw<InvalidDataException>(() => topology.Hop(new IvpLedgeEdge(0, 0)));
    }

    /// <remarks>
    /// **The backside walk starts from the first edge of the triangle a header names**, whichever of the three edges it
    /// was handed: `FUN_180094e30` masks the feature to its triangle, reads `(header &gt;&gt; 12) &amp; 0xFFF`, and takes
    /// `ledge + 16·index + 0x14` (`docs/findings/51`, *The minimize, routine by routine*). Triangle 1's third edge names
    /// triangle 3; triangle 3 names triangle 0.
    /// </remarks>
    [TestCase(1, 2, 3)]
    [TestCase(3, 1, 0)]
    public void Pierce_AnyEdgeOfATriangle_IsTheFirstEdgeOfTheTriangleItsHeaderNames(int triangle, int slot, int across) =>
        Tetrahedron().Pierce(new IvpLedgeEdge(triangle, slot)).ShouldBe(new IvpLedgeEdge(across, 0));

    /// <remarks>
    /// **A header naming a triangle past the ledge is refused (D32).** The engine would take whatever bytes lie there as a
    /// triangle; twelve bits can name 4,095 and a ledge rarely has more than a few dozen.
    /// </remarks>
    [Test]
    public void Pierce_PastTheLedge_IsRefused()
    {
        IvpLedgeTopology topology = new([(0, 1, 2)], [(0, 0, 0)], [1]);

        Should.Throw<InvalidDataException>(() => topology.Pierce(new IvpLedgeEdge(0, 0)));
    }

    /// <remarks>
    /// **A ring that never comes back to its start is refused** after as many hops as the ledge has edges. The
    /// engine's loop has no bound and would never return; here triangle 0's third edge hops `+2` words into
    /// triangle 1, whose edges all hop to themselves, so the walk circles triangle 1 for ever.
    /// </remarks>
    [Test]
    public void Ring_ThatNeverReturnsToItsStart_IsRefused()
    {
        IvpLedgeTopology topology = new([(0, 1, 2), (0, 3, 1)], [(0, 0, 2), (0, 0, 0)], [1, 0]);

        Should.Throw<InvalidDataException>(() => topology.Ring(new IvpLedgeEdge(0, 0)).ToList());
    }

    /// <summary>A closed tetrahedron whose every edge word hops to its classic twin.</summary>
    /// <remarks>
    /// Edge addresses are `16·triangle + 4 + 4·slot`; each offset is the twin's address less the edge's own, over
    /// four. Triangle 0 is `(0, 1, 2)`: `0 → 1` at `+4` twins `1 → 0` at `+28`, `+6`; `1 → 2` at `+8` twins `2 → 1`
    /// at `+60`, `+13`; `2 → 0` at `+12` twins `0 → 2` at `+36`, `+6`. The other three follow the same way. Each
    /// header names a triangle other than its own as the one across the ledge.
    /// </remarks>
    private static IvpLedgeTopology Tetrahedron() =>
        new(
            [(0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2)],
            [(6, 13, 6), (6, 7, -6), (-6, 4, -6), (-7, -4, -13)],
            [3, 3, 1, 0]);
}
