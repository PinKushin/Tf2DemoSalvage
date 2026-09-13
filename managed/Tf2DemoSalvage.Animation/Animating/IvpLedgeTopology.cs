using System;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One edge word of a compact ledge: its triangle, and which of the triangle's three edges it is.</summary>
/// <param name="Triangle">The triangle's index in the ledge.</param>
/// <param name="Slot">0, 1 or 2 — the edge word at <c>+4</c>, <c>+8</c> or <c>+0xC</c> of the triangle.</param>
public readonly record struct IvpLedgeEdge(int Triangle, int Slot);

/// <summary>
/// A compact ledge's triangles and edge words, walked by the address arithmetic IVP's narrow phase uses (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `FUN_1800a1b50`** (`docs/findings/51`, *The edge, and a claim tested with a
/// control*). Triangles are sixteen bytes — a header word, then three edge words — so an edge's address
/// relative to the triangle array is `16·triangle + 4 + 4·slot`, and `address &amp; 0xC` is `4`, `8` or `0xC`.
/// `DAT_180124fb8` and `DAT_180124fc8`, indexed by that, step to the triangle's next and previous edge; an
/// edge word's bits 16–30 (<see cref="Content.Assets.PhysicsLedge.EdgeOffsets"/>) hop that many four-byte words
/// from the edge's own address.
///
/// **Two refusals the engine does not make**, because it trusts its ledges and a `.phy` is a stranger's file
/// (D32): a hop landing on a triangle's header word or outside the ledge, where the engine would read whatever
/// is there as an edge, and a ring that has not returned to its start after as many hops as the ledge has edges,
/// where the engine's loop would never end.
/// </remarks>
public sealed class IvpLedgeTopology
{
    /// <summary><c>DAT_180124fb8</c>, keyed by <c>address &amp; 0xC</c> over four.</summary>
    private static readonly int[] NextOffsets = [0, 4, 4, -8];

    /// <summary><c>DAT_180124fc8</c>, keyed the same way.</summary>
    private static readonly int[] PreviousOffsets = [0, 8, -4, -4];

    private const int TriangleSize = 16;

    private const int EdgeWordSize = 4;

    private readonly IReadOnlyList<(int A, int B, int C)> _triangles;
    private readonly IReadOnlyList<(int A, int B, int C)> _edgeOffsets;

    /// <summary>Wraps a ledge's triangles and the offset field of each of their edge words.</summary>
    /// <param name="triangles">Each triangle's three start points, slot by slot.</param>
    /// <param name="edgeOffsets">Each triangle's three edge offsets, slot by slot, as the file stores them.</param>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    /// <exception cref="ArgumentException">The lists are not the same length.</exception>
    public IvpLedgeTopology(IReadOnlyList<(int A, int B, int C)> triangles, IReadOnlyList<(int A, int B, int C)> edgeOffsets)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(edgeOffsets);

        if (triangles.Count != edgeOffsets.Count)
        {
            throw new ArgumentException("A ledge has one set of edge offsets per triangle.", nameof(edgeOffsets));
        }

        _triangles = triangles;
        _edgeOffsets = edgeOffsets;
    }

    /// <summary>The point an edge starts at — its word's low sixteen bits.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The point's index.</returns>
    public int Start(IvpLedgeEdge edge) => Slot(_triangles[edge.Triangle], edge.Slot);

    /// <summary>The triangle's following edge — <c>DAT_180124fb8</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The next edge of the same triangle.</returns>
    public IvpLedgeEdge Next(IvpLedgeEdge edge)
    {
        int address = Address(edge);
        return EdgeAt(address + NextOffsets[(address & 0xC) >> 2]);
    }

    /// <summary>The triangle's preceding edge — <c>DAT_180124fc8</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The previous edge of the same triangle.</returns>
    public IvpLedgeEdge Previous(IvpLedgeEdge edge)
    {
        int address = Address(edge);
        return EdgeAt(address + PreviousOffsets[(address & 0xC) >> 2]);
    }

    /// <summary>The edge an edge word's offset field leads to — <c>edge + ((int)(word &lt;&lt; 1) &gt;&gt; 17) × 4</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The edge that many words away.</returns>
    /// <exception cref="InvalidDataException">The hop lands on a triangle's header or outside the ledge.</exception>
    public IvpLedgeEdge Hop(IvpLedgeEdge edge) =>
        EdgeAt(Address(edge) + (Slot(_edgeOffsets[edge.Triangle], edge.Slot) * EdgeWordSize));

    /// <summary>The edges from an edge's start point, in the order <c>FUN_1800a1b50</c> visits them.</summary>
    /// <param name="start">The edge the walk begins from.</param>
    /// <returns>Each hop, the last being <paramref name="start"/> itself.</returns>
    /// <exception cref="InvalidDataException">A hop is refused, or the ring does not return to its start.</exception>
    /// <remarks>
    /// **Step back, hop, and stop on the start** (`1800a1e29`–`1800a1fad`): the walk takes the start's previous
    /// edge, which ends at the point, hops to an edge leaving it, and compares that hop with the start only
    /// after the hop has been used — so the start is visited last.
    /// </remarks>
    public IEnumerable<IvpLedgeEdge> Ring(IvpLedgeEdge start)
    {
        Address(start);

        return Walk(start);
    }

    private IEnumerable<IvpLedgeEdge> Walk(IvpLedgeEdge start)
    {
        int limit = _triangles.Count * 3;
        IvpLedgeEdge current = Previous(start);

        for (int hops = 0; hops < limit; hops++)
        {
            IvpLedgeEdge hop = Hop(current);

            yield return hop;

            if (hop == start)
            {
                yield break;
            }

            current = Previous(hop);
        }

        throw new InvalidDataException("A ledge's edge ring did not return to the edge it started from.");
    }

    private int Address(IvpLedgeEdge edge)
    {
        if (edge.Triangle < 0 || edge.Triangle >= _triangles.Count || edge.Slot is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "The edge is not in this ledge.");
        }

        return (edge.Triangle * TriangleSize) + EdgeWordSize + (edge.Slot * EdgeWordSize);
    }

    private IvpLedgeEdge EdgeAt(int address)
    {
        int triangle = Math.DivRem(address, TriangleSize, out int within);

        if (within < 0)
        {
            triangle--;
            within += TriangleSize;
        }

        if (within == 0 || triangle < 0 || triangle >= _triangles.Count)
        {
            throw new InvalidDataException("A ledge's edge offset leads outside its triangles' edge words.");
        }

        return new IvpLedgeEdge(triangle, (within / EdgeWordSize) - 1);
    }

    private static int Slot((int A, int B, int C) values, int slot) =>
        slot switch
        {
            0 => values.A,
            1 => values.B,
            _ => values.C,
        };
}
