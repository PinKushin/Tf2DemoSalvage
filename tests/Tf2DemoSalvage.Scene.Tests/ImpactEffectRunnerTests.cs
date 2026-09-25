using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><see cref="ImpactEffectRunner"/>: impact effects stepped by ticks, rebuilt on a seek back, kept while offered.</summary>
public sealed class ImpactEffectRunnerTests
{
    private const float Interval = 1f / 66f;

    private static readonly Func<Vector3, Vector3, BspTrace> Nothing = static (_, _) => new BspTrace(1f, -1, default, false);

    private static readonly ShotImpact Hit = new(0, 0, 100, 5, 2, default, default, default, 0);

    [Test]
    public void Advance_AskedRepeatedlyAtOneTick_StepsOnlyByTicks()
    {
        ImpactEffectRunner runner = new();

        for (int call = 0; call < 5; call++)
        {
            runner.Advance([(0, Hit)], 110, Interval, static (_, _, _) => Lasting(), Nothing);
        }

        runner.Steps(0).ShouldBe(10);
    }

    [Test]
    public void Advance_AfterSeekingBackwards_ReplaysFromTheImpactsTick()
    {
        int spawned = 0;
        ImpactEffectRunner runner = new();

        runner.Advance([(0, Hit)], 140, Interval, (_, _, _) => { spawned++; return Lasting(); }, Nothing);
        runner.Advance([(0, Hit)], 110, Interval, (_, _, _) => { spawned++; return Lasting(); }, Nothing);

        runner.Steps(0).ShouldBe(10);
        spawned.ShouldBe(2);
    }

    [Test]
    public void Advance_AnEffectThatThrowsNothing_IsNotAskedForAgainWhileOffered()
    {
        int spawned = 0;
        ImpactEffectRunner runner = new();

        for (int tick = 100; tick < 105; tick++)
        {
            runner.Advance([(0, Hit)], tick, Interval, (_, _, _) => { spawned++; return null; }, Nothing);
        }

        spawned.ShouldBe(1);
    }

    [Test]
    public void Advance_AFinishedEffect_IsKeptWhileOfferedAndDroppedAfter()
    {
        int spawned = 0;
        ImpactEffectRunner runner = new();

        runner.Advance([(0, Hit)], 200, Interval, (_, _, _) => { spawned++; return new ImpactEffect(); }, Nothing);
        runner.Advance([(0, Hit)], 201, Interval, (_, _, _) => { spawned++; return new ImpactEffect(); }, Nothing);

        spawned.ShouldBe(1, "a finished effect still in the window is not rebuilt");

        runner.Advance([], 202, Interval, static (_, _, _) => null, Nothing);
        runner.Count.ShouldBe(0);
    }

    /// <remarks>
    /// Two bursts of flecks ten units apart, two ticks apart: the second joins the first's emitter on its own tick, which
    /// needs every effect stepped together rather than each caught up alone. Seeking back before the second starts the
    /// first alone again.
    /// </remarks>
    [Test]
    public void Advance_TwoNearbyBurstsTwoTicksApart_ShareOneEmitter()
    {
        List<ImpactEffect> spawned = [];
        ImpactEffectRunner runner = new();
        (int, int, int)[] live = [(0, 100, 1), (1, 102, 2)];

        runner.Advance(live, 110, Interval, (index, _) => Record(spawned, Burst(new Vector3(10f * index, 0f, 0f))), Nothing);

        spawned[0].Emitters[0].Particles.Count.ShouldBe(2);
        spawned[1].Emitters.ShouldBeEmpty();

        spawned.Clear();
        runner.Advance(live, 101, Interval, (index, _) => Record(spawned, Burst(new Vector3(10f * index, 0f, 0f))), Nothing);

        spawned.Count.ShouldBe(1);
        spawned[0].Emitters[0].Particles.Count.ShouldBe(1);
    }

    private static ImpactEffect Record(List<ImpactEffect> into, ImpactEffect effect)
    {
        into.Add(effect);
        return effect;
    }

    /// <summary>One fleck that lives an hour, in an emitter boxed about its spawn.</summary>
    private static ImpactEffect Burst(Vector3 spawn)
    {
        ImpactEffect effect = new();
        ImpactEmitter emitter = new(ImpactEmitterKind.Fleck) { Mins = spawn - new Vector3(5f), Maxs = spawn + new Vector3(5f) };

        emitter.Particles.Add(new ImpactParticle("m", spawn, Vector3.Zero, 3600f));
        effect.Emitters.Add(emitter);

        return effect;
    }

    /// <summary>An effect with one particle that lives an hour.</summary>
    private static ImpactEffect Lasting()
    {
        ImpactEffect effect = new();
        ImpactEmitter emitter = new(ImpactEmitterKind.Simple);

        emitter.Particles.Add(new ImpactParticle("m", Vector3.Zero, Vector3.Zero, 3600f));
        effect.Emitters.Add(emitter);

        return effect;
    }
}
