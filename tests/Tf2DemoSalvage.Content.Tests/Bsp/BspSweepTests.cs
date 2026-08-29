using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <see cref="BspLeafTree.Sweep"/> stops where the solid is, and says how far it got.
/// </summary>
/// <remarks>
/// **Exact predictions, because the geometry is chosen so there is one right answer.** The fixture
/// is a single plane at z = 0 with normal (0, 0, 1): everything above is empty, everything below is
/// solid. A sweep straight down from 100 to −100 travels 200 units and must stop at z = 0, which is
/// a fraction of exactly 0.5 — not "less than one", which is the assertion that would pass against
/// a trace that stopped anywhere at all.
///
/// **The box cases are what separate this from a ray**, and their answers are arithmetic rather
/// than tolerance: a box of half-extent 6 sweeping the same 200 units stops with its face on the
/// plane, at z = 6, which is (100 − 6) / 200 = 0.47.
///
/// This is the measurement <see cref="BspLeafTree.IsClear"/> cannot make. That one samples in
/// four-unit steps and answers a bool; the chase camera needs a distance, and a sampled walk can
/// tunnel through a thin wall besides.
/// </remarks>
public sealed class BspSweepTests
{
    [Test]
    public void Sweep_StraightDownThroughTheFloor_StopsExactlyAtThePlane()
    {
        Floor().Sweep(0f, 0f, 100f, 0f, 0f, -100f, halfExtent: 0f)
            .ShouldBe(0.5f, 0.001f);
    }

    [Test]
    public void Sweep_EntirelyInOpenAir_ReportsNothingInTheWay()
    {
        // **The control, and without it every assertion above is satisfied by "always blocked".**
        Floor().Sweep(0f, 0f, 100f, 0f, 0f, 50f, halfExtent: 0f)
            .ShouldBe(1f, 0.001f);
    }

    [Test]
    public void Sweep_WithABox_StopsAHalfExtentShortOfThePlane()
    {
        // A box cannot put its centre on the surface: its face gets there first. 100 down to −100 is
        // 200 units, and a half-extent of 6 stops the centre at z = 6, so (100 − 6) / 200.
        Floor().Sweep(0f, 0f, 100f, 0f, 0f, -100f, halfExtent: 6f)
            .ShouldBe(0.47f, 0.001f);
    }

    [Test]
    public void Sweep_WithABoxThatWouldFitAsARay_IsStoppedAnyway()
    {
        // **The case that distinguishes a hull sweep from a ray sweep.** A ray ending at z = 3 never
        // reaches the plane and is clear; a 6-unit box ending there has already pushed through it.
        BspLeafTree floor = Floor();

        floor.Sweep(0f, 0f, 100f, 0f, 0f, 3f, halfExtent: 0f)
            .ShouldBe(1f, 0.001f, "a bare ray stopping above the plane never touches it");

        floor.Sweep(0f, 0f, 100f, 0f, 0f, 3f, halfExtent: 6f)
            .ShouldBeLessThan(1f, "a six-unit box reaches the plane before its centre does");
    }

    [Test]
    public void Sweep_ThroughAMapWithNoTree_ReportsClear()
    {
        // Matching IsClear and SeesSky: a viewer that decided everything was blocked would pin every
        // camera to its subject, which is worse than occasionally passing through a corner.
        BspLeafTree.FromLumps(Array.Empty<byte>(), Array.Empty<byte>())
            .Sweep(0f, 0f, 0f, 0f, 0f, -100f, halfExtent: 6f)
            .ShouldBe(1f, 0.001f);
    }

    [Test]
    public void Sweep_StartingInsideTheSolid_ReportsNoProgress()
    {
        // Starting solid is a real state — a camera shoved into a wall by a moving player — and the
        // honest answer is that it got nowhere, not that it is free to travel.
        Floor().Sweep(0f, 0f, -50f, 0f, 0f, -100f, halfExtent: 0f)
            .ShouldBe(0f, 0.001f);
    }

    /// <summary>A world that is empty above z = 0 and solid below it.</summary>
    /// <remarks>
    /// Leaf 0 is the front child (above the plane) and leaf 1 the back child, with
    /// <c>CONTENTS_SOLID</c> on leaf 1. The plane's normal is (0, 0, 1) at distance 0, so "front"
    /// really is "up".
    /// </remarks>
    private static BspLeafTree Floor()
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(12), 0f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        byte[] leaves = new byte[128];

        BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(32), 1);

        return BspLeafTree.FromLumps(node, plane, leaves);
    }
}
