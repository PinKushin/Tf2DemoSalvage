using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The Scene charge of the roots split (2026-10-31 setup-parts record, instrument audit caveat 6): the meter's parts reach
/// <see cref="EntityModelSet.Counters"/> through <c>MarkSetup</c>, <c>ChargeSetup</c> and <c>SetupRootParts</c>, and
/// <see cref="EntityModelSet.PoseCounters.Since"/> hands back one interval's parts, not the running total.
/// </summary>
/// <remarks>
/// **The amounts are put in by the test, inside the charge window.** Each player's <c>BeforeBuild</c> runs inside its own
/// <c>SetupBones</c>, which <c>Instances</c> brackets with <c>MarkSetup</c>/<c>ChargeSetup</c>; the wrapper records a known
/// <see cref="RootSetupParts"/> into the scene's own meter there. The meter is private and the task forbids a production
/// seam, so it is reached by reflection, and each reflected field is asserted present — a rename fails here loudly rather
/// than leaving an instrument that reads nothing.
///
/// **Two assertions, because the real build adds timed parts on top.** Exact carry: the interval's charged parts equal the
/// meter's own delta over the same call (B243, the value carried, not recomputed). Known amount: <c>Samples</c> is a count,
/// and these models have no layers, so the real build adds exactly 0 — proved by an interval with no injection — leaving
/// calls × injected as the exact expectation; every timed field must be at least its injected share.
///
/// **The pre-pass stays off.** With it on, <c>Instances</c> runs every root's <c>BeforeBuild</c> before the mark (caveat 1),
/// so a record made there is outside the charge by design and the exact carry would not hold — not a bug to fix here. The
/// wrapper's record also counts as a root build, so the build counters are not asserted.
/// </remarks>
public sealed class EntityModelSetSetupPartsTests
{
    private const int Players = 3;

    private static readonly string[] PlayerBones = [.. Enumerable.Range(0, 20).Select(bone => bone == 0 ? "root" : $"b{bone}")];

    [Test]
    public void CountersSince_TwoIntervalsOfKnownRootParts_EachCarriesOnlyItsOwnParts()
    {
        EntityModelSet models = new() { ThreadedBoneSetup = false };
        models.Add(Props(0), _ => EntityModelSetPrePoseTests.Skinned(PlayerBones));
        List<ModelInstance> drawn = [];
        BoneSetupMeter meter = Meter(models);

        // Frame 0 creates the entities; wrapping waits for them.
        models.Instances(Props(0), drawn, seconds: 0d);
        List<AnimatingEntity> entities = Entities(models);
        entities.Count.ShouldBe(Players, "the control: the scene made one entity per player before the wrap");

        // The control interval: no injection, so the real build's Samples must be 0 for the known amount to be exact.
        (RootSetupParts control, RootSetupParts controlMeter) = Interval(models, meter, drawn, frame: 1);
        control.ShouldBe(controlMeter, "the charge carries the meter's own delta");
        control.Samples.ShouldBe(0, "no layers, so the real build counts no layer sample");
        control.Blend.ShouldBeGreaterThan(0, "the control: the roots' calls were charged at all");

        RootSetupParts injected = default;
        int calls = 0;

        foreach (AnimatingEntity entity in entities)
        {
            Action? original = entity.BeforeBuild;
            entity.BeforeBuild = () =>
            {
                original?.Invoke();
                calls++;
                meter.Record(root: true, eligible: false, built: true, ticks: 0, injected);
            };
        }

        RootSetupParts first = new(Blend: 1_000_003, Base: 2_000_029, Accumulate: 3_000_017, Sampling: 1_500_007, Samples: 11,
            Adjust: 4_000_037, Loop: 5_000_011, Ik: 6_000_001, Finish: 7_000_003,
            Seed: 300_007, Capture: 400_009, Solve: 450_013, Compose: 500_029, Enumerators: 17);
        RootSetupParts second = new(Blend: 8_000_009, Base: 9_000_011, Accumulate: 10_000_019, Sampling: 9_500_021, Samples: 13,
            Adjust: 11_000_027, Loop: 12_000_007, Ik: 13_000_027, Finish: 14_000_029,
            Seed: 100_003, Capture: 200_003, Solve: 250_007, Compose: 150_001, Enumerators: 19);

        injected = first;
        calls = 0;
        (RootSetupParts one, RootSetupParts oneMeter) = Interval(models, meter, drawn, frame: 2);
        int firstCalls = calls;

        firstCalls.ShouldBeGreaterThan(0, "the control: the wrapper ran inside the interval");
        one.ShouldBe(oneMeter, "the first interval carries the meter's delta");
        one.Samples.ShouldBe(firstCalls * first.Samples, "exactly the samples put in");
        one.Enumerators.ShouldBe(firstCalls * first.Enumerators, "exactly the enumerators put in");
        AtLeast(one, firstCalls, first);

        injected = second;
        calls = 0;
        (RootSetupParts two, RootSetupParts twoMeter) = Interval(models, meter, drawn, frame: 3);
        int secondCalls = calls;

        secondCalls.ShouldBeGreaterThan(0, "the control: the wrapper ran inside the interval");
        two.ShouldBe(twoMeter, "the second interval carries only its own delta, not the running total");
        two.Samples.ShouldBe(secondCalls * second.Samples, "exactly this interval's samples, none of the first's");
        two.Enumerators.ShouldBe(secondCalls * second.Enumerators, "exactly this interval's enumerators, none of the first's");
        AtLeast(two, secondCalls, second);
    }

    /// <summary>One <c>Instances</c> call: the charged parts from <c>Counters.Since</c>, and the meter's delta over it.</summary>
    private static (RootSetupParts Charged, RootSetupParts Meter) Interval(
        EntityModelSet models, BoneSetupMeter meter, List<ModelInstance> drawn, int frame)
    {
        EntityModelSet.PoseCounters before = models.Counters;
        RootSetupParts meterBefore = meter.RootParts;

        models.Instances(Props(frame), drawn, seconds: frame / 66d);

        return (models.Counters.Since(before).RootParts, meter.RootParts.Since(meterBefore));
    }

    /// <summary>Each timed field holds at least its injected share; the real build only adds to it.</summary>
    private static void AtLeast(RootSetupParts charged, int calls, RootSetupParts injected)
    {
        charged.Blend.ShouldBeGreaterThanOrEqualTo(calls * injected.Blend);
        charged.Base.ShouldBeGreaterThanOrEqualTo(calls * injected.Base);
        charged.Accumulate.ShouldBeGreaterThanOrEqualTo(calls * injected.Accumulate);
        charged.Sampling.ShouldBeGreaterThanOrEqualTo(calls * injected.Sampling);
        charged.Adjust.ShouldBeGreaterThanOrEqualTo(calls * injected.Adjust);
        charged.Loop.ShouldBeGreaterThanOrEqualTo(calls * injected.Loop);
        charged.Ik.ShouldBeGreaterThanOrEqualTo(calls * injected.Ik);
        charged.Finish.ShouldBeGreaterThanOrEqualTo(calls * injected.Finish);
        charged.Seed.ShouldBeGreaterThanOrEqualTo(calls * injected.Seed);
        charged.Capture.ShouldBeGreaterThanOrEqualTo(calls * injected.Capture);
        charged.Solve.ShouldBeGreaterThanOrEqualTo(calls * injected.Solve);
        charged.Compose.ShouldBeGreaterThanOrEqualTo(calls * injected.Compose);
    }

    private static BoneSetupMeter Meter(EntityModelSet models)
    {
        FieldInfo? field = typeof(EntityModelSet).GetField("_boneFrames", BindingFlags.NonPublic | BindingFlags.Instance);
        field.ShouldNotBeNull("EntityModelSet._boneFrames renamed? This test reads the scene's meter through it.");
        return ((BoneFrameCounter)field.GetValue(models)!).Meter;
    }

    private static List<AnimatingEntity> Entities(EntityModelSet models)
    {
        FieldInfo? field = typeof(EntityModelSet).GetField("_entities", BindingFlags.NonPublic | BindingFlags.Instance);
        field.ShouldNotBeNull("EntityModelSet._entities renamed? This test wraps each entity's BeforeBuild through it.");
        return [.. ((Dictionary<int, AnimatingEntity>)field.GetValue(models)!).Values];
    }

    private static SceneProp[] Props(int frame) =>
        [.. Enumerable.Range(1, Players).Select(index =>
            EntityModelSetPrePoseTests.Prop("models/player/demo.mdl", index, x: (100f * index) + (7f * frame), yaw: (13f * frame) + index))];
}
