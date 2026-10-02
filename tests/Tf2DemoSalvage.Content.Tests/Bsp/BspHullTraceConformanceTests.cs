using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A box with three extents swept through brushes and terrain — `Ray_t::m_Extents` (`cmodel.h:66`).
/// </summary>
/// <remarks>
/// **Valve's ray keeps one half-extent per axis**: `Ray_t::Init( start, end, mins, maxs )` sets
/// `m_Extents = (maxs − mins) · 0.5` and moves `m_Start` to the box's centre (`cmodel.h:84-96`). A player's hull is
/// 48 × 48 × 82 standing (`tf_gamerules.cpp:1313`), so a cube cannot stand in for it.
///
/// **How a box meets a plane is not in the published SDK** — `engine/cmodel.cpp` is absent from it. The formula is the one
/// every id-derived collision model uses, published in Quake 2's `CM_ClipBoxToBrush` and `CM_RecursiveHullCheck`
/// (`qcommon/cmodel.c`): the plane is pushed out by the box's support along the normal,
/// `|ex·nx| + |ey·ny| + |ez·nz|`. *Evidence class: published source of the ancestor engine, interpolated to Source.*
///
/// **One tilted half-space**, so every axis's extent moves the answer and swapping two of them moves it by a computed amount.
/// </remarks>
public sealed class BspHullTraceConformanceTests
{
    /// <summary>Valve's `DIST_EPSILON`.</summary>
    private const float Epsilon = 0.03125f;

    [TestCase(0.6f, 0f, 0.8f, TestName = "Trace_ABoxIntoAPlaneTiltedAlongX_IsPushedOutByItsXAndZExtents")]
    [TestCase(0f, 0.6f, 0.8f, TestName = "Trace_ABoxIntoAPlaneTiltedAlongY_IsPushedOutByItsYAndZExtents")]
    public void Trace_ABoxIntoATiltedPlane_IsPushedOutByEachAxissExtent(float nx, float ny, float nz)
    {
        (float X, float Y, float Z) extents = (10f, 20f, 30f);
        float push = (nx * extents.X) + (ny * extents.Y) + (nz * extents.Z);

        // From z = 100 to −100 the plane's side runs 80 to −80, so the entry is (80 − push − ε) / 160.
        BspTrace trace = World(nx, ny, nz, 0x1).Trace(0f, 0f, 100f, 0f, 0f, -100f, extents);

        trace.Fraction.ShouldBe((80f - push - Epsilon) / 160f, 1e-5f);
    }

    [Test]
    public void Trace_EqualExtents_MatchesTheCubeTrace()
    {
        BspLeafTree world = World(0.6f, 0f, 0.8f, 0x1);

        world.Trace(0f, 0f, 100f, 0f, 0f, -100f, (12f, 12f, 12f))
            .ShouldBe(world.Trace(0f, 0f, 100f, 0f, 0f, -100f, halfExtent: 12f));
    }

    [Test]
    public void Trace_StartingInsideAndLeaving_IsStartSolidButNotAllSolid()
    {
        BspTrace trace = World(0f, 0f, 1f, 0x1).Trace(0f, 0f, -10f, 0f, 0f, 50f, (0f, 0f, 0f));

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeFalse();
    }

    [Test]
    public void Trace_FromOutsideIntoTheBrush_IsNotStartSolid()
    {
        World(0f, 0f, 1f, 0x1).Trace(0f, 0f, 100f, 0f, 0f, -100f, (0f, 0f, 0f)).StartSolid.ShouldBeFalse();
    }

    [Test]
    public void Trace_PlayerSolidThroughAPlayerClip_Stops()
    {
        // `MASK_PLAYERSOLID` adds `CONTENTS_PLAYERCLIP` to `MASK_SOLID` (`bspflags.h:108`); a camera passes a player clip.
        BspLeafTree world = World(0f, 0f, 1f, 0x10000);

        world.Trace(0f, 0f, 100f, 0f, 0f, -100f, (0f, 0f, 0f), mask: BspLeafTree.MaskPlayerSolid).Fraction.ShouldBeLessThan(1f);
        world.Trace(0f, 0f, 100f, 0f, 0f, -100f, (0f, 0f, 0f)).Fraction.ShouldBe(1f);
    }

    [Test]
    public void SweepSurface_ABoxWithATallZExtentOntoTerrain_StopsByThatExtent()
    {
        // The square at z = 0; a box reaching 20 down from its centre meets it 20 sooner: (50 − 20) / 100.
        Ground().SweepSurface(10f, 10f, 50f, 10f, 10f, -50f, (1f, 1f, 20f), BspLeafTree.MaskSolid).Fraction
            .ShouldBe(0.30f, 0.001f);
    }

    [Test]
    public void SweepSurface_ABoxReachingOverTheEdgeByItsXExtent_IsStopped()
    {
        // Centred 15 off the square's edge, a box 20 wide in x reaches 5 onto it; the bounds cull must use x's extent.
        Ground().SweepSurface(-15f, 50f, 50f, -15f, 50f, -50f, (20f, 1f, 1f), BspLeafTree.MaskSolid).Fraction
            .ShouldBeLessThan(1f);
    }

    [Test]
    public void SweepSurface_AMaskWithoutSolid_PassesThroughTerrain()
    {
        // Terrain is `CONTENTS_SOLID`; a mask that does not include it is not stopped by it.
        Ground().SweepSurface(10f, 10f, 50f, 10f, 10f, -50f, (1f, 1f, 1f), BspLeafTree.ContentsWater).Fraction.ShouldBe(1f);
    }

    /// <summary>One square of terrain at z = 0, 100 on a side.</summary>
    private static DisplacementCollision Ground() => DisplacementCollision.FromTriangles(
    [
        (7, (IReadOnlyList<SurfaceVertex>)
        [
            new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new(100f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new(0f, 100f, 0f, 0f, 0f, 0f, 0f, 0f),
            new(100f, 0f, 0f, 0f, 0f, 0f, 0f, 0f), new(100f, 100f, 0f, 0f, 0f, 0f, 0f, 0f), new(0f, 100f, 0f, 0f, 0f, 0f, 0f, 0f),
        ]),
    ]);

    /// <summary>A world of one half-space through the origin with the normal and contents given, as the mask suite builds.</summary>
    private static BspLeafTree World(float nx, float ny, float nz, int contents)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), nx);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(4), ny);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), nz);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -2);

        byte[] leaves = new byte[64];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), 1);

        byte[] leafBrushes = new byte[2];
        byte[] brushes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(8), contents);

        byte[] brushSides = new byte[8];

        return BspLeafTree.FromCollisionLumps(node, plane, leaves, leafBrushes, brushes, brushSides);
    }
}
