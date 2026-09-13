using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

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
    /// <summary>Three distinct points, so a read from the wrong base lands on a different number.</summary>
    private static readonly Vector3[] Triangle =
        [new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f)];

    /// <remarks>
    /// **One ledge, three points, one triangle — the smallest thing that can distinguish a right
    /// reader from a wrong one.** A shape with more of anything would let an off-by-one in the
    /// stride still land on a real number.
    /// </remarks>
    [Test]
    public void Read_WithOneLedgeOfOneTriangle_ResolvesEveryPoint()
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)]);

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
    /// **A `VPHY` solid of type 0 is built whatever its magic says** (B404). `FUN_18000a100` compares the tag at
    /// `+0`, reads the type as the signed word at `+6` (`MOVSX ECX, word ptr [RDI + 0x6]`) and hands `+0x1C` to
    /// `FUN_18000bcf0` without looking further; nothing that builds the collide reads the surface's `+0x2C` either
    /// (`docs/findings/51`, *What the loader does with a solid it cannot use*). The word at `+4` carries the `0x0100`
    /// every shipped solid does, so a reader taking the type from there refuses the solid.
    /// </remarks>
    [TestCase("MOPP")]
    [TestCase("QQQQ")]
    public void Read_ATaggedSolidOfTypeZero_BuildsWhateverItsMagicSays(string magic)
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)], magic: Encoding.ASCII.GetBytes(magic));

        PhysicsHull.Read(solid).Count.ShouldBe(1);
        PhysicsHull.MassProperties(solid).ShouldNotBeNull();
    }

    /// <remarks>
    /// **Type 1 is `DevMsg(2, "Null physics model")` and a NULL collide** (B404): no hull, and no surface for the
    /// mass properties to come from. The word at `+4` is left zero so a reader taking the type from there builds it.
    /// </remarks>
    [Test]
    public void Read_ATaggedSolidOfTypeOne_IsANullPhysicsModel()
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)], version: 0, type: 1);

        PhysicsHull.Read(solid).ShouldBeEmpty();
        PhysicsHull.MassProperties(solid).ShouldBeNull();
    }

    /// <remarks>
    /// **Any other type is NULL too, silently** (B404) — `FUN_18000a100` builds only on zero, prints only on one,
    /// and the comparison is signed, so a negative word is "other" rather than a large type. The word at `+4` is left
    /// zero, as in the case above, so a reader taking the type from there builds it.
    /// </remarks>
    [TestCase(2)]
    [TestCase(-1)]
    public void Read_ATaggedSolidOfAnotherType_IsNull(int type)
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)], version: 0, type: (short)type);

        PhysicsHull.Read(solid).ShouldBeEmpty();
        PhysicsHull.MassProperties(solid).ShouldBeNull();
    }

    /// <remarks>
    /// **A tagged solid's surface is the `+8` data size, not the rest of the blob** (B404). The loader passes
    /// `dword ptr [RDI + 0x8]` to `FUN_18000bcf0` as the length it copies into the collide, and never compares it
    /// with the size prefix — so ledges written past it are not in the engine's copy. Here the size covers the
    /// surface header and stops before the ledge tree: the mass properties read, the hull does not.
    /// </remarks>
    [Test]
    public void Read_ATaggedSolidWhoseDataSizeStopsBeforeItsLedges_ReadsOnlyTheSurface()
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)], massCenter: new Vector3(0.1f, -0.2f, 0.3f), dataSize: 0x30);

        PhysicsHull.Read(solid).ShouldBeEmpty();

        PhysicsMassProperties? properties = PhysicsHull.MassProperties(solid);

        properties.ShouldNotBeNull();
        properties.Value.MassCenter.ShouldBe(new Vector3(0.1f, -0.2f, 0.3f));
    }

    /// <remarks>
    /// **A solid with no `VPHY` tag IS its compact surface, from its first byte** (B404) — `FUN_18000a100` passes
    /// the solid's own address to `FUN_18000bcf0` when the magic at `+0x2C` is `IVPS` or `SPVI`, and when it is
    /// zero it prints `"Old format .PHY file loaded!!!"` and builds the same way. The points and the mass center are
    /// distinct values, so a reader still taking the surface from `+0x1C` reads different numbers or none.
    /// </remarks>
    [TestCase(0x53505649)] // IVPS
    [TestCase(0x49565053)] // SPVI
    [TestCase(0)]
    public void Read_AnUntaggedSolidWithALoadableMagic_BuildsFromItsFirstByte(int magic)
    {
        byte[] solid = Solid(
            Triangle,
            [(0, 1, 2)],
            magic: BitConverter.GetBytes(magic),
            massCenter: new Vector3(0.1f, -0.2f, 0.3f),
            rotationInertia: new Vector3(0.004f, 0.005f, 0.006f),
            tagged: false);

        IReadOnlyList<PhysicsLedge> ledges = PhysicsHull.Read(solid);

        ledges.Count.ShouldBe(1);
        ledges[0].Points[0].ShouldBe(new Vector3(1f, 2f, 3f));
        ledges[0].Points[2].ShouldBe(new Vector3(7f, 8f, 9f));

        PhysicsMassProperties? properties = PhysicsHull.MassProperties(solid);

        properties.ShouldNotBeNull();
        properties.Value.MassCenter.ShouldBe(new Vector3(0.1f, -0.2f, 0.3f));
        properties.Value.RotationInertia.ShouldBe(new Vector3(0.004f, 0.005f, 0.006f));
    }

    /// <remarks>
    /// **An untagged solid whose magic is `MOPP`, or anything else the loader does not name, is NULL** (B404).
    /// `MOPP` is Havok's own tree and a different structure entirely — reading it as `IVPS` would not fail, it would
    /// produce triangles out of unrelated bytes. The loader compares it first and nulls the collide; an unknown magic
    /// falls through every comparison to the same answer.
    /// </remarks>
    [TestCase("MOPP")]
    [TestCase("QQQQ")]
    public void Read_AnUntaggedSolidWithMoppOrAnUnknownMagic_IsNull(string magic)
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)], magic: Encoding.ASCII.GetBytes(magic), tagged: false);

        PhysicsHull.Read(solid).ShouldBeEmpty();
        PhysicsHull.MassProperties(solid).ShouldBeNull();
    }

    /// <remarks>
    /// **A solid with no `VPHY` tag and fewer than `0x30` bytes is `Error("Corrupt physics model")`** (B404) —
    /// `CMP R14D, 0x30; JC` on the size prefix, before the magic is read, and `Error` does not return. The reader
    /// reports it rather than throwing, so each caller decides what a refused file costs; its hull and mass
    /// properties are nothing.
    /// </remarks>
    [TestCase(0)]
    [TestCase(4)]
    [TestCase(0x2F)]
    public void Load_AnUntaggedSolidUnder0x30Bytes_IsCorrupt(int size)
    {
        byte[] solid = new byte[size];

        PhysicsHull.Load(solid).ShouldBe(PhysicsSolidLoad.Corrupt);
        PhysicsHull.Read(solid).ShouldBeEmpty();
        PhysicsHull.MassProperties(solid).ShouldBeNull();
    }

    /// <remarks>
    /// **The boundary: `JC` is unsigned below, so exactly `0x30` bytes is not corrupt** (B404). A bare surface with
    /// the `IVPS` magic and no ledges is built — its mass properties read, and there is no tree to walk.
    /// </remarks>
    [Test]
    public void Load_AnUntaggedSolidOfExactly0x30Bytes_IsACollide()
    {
        byte[] solid = new byte[0x30];

        BitConverter.GetBytes(0.25f).CopyTo(solid, 0x00);
        "IVPS"u8.CopyTo(solid.AsSpan(0x2C));

        PhysicsHull.Load(solid).ShouldBe(PhysicsSolidLoad.Collide);
        PhysicsHull.MassProperties(solid)!.Value.MassCenter.X.ShouldBe(0.25f);
    }

    /// <remarks>
    /// **What each of the loader's branches builds, through the one call a caller makes to ask** (B404). The rows
    /// above pin the hull and the mass properties; this pins that the classification agrees with them, since
    /// <see cref="PhysicsModel"/> and the map reader act on it.
    /// </remarks>
    [Test]
    public void Load_EachOfTheLoadersBranches_IsClassifiedAsItBuilds()
    {
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], magic: "MOPP"u8)).ShouldBe(PhysicsSolidLoad.Collide);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], type: 1)).ShouldBe(PhysicsSolidLoad.Null);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], type: 2)).ShouldBe(PhysicsSolidLoad.Null);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], magic: "MOPP"u8, tagged: false)).ShouldBe(PhysicsSolidLoad.Null);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], tagged: false)).ShouldBe(PhysicsSolidLoad.Collide);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], magic: [0, 0, 0, 0], tagged: false))
            .ShouldBe(PhysicsSolidLoad.Collide);
        PhysicsHull.Load(Solid(Triangle, [(0, 1, 2)], magic: "QQQQ"u8, tagged: false)).ShouldBe(PhysicsSolidLoad.Null);
    }

    /// <remarks>
    /// **A tagged solid too short for its own 0x1C-byte header carries no surface (D32).** The loader reads the type
    /// and data size and copies from `+0x1C` without checking, which past the blob is someone else's bytes; this
    /// answers NULL rather than read them. Never corrupt, because the size check belongs to the untagged branch.
    /// </remarks>
    [TestCase(4)]
    [TestCase(0x1B)]
    public void Load_ATaggedSolidTooShortForItsHeader_IsNull(int size)
    {
        byte[] solid = new byte[size];

        "VPHY"u8.CopyTo(solid);

        PhysicsHull.Load(solid).ShouldBe(PhysicsSolidLoad.Null);
        PhysicsHull.Read(solid).ShouldBeEmpty();
    }

    /// <remarks>
    /// **A `.phy` is a stranger's file (D32).** A triangle count that runs past the blob must read
    /// nothing rather than throw out of the arithmetic — the same guard every other reader here
    /// carries, and the one a fuzzer reaches first.
    /// </remarks>
    [Test]
    public void Read_WithATriangleCountPastTheEnd_ReadsNothing()
    {
        byte[] solid = Solid(Triangle, [(0, 1, 2)]);

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
            Triangle,
            [(0, 1, 2)],
            massCenter: new Vector3(0.1f, -0.2f, 0.3f),
            rotationInertia: new Vector3(0.004f, 0.005f, 0.006f));

        PhysicsMassProperties? properties = PhysicsHull.MassProperties(solid);

        properties.ShouldNotBeNull();
        properties.Value.MassCenter.ShouldBe(new Vector3(0.1f, -0.2f, 0.3f));
        properties.Value.RotationInertia.ShouldBe(new Vector3(0.004f, 0.005f, 0.006f));
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
            Triangle,
            [(0, 1, 2)],
            edgeOffsets: [(-3, 2, 16383)],
            highBit: true);

        PhysicsLedge ledge = PhysicsHull.Read(solid)[0];

        ledge.Triangles[0].ShouldBe((0, 1, 2), "the start points are the low sixteen bits still");
        ledge.EdgeOffsets[0].ShouldBe((-3, 2, 16383));
    }

    /// <summary>Where the ledge is written in a tagged solid, from its <c>VPHY</c> tag.</summary>
    private const int LedgeAt = 0x1C + 0x30;

    /// <summary>
    /// Builds one solid blob: surface, then one ledge, its triangles and its points, then the tree.
    /// </summary>
    /// <remarks>
    /// **Laid out in the order a real file uses** — the ledges come BEFORE the tree, which is why
    /// a leaf's ledge offset is negative in every shipped file and why writing them the tidy way
    /// round would leave the reader's sign handling untested.
    ///
    /// **Tagged, the solid opens with the 0x1C-byte `VPHY` header, written as TF2 writes it** — the tag, a word the
    /// loader does not read and every one of 36,917 shipped solids sets to `0x0100`, the type word, and the data size,
    /// which every shipped solid sets to everything after the header (the `phy-solids` probe). Untagged, the surface
    /// is the solid's first byte.
    /// </remarks>
    private static byte[] Solid(
        Vector3[] points,
        (int A, int B, int C)[] triangles,
        ReadOnlySpan<byte> magic = default,
        Vector3 massCenter = default,
        Vector3 rotationInertia = default,
        (int A, int B, int C)[]? edgeOffsets = null,
        bool highBit = false,
        bool tagged = true,
        short version = 0x100,
        short type = 0,
        int? dataSize = null)
    {
        int surfaceAt = tagged ? 0x1C : 0;
        int ledgeAt = surfaceAt + 0x30;
        int trianglesAt = ledgeAt + 0x10;
        int pointsAt = trianglesAt + (triangles.Length * 0x10);
        int treeAt = pointsAt + (points.Length * 0x10);

        byte[] solid = new byte[treeAt + 0x1C];

        if (tagged)
        {
            "VPHY"u8.CopyTo(solid);
            BitConverter.GetBytes(version).CopyTo(solid, 0x04);
            BitConverter.GetBytes(type).CopyTo(solid, 0x06);
            BitConverter.GetBytes(dataSize ?? solid.Length - 0x1C).CopyTo(solid, 0x08);
        }

        // IVP_Compact_Surface: the mass center and rotation inertia lead it, then the tree offset and the
        // magic, all relative to the surface's own base rather than to the file.
        BitConverter.GetBytes(massCenter.X).CopyTo(solid, surfaceAt + 0x00);
        BitConverter.GetBytes(massCenter.Y).CopyTo(solid, surfaceAt + 0x04);
        BitConverter.GetBytes(massCenter.Z).CopyTo(solid, surfaceAt + 0x08);
        BitConverter.GetBytes(rotationInertia.X).CopyTo(solid, surfaceAt + 0x0C);
        BitConverter.GetBytes(rotationInertia.Y).CopyTo(solid, surfaceAt + 0x10);
        BitConverter.GetBytes(rotationInertia.Z).CopyTo(solid, surfaceAt + 0x14);
        BitConverter.GetBytes(treeAt - surfaceAt).CopyTo(solid, surfaceAt + 0x20);
        (magic.IsEmpty ? "IVPS"u8 : magic).CopyTo(solid.AsSpan(surfaceAt + 0x2C));

        // The ledge: its point array is addressed as a delta from the ledge itself.
        BitConverter.GetBytes(pointsAt - ledgeAt).CopyTo(solid, ledgeAt);
        BitConverter.GetBytes(triangles.Length).CopyTo(solid, ledgeAt + 0x0C);

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
        BitConverter.GetBytes(ledgeAt - treeAt).CopyTo(solid, treeAt + 4);

        return solid;
    }

    /// <summary>One edge word: a start point, a fifteen-bit offset above it, and optionally bit 31.</summary>
    private static uint Edge(int start, int offset, bool high) =>
        (uint)(start & 0xFFFF) | (((uint)offset & 0x7FFF) << 16) | (high ? 0x8000_0000u : 0u);
}
