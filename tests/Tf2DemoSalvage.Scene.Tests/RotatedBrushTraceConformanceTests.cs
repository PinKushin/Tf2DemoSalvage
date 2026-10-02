using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A box traced against a brush entity whose networked angles are not zero — <c>CM_TransformedBoxTrace</c> (B450).
/// </summary>
/// <remarks>
/// **Read in disassembly, <c>engine.dll</c> (x64) <c>FUN_18016ab60</c>**, the only caller of <c>CM_BoxTrace</c> besides
/// <c>CEngineTrace::TraceRay</c>'s world pass (<c>FUN_18018e870</c>, <c>enginetrace.cpp:0x68e</c>). With <c>Ray_t</c> laid
/// out as <c>cmodel.h:61-66</c> — start, delta, start offset, extents — it does:
/// <list type="bullet">
/// <item>angles all zero (<c>18016abba-18016abd8</c>): start − origin, delta and extents as they are;</item>
/// <item>otherwise <c>AngleMatrix( angles, origin )</c> (<c>FUN_180276390</c>), the delta through <c>VectorIRotate</c>
/// (<c>FUN_1802795a0</c>), the ray's real start — <c>m_Start + m_StartOffset</c>, the box's origin — through
/// <c>VectorITransform</c> (<c>FUN_180279620</c>), then <c>m_StartOffset</c> taken off again unrotated
/// (<c>18016ac79-18016aca1</c>). **The extents are copied untouched**: the box keeps its own axes in the model's frame, so
/// in the world it is turned with the model — an oriented box, not a grown one;</item>
/// <item>after the trace, a fraction other than 1 and a rotation put <c>plane.normal</c> back through
/// <c>VectorRotate</c> (<c>FUN_1802796c0</c>, <c>18016ad1e</c>); <c>plane.dist</c>, <c>startsolid</c> and <c>allsolid</c>
/// are left as the local trace gave them, and the end position is recomputed from the world ray (<c>FUN_180169540</c>).</item>
/// </list>
/// *Evidence class: read from the shipped binary.* <c>IEngineTrace::ClipRayToEntity</c> (<c>IEngineTrace.h:142</c>) is the
/// published face of it; the SDK does not ship <c>cmodel.cpp</c>.
///
/// **The model is a slab, <c>|x| ≤ 4</c>, unbounded on y and z**, so a turn moves which way it faces and nothing else.
/// </remarks>
public sealed class RotatedBrushTraceConformanceTests
{
    private const float Epsilon = 0.03125f;
    private const int Door = 7;

    private static readonly (float X, float Y, float Z) Mins = (-24f, -24f, 0f);
    private static readonly (float X, float Y, float Z) Maxs = (24f, 24f, 82f);

    [Test]
    public void TraceHull_ASlabPitchedFlat_StopsTheDroppingBoxByItsLocalExtent()
    {
        // Pitch 90 lays the slab flat at |z| ≤ 4 (forward is (0, 0, −1), mathlib_base.cpp:1219-1221). Feet from z 100 to
        // −100 are local x −100 to 100, the box centre's local x is the feet's — the offset is re-added unrotated, along
        // local z — and the box reaches 24 along local x. It meets x ≥ −4 at x = −28: (72 − ε) / 200.
        BspTrace trace = Level().TraceHull(
            (100f, 0f, 100f), (100f, 0f, -100f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)]).ShouldNotBeNull();

        trace.Fraction.ShouldBe((72f - Epsilon) / 200f, 1e-6f);
        trace.BrushEntity.ShouldBe(Door);
    }

    [Test]
    public void TraceHull_TheSameSlabUnturned_DoesNotStopTheBoxBesideIt()
    {
        // The control: upright, |x| ≤ 4 never comes within 24 of x = 100.
        Level().TraceHull((100f, 0f, 100f), (100f, 0f, -100f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 0f, 0f)])
            .ShouldNotBeNull().Fraction.ShouldBe(1f);
    }

    [Test]
    public void TraceHull_ASlabPitchedFlat_ReturnsTheWorldNormal()
    {
        // Local (−1, 0, 0) through VectorRotate is −forward, (0, 0, 1): the floor's normal, not the model's.
        BspTrace trace = Level().TraceHull(
            (100f, 0f, 100f), (100f, 0f, -100f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)]).ShouldNotBeNull();

        trace.Normal.X.ShouldBe(0f, 1e-6f);
        trace.Normal.Y.ShouldBe(0f, 1e-6f);
        trace.Normal.Z.ShouldBe(1f, 1e-6f);
    }

    [Test]
    public void TraceHull_ASlabTurnedByYaw_LetsThroughTheBoxTheUnturnedOneStops()
    {
        // Yaw 90 turns |x| ≤ 4 into |y| ≤ 4; a box crossing x at y = 100 meets the upright slab and misses the turned one.
        MapLevel level = Level();

        level.TraceHull((-100f, 100f, 0f), (100f, 100f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 0f, 0f)])
            .ShouldNotBeNull().Fraction.ShouldBe((100f - 24f - 4f - Epsilon) / 200f, 1e-6f);
        level.TraceHull((-100f, 100f, 0f), (100f, 100f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)])
            .ShouldNotBeNull().Fraction.ShouldBe(1f);
    }

    [Test]
    public void TraceHull_ASlabTurnedByYaw_StopsTheBoxCrossingItsNewFace()
    {
        // Along y at x = 100: the turned slab stops it at y = −28 (72 − ε of 200), with local (−1, 0, 0) back
        // as −forward, (0, −1, 0): the side facing −y.
        BspTrace trace = Level().TraceHull(
            (100f, -100f, 0f), (100f, 100f, 0f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)]).ShouldNotBeNull();

        trace.Fraction.ShouldBe((72f - Epsilon) / 200f, 1e-6f);
        trace.Normal.X.ShouldBe(0f, 1e-6f);
        trace.Normal.Y.ShouldBe(-1f, 1e-6f);
        trace.Normal.Z.ShouldBe(0f, 1e-6f);
    }

    [Test]
    public void TraceHull_StartingInsideATurnedSlabAndLeaving_IsStartSolidButNotAllSolid()
    {
        // Feet at z 0 put the box across the flat slab; rising 200 takes it out. CM_BoxTrace's flags pass through untouched.
        BspTrace trace = Level().TraceHull(
            (0f, 0f, 0f), (0f, 0f, 200f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)]).ShouldNotBeNull();

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeFalse();
        trace.BrushEntity.ShouldBe(Door);
    }

    [Test]
    public void TraceHull_StayingInsideATurnedSlab_IsAllSolid()
    {
        BspTrace trace = Level().TraceHull(
            (0f, 0f, 0f), (0f, 0f, 1f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [Slab(0f, 90f, 0f)]).ShouldNotBeNull();

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeTrue();
    }

    [Test]
    public void TraceHull_ATurnedSlabOffItsOrigin_TurnsAboutThatOrigin()
    {
        // AngleMatrix( angles, origin ): placed at z = 50 and pitched flat, the slab is |z − 50| ≤ 4, so the feet stop at
        // z = 78 — 22 of 200 from 100.
        SolidBrush placed = Slab(0f, 90f, 0f) with { Origin = new Vector3(0f, 0f, 50f) };

        Level().TraceHull((100f, 0f, 100f), (100f, 0f, -100f), Mins, Maxs, BspLeafTree.MaskPlayerSolid, [placed])
            .ShouldNotBeNull().Fraction.ShouldBe((22f - Epsilon) / 200f, 1e-6f);
    }

    private static SolidBrush Slab(float pitch, float yaw, float roll) =>
        new(HeadNode: 1, Origin: Vector3.Zero, Entity: Door, Angles: new Vector3(pitch, yaw, roll));

    /// <summary>An empty world at node 0 and one submodel at node 1: a brush of two sides, x ≤ 4 and −x ≤ 4.</summary>
    private static MapLevel Level()
    {
        byte[] planes = new byte[60];

        Plane(planes, 0, 1f, 0f, 0f, 4f);
        Plane(planes, 1, -1f, 0f, 0f, 4f);
        Plane(planes, 2, 0f, 0f, 1f, -100000f); // every point is in front of it

        byte[] nodes = new byte[64];

        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(0), 2);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(8), -1);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(36), -2);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(40), -2);

        byte[] leaves = new byte[64];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), 1);

        byte[] leafBrushes = new byte[2];
        byte[] brushes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(8), 0x1);

        byte[] sides = new byte[16];

        BinaryPrimitives.WriteUInt16LittleEndian(sides.AsSpan(8), 1);

        BspLeafTree tree = BspLeafTree.FromCollisionLumps(nodes, planes, leaves, leafBrushes, brushes, sides);

        return new MapLevel(
            null, null, null, new Dictionary<int, string>(), tree, null, null, [], [], [], [], null, default);
    }

    private static void Plane(byte[] planes, int index, float x, float y, float z, float distance)
    {
        Span<byte> at = planes.AsSpan(index * 20);

        BinaryPrimitives.WriteSingleLittleEndian(at, x);
        BinaryPrimitives.WriteSingleLittleEndian(at[4..], y);
        BinaryPrimitives.WriteSingleLittleEndian(at[8..], z);
        BinaryPrimitives.WriteSingleLittleEndian(at[12..], distance);
    }
}
