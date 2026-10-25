using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The per-list matrix pool the instance build writes into in place (B585).</summary>
/// <remarks>
/// The engine builds the model-to-world matrix every draw into a stack <c>ClientModelRenderInfo_t</c>
/// (<c>c_baseanimating.cpp:3464</c>, <c>AngleMatrix</c> at <c>:3491</c>): no heap, no compare. These hold the
/// same three properties for ours: identical contents, no steady-state allocation, and a matrix that is
/// rewritten only when the list it was handed in is rebuilt.
/// </remarks>
public sealed class InstanceMatricesTests
{
    [Test]
    public void Next_RandomTransforms_AreBitIdenticalToToMatrix()
    {
        uint random = 585u;
        InstanceMatrices pool = new();

        for (int frame = 0; frame < 4; frame++)
        {
            pool.Reset();

            for (int i = 0; i < 300; i++)
            {
                PropTransform transform = new(
                    Coordinate(ref random), Coordinate(ref random), Coordinate(ref random),
                    Angle(ref random), Angle(ref random), Angle(ref random),
                    Unit(ref random) * 4f);

                float[] pooled = pool.Next(transform);
                float[] fresh = transform.ToMatrix();

                for (int k = 0; k < 16; k++)
                {
                    BitConverter.SingleToInt32Bits(pooled[k]).ShouldBe(BitConverter.SingleToInt32Bits(fresh[k]));
                }
            }
        }
    }

    [Test]
    public void Next_AfterWarmUp_AllocatesNothing()
    {
        InstanceMatrices pool = new();
        PropTransform transform = new(1f, 2f, 3f, 10f, 20f, 30f, 1f);

        Fill(pool, transform, 300);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Fill(pool, transform, 300);
        long after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0L);
    }

    [Test]
    public void Next_WithinOnePass_HandsOutDistinctArrays()
    {
        // Growing past capacity must keep the arrays already handed out this pass, which the list still holds.
        InstanceMatrices pool = new();
        HashSet<float[]> seen = new(ReferenceEqualityComparer.Instance);

        pool.Reset();

        for (int i = 0; i < 100; i++)
        {
            float[] matrix = pool.Next(new PropTransform(i, 0f, 0f, 0f, 0f, 0f, 1f));
            seen.Add(matrix).ShouldBeTrue();
        }

        seen.Count.ShouldBe(100);
    }

    [Test]
    public void Instances_AnotherListsPass_LeavesThisListsMatricesUntouched()
    {
        // **The hand-off contract** (B585): the world, undrawn and viewmodel lists are alive at once and the
        // device reads all three. A matrix is rewritten only when the list that holds it is rebuilt — which
        // is after `FinishDrawBeforePose` (`ViewerPresenter.Device.cs` `PoseOver`), the wait that already
        // makes reusing the lists themselves safe.
        EntityModelSet models = new();
        List<ModelInstance> world = [];
        List<ModelInstance> other = [];

        models.Add([Prop(10f, 1), Prop(20f, 2)], OneTriangle);
        models.Instances([Prop(10f, 1), Prop(20f, 2)], world);
        float[][] contents = [[.. world[0].Matrix], [.. world[1].Matrix]];

        models.Instances([Prop(70f, 1), Prop(80f, 2)], other);

        world[0].Matrix.ShouldBe(contents[0]);
        world[1].Matrix.ShouldBe(contents[1]);
        other[0].Matrix[12].ShouldBe(70f, 1e-4f);
    }

    [Test]
    public void Instances_TheSameListNextFrame_RewritesItsMatricesInPlace()
    {
        // No per-frame allocation: the next build of a list reuses its arrays, written over, never compared.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        models.Add([Prop(10f, 1)], OneTriangle);
        models.Instances([Prop(10f, 1)], instances);
        float[] first = instances[0].Matrix;

        models.Instances([Prop(20f, 1)], instances);

        instances[0].Matrix.ShouldBeSameAs(first);
        instances[0].Matrix.ShouldBe(new PropTransform(20f, 0f, 0f, 0f, 0f, 0f, 1f).ToMatrix());
    }

    [Test]
    public void Instances_ASteadyStatePass_AllocatesNoMatrices()
    {
        // The warm pass's WHOLE allocation per prop, against the 88 bytes the old `float[16]` cost each one: f12's
        // ~246 collated props. Measured 2026-10-25 at 1 byte a prop (the pass's few fixed allocations over 246), so
        // the matrices' share is zero; restoring `ToMatrix()` makes it 89. Not asserted == 0 for the whole pass,
        // because what else it allocates is not B585's.
        EntityModelSet models = new();
        List<ModelInstance> instances = [];
        SceneProp[] props = new SceneProp[246];

        for (int i = 0; i < props.Length; i++)
        {
            props[i] = Prop(i * 10f, i + 1);
        }

        models.Add(props, OneTriangle);
        models.Instances(props, instances);
        models.Instances(props, instances);

        long before = GC.GetAllocatedBytesForCurrentThread();
        models.Instances(props, instances);
        long perProp = (GC.GetAllocatedBytesForCurrentThread() - before) / props.Length;

        perProp.ShouldBeLessThan(88L, "a float[16] per prop is 88 bytes; the pool must remove it");
    }

    private static void Fill(InstanceMatrices pool, PropTransform transform, int count)
    {
        pool.Reset();

        for (int i = 0; i < count; i++)
        {
            _ = pool.Next(transform);
        }
    }

    // A seeded xorshift32, so a failure names a reproducible case (CA5394 forbids `Random` here).
    private static float Unit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return (state >> 8) / (float)(1 << 24);
    }

    private static float Coordinate(ref uint state) => (Unit(ref state) - 0.5f) * 16384f;

    private static float Angle(ref uint state) => (Unit(ref state) - 0.5f) * 720f;

    private static SceneProp Prop(float x, int entity) =>
        new(
            entity,
            "models/props/crate.mdl",
            ScenePropTrack.Classify("models/props/crate.mdl"),
            new ScenePose { X = x },
            null);

    private static PropModels.ModelFrames OneTriangle(string path) => ModelFramesFixture.OneTriangle(path);
}
