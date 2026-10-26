using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What a decal projection costs per vertex, apart from what it answers (the conformance suite holds that).</summary>
public sealed class StudioDecalProjectionTests
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <remarks>
    /// B599: the projection runs per frame for every model decal, and the per-vertex bone walk was a `yield` iterator, an
    /// enumerator per vertex — ~0.1 MB a frame on f12 gummo (measurements/2026-10-25-allocation-attribution.md). The
    /// per-call arrays (the frame, one matrix a bone) are the same for one vertex as for a thousand, so the two calls
    /// allocate exactly the same bytes or something is allocated per vertex.
    /// </remarks>
    [Test]
    public void Census_AThousandVerticesAgainstOne_AllocatesNoMore()
    {
        WorldVertex[] one = [Corner(0f, 1f, 1f)];
        WorldVertex[] many = new WorldVertex[1000];
        Array.Fill(many, Corner(0f, 1f, 1f));

        // Warm: the first call JITs.
        Census(one);
        Census(many);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Census(one);
        long forOne = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        (int facing, _, _) = Census(many);
        long forMany = GC.GetAllocatedBytesForCurrentThread() - before;

        // Control: the thousand were all projected, so a per-vertex allocation had a thousand chances.
        facing.ShouldBe(1000);
        forMany.ShouldBe(forOne);
    }

    /// <remarks>Three weights summing to one over three bones each moving y by 0, 2 and 4: u = (0·0.5 + 2·0.25 + 4·0.25) / 4 · 0.5 + 0.5.</remarks>
    [Test]
    public void Census_AVertexOnThreeWeightedBones_ProjectsTheWeightedSum()
    {
        float[] up2 = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 2f, 0f, 0f, 1f, 0f];
        float[] up4 = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 4f, 0f, 0f, 1f, 0f];
        WorldVertex vertex = Corner(0f, 0f, 0f) with { BoneB = 1f, BoneC = 2f, WeightA = 0.5f, WeightB = 0.25f, WeightC = 0.25f };

        // Census reports the distance from the decal's axis: v is 0, so it is |u'| = 1.5 in world units.
        (int facing, int inside, float nearest) = StudioDecalProjection.Census(
            [vertex], [Identity, up2, up4], new Vector3(100f, 0f, 0f), new Vector3(-110f, 0f, 0f), radius: 4f);

        (facing, inside).ShouldBe((1, 1));
        nearest.ShouldBe(1.5f, 1e-6f);
    }

    private static (int Facing, int Inside, float Nearest) Census(WorldVertex[] vertices) =>
        StudioDecalProjection.Census(vertices, [Identity], new Vector3(100f, 0f, 0f), new Vector3(-110f, 0f, 0f), radius: 4f);

    // A packed model vertex: model-space z rides in Depth.
    private static WorldVertex Corner(float x, float y, float z) =>
        new(x, y, z, 0f, 0f, 0f, 0f, 0f, NormalX: 1f, NormalY: 0f, NormalZ: 0f, WeightA: 1f);
}
