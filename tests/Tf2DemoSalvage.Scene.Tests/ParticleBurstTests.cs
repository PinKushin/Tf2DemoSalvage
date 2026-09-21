using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A one-shot effect at a point, which is what an explosion is (B415).</summary>
/// <remarks>
/// **A burst has a different lifetime from a trail and that is the whole reason it needs its own path.** Every
/// effect `ParticleEffects` held until now is owned by a live entity: it exists while the rocket does and stops
/// emitting when the rocket is gone. A blast has no entity at all — it happens at a tick and then runs on its own
/// for as long as its system says.
///
/// **And a viewer can scrub.** The engine never replays anything; a demo player must, so a burst's particles have
/// to be a function of how many ticks have passed since it fired rather than of how many times `Update` happened
/// to be called. That is the same rule `PlayerGibs` follows for a different reason
/// (`docs/memory/determinism-does-not-require-flattening-a-draw.md`), and the same rebuild-and-replay
/// `CorpsePhysics` does on a backward seek.
/// </remarks>
public sealed class ParticleBurstTests
{
    /// <summary>Ticks per second, as a demo runs.</summary>
    private const float Interval = 1f / 66f;

    [Test]
    public void Bursts_AtTheTickItFired_HasNotBeenSteppedYet()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 100);

        effects.BurstCount.ShouldBe(1);
        effects.BurstSteps(1).ShouldBe(0, "a blast on the tick it fired has had no time pass");
    }

    /// <remarks>
    /// **The count of steps is the tick difference, not the number of calls.** A viewer that renders at 300 frames
    /// a second calls this five times per tick, and a burst stepped per call would run five times too fast — which
    /// is exactly the fault B375 found in the rocket trail.
    /// </remarks>
    [Test]
    public void Bursts_AskedRepeatedlyAtOneTick_StepOnlyOnce()
    {
        ParticleEffects effects = new();

        for (int call = 0; call < 5; call++)
        {
            effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 110);
        }

        effects.BurstSteps(1).ShouldBe(10, "ten ticks passed, however many frames were drawn");
    }

    /// <remarks>
    /// **A burst met mid-life catches up rather than starting fresh.** A viewer seeking into the middle of an
    /// explosion must see it half-finished, not beginning — the same replay the rocket trail does from its
    /// projectile's start position.
    /// </remarks>
    [Test]
    public void Bursts_MetPartWayThrough_CatchUpToWhereTheyShouldBe()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 140);

        effects.BurstSteps(1).ShouldBe(40);
    }

    /// <remarks>
    /// **A backward seek rebuilds.** Stepping is one-way, so a burst that has run forty ticks cannot be asked for
    /// its state at ten by stepping again; it is thrown away and replayed. Without this a scrub backwards shows
    /// every explosion further through its life than it should be, which looks like the effects are too short.
    /// </remarks>
    [Test]
    public void Bursts_AfterSeekingBackwards_AreReplayedFromTheirOwnTick()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 140);
        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 110);

        effects.BurstSteps(1).ShouldBe(10, "ten ticks after it fired, however it was reached");
    }

    /// <remarks>
    /// **Two blasts are two bursts, keyed apart.** They share a definition and a tick and differ only in where
    /// they happened, so a key that collapsed them would draw one explosion for two rockets.
    /// </remarks>
    [Test]
    public void Bursts_TwoBlastsOnOneTick_AreTwoEffects()
    {
        ParticleEffects effects = new();

        effects.Bursts(
            [Burst(key: 1, tick: 100), Burst(key: 2, tick: 100, x: 512f)], Interval, null, tick: 100);

        effects.BurstCount.ShouldBe(2);
    }

    /// <remarks>
    /// **A burst that is no longer offered keeps running until it is empty**, rather than vanishing the moment the
    /// caller's window moves past it. The same rule a rocket's trail follows when the rocket explodes: cutting the
    /// effect at the source is the opposite of what an explosion looks like.
    /// </remarks>
    [Test]
    public void Bursts_NoLongerOffered_KeepRunningUntilEmpty()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 100);
        effects.Bursts([], Interval, null, tick: 101);

        effects.BurstCount.ShouldBe(1, "a blast does not stop because the window moved past it");
    }

    /// <remarks>
    /// **Scrubbing back before an explosion must remove it, and this is the only test that says so.** The
    /// per-burst rebuild handles a blast the caller still offers; a blast that fired AFTER the tick now being
    /// shown is not offered at all, so nothing would touch it — and it would go on running and go on drawing.
    /// An explosion visible before it happens is the symptom.
    ///
    /// **Found by sabotage, not by writing it.** Deleting the rewind clear reddened nothing, because every other
    /// test here keeps offering the burst.
    /// </remarks>
    [Test]
    public void Bursts_AfterSeekingBackPastABlast_ForgetIt()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 140);
        effects.Bursts([], Interval, null, tick: 50);

        effects.BurstCount.ShouldBe(0, "a blast that has not fired yet must not be on screen");
    }

    /// <remarks>
    /// **A burst that has finished while its caller still offers it is kept, finished, rather than retired** — retired,
    /// the next call finds it missing, builds it again and replays it from its own tick, every frame until the window
    /// passes it. That cost was filed in B415 as a suspicion from a backgrounded run; it is read here from the code.
    /// Once it is no longer offered it goes.
    /// </remarks>
    [Test]
    public void Bursts_FinishedButStillOffered_AreKeptUntilTheWindowPassesThem()
    {
        ParticleEffects effects = new();

        effects.Bursts([Finished(key: 1, tick: 100)], Interval, null, tick: 110);
        effects.BurstCount.ShouldBe(1, "a finished burst the caller still offers is kept, not rebuilt next call");

        effects.Bursts([], Interval, null, tick: 111);
        effects.BurstCount.ShouldBe(0);
    }

    /// <remarks>A demo change forgets every burst, as it forgets every trail.</remarks>
    [Test]
    public void Clear_AfterBursts_ForgetsThem()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100)], Interval, null, tick: 100);
        effects.Clear();

        effects.BurstCount.ShouldBe(0);
    }

    /// <summary>One burst of a long-lived system, at a point.</summary>
    /// <remarks>
    /// **The system emits for an hour on purpose.** What is under test is the LIFECYCLE — how many times a burst
    /// is stepped and when it is rebuilt — and a system that finished part way through would make the step counts
    /// depend on the operators as well. `ParticleProbe`'s `simulate` is where a real `.pcf` runs end to end.
    ///
    /// **It has to declare an emitter at all**, which is the thing this fixture got wrong first: a system with no
    /// emitters can never produce a particle, so it is genuinely finished on its first step and the burst is
    /// retired before anything can be asked about it. That was the fixture being degenerate, not the code being
    /// wrong — but chasing it is what surfaced `ParticleEffect.Finished`, which the code really did need.
    /// </remarks>
    /// <remarks>
    /// **Control point 1 is set before the first step**, as `ParticleEffectCallback` sets it before the effect
    /// simulates: a tracer spawned without it would head for the world origin.
    /// </remarks>
    [Test]
    public void Bursts_WithAnEnd_SendTheirParticlesToIt()
    {
        ParticleEffects effects = new();

        ParticleSystem tracer = new(
            Name: "test_tracer",
            Emitters: [new ParticleFunction("emit_instantaneously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["num_to_emit"] = new(DmxAttributeType.Whole, 1d),
            })],
            Initializers: [new ParticleFunction("move particles between 2 control points", "move", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["minimum speed"] = new(DmxAttributeType.Real, 1000d),
                ["maximum speed"] = new(DmxAttributeType.Real, 1000d),
            })],
            Operators: [],
            Renderers: [],
            Children: [],
            Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));

        effects.Bursts(
            [new ParticleBurst(
                7,
                tracer,
                ParticleControlPoint.Unoriented(new Vector3(100f, 0f, 0f)),
                100,
                ParticleControlPoint.Unoriented(new Vector3(100f, 300f, 0f)))],
            Interval,
            null,
            tick: 101);

        ParticleStore particles = effects.BurstParticles(7).ShouldNotBeNull();

        particles.Count.ShouldBe(1);
        particles.LifetimeOf(0).ShouldBe(0.3f, 0.0001f, "300 units at 1000 a second");
    }

    private static ParticleBurst Burst(long key, int tick, float x = 0f) =>
        new(
            key,
            LongLived,
            new ParticleControlPoint(
                new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            tick);

    /// <summary>A burst of a system with no emitters, which is finished on its first step.</summary>
    private static ParticleBurst Finished(long key, int tick) =>
        new(
            key,
            LongLived with { Name = "test_finished", Emitters = [] },
            new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            tick);

    /// <summary>A system that emits at no rate for an hour, so it is never finished and never grows.</summary>
    private static readonly ParticleSystem LongLived = new(
        Name: "test_burst",
        Emitters:
        [
            new ParticleFunction(
                Function: "emit_continuously",
                Name: "emitter",
                Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["emission_rate"] = new(DmxAttributeType.Real, 0d),
                    ["emission_duration"] = new(DmxAttributeType.Real, 3600d),
                }),
        ],
        Initializers: [],
        Operators: [],
        Renderers: [],
        Children: [],
        Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));
}
