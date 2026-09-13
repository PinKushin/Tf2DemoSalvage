using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The <c>IVPS</c> compact-ledge hull inside a <c>.phy</c> solid (B58).
/// </summary>
/// <remarks>
/// **Synthetic rather than a shipped `.phy`, and here that is the STRONGER instrument** (D38). A
/// test against `barrel01` can only compare two readings of the same bytes and has no idea what the
/// right answer is; this file writes the points and the triangle indices, so it knows. The shipped
/// files were the evidence for the LAYOUT and that measurement is written up in
/// `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` — the reader is checked against real
/// hulls by the `ragdoll` probe, which is where a census belongs.
///
/// **What each test pins is a byte offset that a plausible misreading gets wrong**, because that is
/// the failure this format actually produced: triangles at `+0x14` instead of `+0x10` was read out
/// of the decompiled validator and only the files disproved it.
/// </remarks>
public sealed class PhysicsHullConformanceTests
{
    /// <remarks>
    /// **One ledge, three points, one triangle — the smallest thing that can distinguish a right
    /// reader from a wrong one.** A shape with more of anything would let an off-by-one in the
    /// stride still land on a real number.
    /// </remarks>
    [Test]
    public void Read_WithOneLedgeOfOneTriangle_ResolvesEveryPoint()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)]);

        IReadOnlyList<PhysicsLedge> ledges = PhysicsHull.Read(solid);

        ledges.Count.ShouldBe(1);
        ledges[0].Triangles.Count.ShouldBe(1);
        ledges[0].Triangles[0].ShouldBe((0, 1, 2));
        ledges[0].Points[0].ShouldBe(new Vector3(1f, 2f, 3f));
        ledges[0].Points[2].ShouldBe(new Vector3(7f, 8f, 9f));
    }

    /// <remarks>
    /// **The triangle stride, isolated.** Two triangles that name DIFFERENT points is the condition
    /// under which a sixteen-byte stride and any other stride predict different output; two
    /// triangles naming the same points would read correctly at several wrong strides.
    /// </remarks>
    [Test]
    public void Read_WithTwoTriangles_ReadsThemSixteenBytesApart()
    {
        byte[] solid = Solid(
            [
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 1f),
            ],
            [(0, 1, 2), (1, 2, 3)]);

        IReadOnlyList<PhysicsLedge> ledges = PhysicsHull.Read(solid);

        ledges[0].Triangles.Count.ShouldBe(2);

        // Renumbered in first-seen order, so the second triangle's points are 1, 2 and a NEW 3.
        ledges[0].Triangles[1].ShouldBe((1, 2, 3));
        ledges[0].Points[3].ShouldBe(new Vector3(0f, 0f, 1f));
    }

    /// <remarks>
    /// **`MOPP` is Havok's own tree and a different structure entirely**, and the engine's
    /// deserialiser refuses it too. Reading it as `IVPS` would not fail — it would produce triangles
    /// out of unrelated bytes, which is the worst possible outcome for a collision hull.
    /// </remarks>
    [Test]
    public void Read_WithAMoppSurface_ReadsNothing()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)],
            magic: "MOPP"u8);

        PhysicsHull.Read(solid).ShouldBeEmpty();
    }

    /// <remarks>
    /// **A magic of zero is an OLD `.phy` and the engine loads it anyway**, so refusing it would
    /// drop hulls the game itself collides against. The control for the test above: without this,
    /// "refuses what it does not know" and "refuses everything but IVPS" are the same observation.
    /// </remarks>
    [Test]
    public void Read_WithAZeroMagic_ReadsTheHullAnyway()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)],
            magic: [0, 0, 0, 0]);

        PhysicsHull.Read(solid).Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A `.phy` is a stranger's file (D32).** A triangle count that runs past the blob must read
    /// nothing rather than throw out of the arithmetic — the same guard every other reader here
    /// carries, and the one a fuzzer reaches first.
    /// </remarks>
    [Test]
    public void Read_WithATriangleCountPastTheEnd_ReadsNothing()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)]);

        // The count sits in the low 16 bits at ledge + 0x0C. The ledge is the first thing written
        // after the surface, so its own base is known here rather than searched for.
        BitConverter.GetBytes(4096).CopyTo(solid, LedgeAt + 0x0C);

        PhysicsHull.Read(solid).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The mass center is the surface's first three floats and the rotation inertia the next three** —
    /// `IVP_Compact_Surface+0x00..0x08` and `+0x0C..0x14`, which is what the surface manager's virtuals `+8`
    /// and `+0x18` copy out (`docs/findings/51`, *Which hull bytes the surface manager returns*). Six distinct
    /// values, so a read four bytes off lands on a different number rather than a plausible one.
    /// </remarks>
    [Test]
    public void MassProperties_FromTheSurfaceHeader_ReadsTheMassCenterThenTheRotationInertia()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)],
            massCenter: new Vector3(0.1f, -0.2f, 0.3f),
            rotationInertia: new Vector3(0.004f, 0.005f, 0.006f));

        PhysicsMassProperties? properties = PhysicsHull.MassProperties(solid);

        properties.ShouldNotBeNull();
        properties.Value.MassCenter.ShouldBe(new Vector3(0.1f, -0.2f, 0.3f));
        properties.Value.RotationInertia.ShouldBe(new Vector3(0.004f, 0.005f, 0.006f));
    }

    /// <remarks>
    /// **What the ledge reader refuses, this refuses** — a `MOPP` surface is a different structure, so its
    /// first six floats are not a mass center and an inertia.
    /// </remarks>
    [Test]
    public void MassProperties_WithAMoppSurface_ReadsNothing()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)],
            magic: "MOPP"u8,
            massCenter: new Vector3(0.1f, -0.2f, 0.3f));

        PhysicsHull.MassProperties(solid).ShouldBeNull();
    }

    /// <remarks>
    /// **A blob too short to hold a surface has none to read (D32)**, rather than an exception from slicing.
    /// </remarks>
    [Test]
    public void MassProperties_TooShortForASurface_ReadsNothing()
    {
        PhysicsHull.MassProperties(new byte[0x20]).ShouldBeNull();
    }

    /// <remarks>
    /// **An edge word's bits 16–30 are a signed offset, counted in four-byte edge words, and bit 31 is not part
    /// of it** (B369). `FUN_1800a1b50` reads it as `(int)(word &lt;&lt; 1) &gt;&gt; 17` before hopping `offset × 4` bytes
    /// from the edge, which is how the vertex-face search walks the edges around a point (`docs/findings/51`,
    /// *The edge, and a claim tested with a control*). The low 16 bits stay the start point. A negative, a
    /// positive and the largest positive value, with bit 31 set on one word, so a shift that keeps bit 31, an
    /// unsigned read or a sixteen-bit field each give a different answer.
    /// </remarks>
    [Test]
    public void Read_AnEdgeWordsUpperBits_AreCarriedAsASignedEdgeOffset()
    {
        byte[] solid = Solid(
            [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)],
            [(0, 1, 2)],
            edgeOffsets: [(-3, 2, 16383)],
            highBit: true);

        PhysicsLedge ledge = PhysicsHull.Read(solid)[0];

        ledge.Triangles[0].ShouldBe((0, 1, 2), "the start points are the low sixteen bits still");
        ledge.EdgeOffsets[0].ShouldBe((-3, 2, 16383));
    }

    /// <summary>Where the ledge is written, from the solid's <c>VPHY</c> tag.</summary>
    private const int LedgeAt = 0x1C + 0x30;

    /// <summary>
    /// Builds one solid blob: surface, then one ledge, its triangles and its points, then the tree.
    /// </summary>
    /// <remarks>
    /// **Laid out in the order a real file uses** — the ledges come BEFORE the tree, which is why
    /// a leaf's ledge offset is negative in every shipped file and why writing them the tidy way
    /// round would leave the reader's sign handling untested.
    /// </remarks>
    private static byte[] Solid(
        Vector3[] points,
        (int A, int B, int C)[] triangles,
        ReadOnlySpan<byte> magic = default,
        Vector3 massCenter = default,
        Vector3 rotationInertia = default,
        (int A, int B, int C)[]? edgeOffsets = null,
        bool highBit = false)
    {
        int trianglesAt = LedgeAt + 0x10;
        int pointsAt = trianglesAt + (triangles.Length * 0x10);
        int treeAt = pointsAt + (points.Length * 0x10);

        byte[] solid = new byte[treeAt + 0x1C];

        "VPHY"u8.CopyTo(solid);

        // IVP_Compact_Surface: the mass center and rotation inertia lead it, then the tree offset and the
        // magic, all relative to the surface's own base rather than to the file.
        BitConverter.GetBytes(massCenter.X).CopyTo(solid, 0x1C + 0x00);
        BitConverter.GetBytes(massCenter.Y).CopyTo(solid, 0x1C + 0x04);
        BitConverter.GetBytes(massCenter.Z).CopyTo(solid, 0x1C + 0x08);
        BitConverter.GetBytes(rotationInertia.X).CopyTo(solid, 0x1C + 0x0C);
        BitConverter.GetBytes(rotationInertia.Y).CopyTo(solid, 0x1C + 0x10);
        BitConverter.GetBytes(rotationInertia.Z).CopyTo(solid, 0x1C + 0x14);
        BitConverter.GetBytes(treeAt - 0x1C).CopyTo(solid, 0x1C + 0x20);
        (magic.IsEmpty ? "IVPS"u8 : magic).CopyTo(solid.AsSpan(0x1C + 0x2C));

        // The ledge: its point array is addressed as a delta from the ledge itself.
        BitConverter.GetBytes(pointsAt - LedgeAt).CopyTo(solid, LedgeAt);
        BitConverter.GetBytes(triangles.Length).CopyTo(solid, LedgeAt + 0x0C);

        for (int index = 0; index < triangles.Length; index++)
        {
            int at = trianglesAt + (index * 0x10);

            // `at` itself is the triangle's header word, which nothing reads. Each edge word is its start point in
            // the low sixteen bits and its offset in bits 16–30; bit 31, when asked for, goes on the middle edge.
            (int A, int B, int C) offsets = edgeOffsets?[index] ?? (0, 0, 0);

            BitConverter.GetBytes(Edge(triangles[index].A, offsets.A, high: false)).CopyTo(solid, at + 4);
            BitConverter.GetBytes(Edge(triangles[index].B, offsets.B, highBit)).CopyTo(solid, at + 8);
            BitConverter.GetBytes(Edge(triangles[index].C, offsets.C, high: false)).CopyTo(solid, at + 12);
        }

        for (int index = 0; index < points.Length; index++)
        {
            int at = pointsAt + (index * 0x10);

            BitConverter.GetBytes(points[index].X).CopyTo(solid, at);
            BitConverter.GetBytes(points[index].Y).CopyTo(solid, at + 4);
            BitConverter.GetBytes(points[index].Z).CopyTo(solid, at + 8);
        }

        // The tree root, a single leaf: right offset 0, and a NEGATIVE ledge offset.
        BitConverter.GetBytes(LedgeAt - treeAt).CopyTo(solid, treeAt + 4);

        return solid;
    }

    /// <summary>One edge word: a start point, a fifteen-bit offset above it, and optionally bit 31.</summary>
    private static uint Edge(int start, int offset, bool high) =>
        (uint)(start & 0xFFFF) | (((uint)offset & 0x7FFF) << 16) | (high ? 0x8000_0000u : 0u);
}
