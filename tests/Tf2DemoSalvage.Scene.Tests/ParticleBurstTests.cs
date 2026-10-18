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
    /// <remarks>
    /// **With particles in it** (2026-10-17): this used an effect that emits nothing, which was kept only because an
    /// unoffered effect was frozen rather than run; now that it runs, an empty one is rightly gone.
    /// </remarks>
    [Test]
    public void Bursts_NoLongerOffered_KeepRunningUntilEmpty()
    {
        ParticleEffects effects = new();
        ParticleBurst blast = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 105; tick++)
        {
            effects.Bursts([blast], Interval, null, tick);
        }

        effects.Bursts([], Interval, null, tick: 106);
        int atEnd = effects.BurstParticles(1)!.Count;
        // Born from tick 100 to 105, each living 33 ticks: by 136 the first are gone and none has replaced them.
        effects.Bursts([], Interval, null, tick: 136);
        int later = effects.BurstParticles(1)!.Count;

        effects.BurstCount.ShouldBe(1, "a blast does not stop because the window moved past it");

        // It RUNS, it is not held: a frozen effect keeps every particle, a stopped one only loses them.
        atEnd.ShouldBeGreaterThan(0, "the control: it had particles when the offer ended");
        later.ShouldBeLessThan(atEnd, "no particle is born after the offer ends, and the live ones age out");
    }

    /// <remarks>
    /// **Offered again after a gap, it continues — one effect, not a second built from its start** (review): a heal beam
    /// across a moment its patient was not posed. Counted where a burst is built.
    /// </remarks>
    [Test]
    public void Bursts_OfferedAgainAfterAGap_ContinueRatherThanRebuild()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 105; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Bursts([], Interval, null, tick: 106);
        effects.Bursts([beam], Interval, null, tick: 107);

        effects.BurstBuilds.ShouldBe(1);
        effects.BurstCount.ShouldBe(1);
    }

    /// <remarks>
    /// **A paused frame asks for the same tick again, and a fading effect does not age** — while the frames before the pause,
    /// the control, do age it. A fade that ran a fixed step per call, not per tick, would fail the second assertion.
    /// </remarks>
    [Test]
    public void Bursts_NoLongerOfferedWhilePaused_DoNotAge()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 120; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Bursts([], Interval, null, tick: 121);
        float before = effects.BurstParticles(1)!.Age;
        effects.Bursts([], Interval, null, tick: 125);
        float paused = effects.BurstParticles(1)!.Age;
        effects.Bursts([], Interval, null, tick: 125);
        effects.Bursts([], Interval, null, tick: 125);

        paused.ShouldBeGreaterThan(before, "the control: four played ticks age it");
        effects.BurstParticles(1)!.Age.ShouldBe(paused);
    }

    /// <remarks>
    /// **A fade is simulated a tick at a time** (`CParticleMgr::UpdateNewEffects`, once a frame): ten ticks unoffered in one
    /// call are ten steps of one tick, not one of ten. The store's last step says which.
    /// </remarks>
    [Test]
    public void Bursts_NoLongerOfferedAcrossTenTicks_FadeOneTickAtATime()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 110; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Bursts([], Interval, null, tick: 120);

        effects.BurstParticles(1)!.LastStep.ShouldBe(Interval, 1e-6f);
    }

    /// <remarks>
    /// **At this layer a seek keeps what the target offers and only that** — offered there, stopped and fading, it is
    /// built fading; no longer offered, it is gone. The feed offers a fading effect for as long as it shows (BurstEffects),
    /// so a seek into a fade builds it as playing through does.
    /// </remarks>
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void Bursts_ASeekForward_KeepsWhatTheTargetOffers(bool offered, int left)
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100, StopTick: 115);

        for (int tick = 100; tick <= 110; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Bursts(offered ? [beam] : [], Interval, null, tick: 120, seeked: true);

        effects.BurstCount.ShouldBe(left);
    }

    /// <remarks>
    /// **A dormant owner's effect is held, hidden, and resumed — not replayed** (`SetDormant`, particle_property.cpp:465-475):
    /// across ten dormant ticks nothing is built again and nothing is stepped; back, it runs on from where the clock is.
    /// </remarks>
    [Test]
    public void Bursts_WhileTheOwnerIsDormant_AreHeldAndResumeWithoutARebuild()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            1, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 110; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        int steps = effects.BurstSteps(1);

        for (int tick = 111; tick <= 120; tick++)
        {
            effects.Bursts([beam with { Dormant = true }], Interval, null, tick);
        }

        effects.BurstSteps(1).ShouldBe(steps, "held: not stepped while dormant");

        effects.Bursts([beam], Interval, null, tick: 121);

        effects.BurstBuilds.ShouldBe(1, "resumed, not built again");
        effects.BurstSteps(1).ShouldBe(steps + 1, "one tick on from where the clock stood");
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

    /// <remarks>
    /// **A continuous effect can be stopped** — `ParticleProp()->StopEmission`, what a medigun's beam gets when its
    /// target changes. Its particles live out their lives; no more are born.
    /// </remarks>
    [Test]
    public void Bursts_PastTheirStopTick_EmitNoMore()
    {
        ParticleEffects stopped = new();
        ParticleEffects running = new();

        stopped.Bursts([Emitting(key: 1, tick: 100, stop: 110)], Interval, null, tick: 110);
        int atStop = stopped.BurstParticles(1)!.Count;

        stopped.Bursts([Emitting(key: 1, tick: 100, stop: 110)], Interval, null, tick: 120);
        running.Bursts([Emitting(key: 1, tick: 100, stop: null)], Interval, null, tick: 120);

        atStop.ShouldBeGreaterThan(0);
        stopped.BurstParticles(1)!.Count.ShouldBe(atStop, "nothing was born after the stop, and none has died yet");
        running.BurstParticles(1)!.Count.ShouldBeGreaterThan(atStop);
    }

    [Test]
    public void Bursts_AStoppedBurstNoLongerOfferedOnceEmpty_IsRetired()
    {
        // Its emitter runs for ever, so it is never FINISHED; stopped and empty is what lets it go.
        ParticleEffects effects = new();

        effects.Bursts([Emitting(key: 1, tick: 100, stop: 110)], Interval, null, tick: 110);
        effects.Bursts([Emitting(key: 1, tick: 100, stop: 110)], Interval, null, tick: 300);
        effects.Bursts([], Interval, null, tick: 301);

        effects.BurstCount.ShouldBe(0);
    }

    /// <remarks>
    /// **A followed control point is read every frame** — `CParticleProperty::UpdateControlPoint`
    /// (particle_property.cpp:560-640) runs for every `PATTACH_*_FOLLOW` point before each simulation, so a particle is
    /// born where the entity WAS on its own tick. The engine reaches a seek's tick by playing to it, with the entities
    /// where they were on the way. A catch-up that steps every past tick at today's point lays a medigun beam between
    /// two players as if neither had moved for two seconds: a thin straight line where TF2 has a band (B527).
    /// </remarks>
    [Test]
    public void Bursts_AFollowingBurstMetBySeek_StepsEachPastTickAtThatTicksPoint()
    {
        ParticleEffects frozen = new();
        ParticleEffects followed = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100, stop: null) with { At = Along(110) };

        frozen.Bursts([burst], Interval, null, tick: 110);
        followed.Bursts([burst with { History = static tick => (Along(tick), null) }], Interval, null, tick: 110);

        (float Least, float Most) = Span(followed.BurstParticles(1)!);

        Span(frozen.BurstParticles(1)!).ShouldBe((110f, 110f), "the control: with no history every tick is stepped at today's point");
        Least.ShouldBeLessThanOrEqualTo(101f, "the first tick's particles were born where the point was on tick 101");
        Most.ShouldBe(110f, "and the last where it is now");
    }

    /// <remarks>The second point follows too: the patient's end of a beam is where the patient was on each tick.</remarks>
    [Test]
    public void Bursts_AFollowingBurstMetBySeek_SetsEachPastTicksSecondPoint()
    {
        ParticleEffects effects = new();
        ParticleBurst emitting = Emitting(key: 1, tick: 100, stop: null);
        ParticleBurst burst = emitting with
        {
            Definition = emitting.Definition with
            {
                Initializers =
                [
                    new ParticleFunction(
                        "Position Within Sphere Random",
                        "at the second point",
                        new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                        {
                            ["control_point_number"] = new(DmxAttributeType.Whole, 1d),
                        }),
                ],
            },
            End = Along(110),
            History = static tick => (Along(0), Along(tick)),
        };

        effects.Bursts([burst], Interval, null, tick: 110);

        (float Least, float Most) = Span(effects.BurstParticles(1)!);

        Least.ShouldBeLessThanOrEqualTo(101f);
        Most.ShouldBe(110f);
    }

    /// <remarks>
    /// **`StopEmission` stops the emitters and nothing else.** `CParticleProperty::StopEmission` (particle_property.cpp:
    /// 251-263) leaves the effect in `m_ParticleEffects`, so `UpdateControlPoint` goes on reading its followed points
    /// every frame while the last particles live out their second. A medigun beam let go of keeps bending between the
    /// medic and the patient as they part; frozen at the stop it hangs where they were (B527).
    /// </remarks>
    [Test]
    public void Bursts_AStoppedBurstWhosePointMoves_GoesOnFollowingIt()
    {
        ParticleEffects effects = new();
        ParticleBurst emitting = Emitting(key: 1, tick: 100, stop: 110);
        ParticleBurst burst = emitting with
        {
            Definition = emitting.Definition with
            {
                Operators =
                [
                    new ParticleFunction(
                        "Movement Lock to Control Point",
                        "rides the point",
                        new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
                ],
            },
        };

        // Not from the origin, which the lock's context reads as "no point seen yet".
        effects.Bursts([burst with { At = Along(10) }], Interval, null, tick: 110);
        Span(effects.BurstParticles(1)!).ShouldBe((10f, 10f), "the control: born on a point that has not moved");

        effects.Bursts([burst with { At = Along(60) }], Interval, null, tick: 111);

        Span(effects.BurstParticles(1)!).ShouldBe((60f, 60f), "locked to a point that moved fifty units after the stop");
    }

    /// <summary>A point whose X is the tick it is asked for.</summary>
    private static ParticleControlPoint Along(int tick) =>
        new(new Vector3(tick, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>The least and greatest X of a store's particles.</summary>
    private static (float Least, float Most) Span(ParticleStore particles)
    {
        float least = float.MaxValue;
        float most = float.MinValue;

        for (int index = 0; index < particles.Count; index++)
        {
            least = MathF.Min(least, particles.PositionOf(index).X);
            most = MathF.Max(most, particles.PositionOf(index).X);
        }

        return (least, most);
    }

    /// <summary>A burst of a system emitting two particles a tick for ever, each living a second.</summary>
    private static ParticleBurst Emitting(long key, int tick, int? stop) =>
        new(
            key,
            LongLived with
            {
                Name = "test_emitting",
                Emitters =
                [
                    new ParticleFunction(
                        "emit_continuously",
                        "emitter",
                        new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                        {
                            ["emission_rate"] = new(DmxAttributeType.Real, 132d),
                        }),
                ],
            },
            new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            tick,
            StopTick: stop);

    /// <remarks>
    /// **A seekable effect is the engine's SEEDED collection** (B469, D136): its seed is fixed by its identity — a burst's
    /// key — so a replay after a seek draws the same numbers, and two bursts of one system do not draw the same ones.
    /// </remarks>
    [Test]
    public void Bursts_ANewBurst_IsSeededFromItsKey()
    {
        ParticleEffects effects = new();

        effects.Bursts([Burst(key: 1, tick: 100), Burst(key: 2, tick: 100)], Interval, null, tick: 100);

        (effects.BurstEffect(1)!.Particles.Seed, effects.BurstEffect(2)!.Particles.Seed)
            .ShouldBe((ParticleEffects.SeedFor(1), ParticleEffects.SeedFor(2)));
    }

    /// <remarks>A trail's identity is its entity and the tick its track began, since entity indices are reused.</remarks>
    [Test]
    public void Update_ANewTrail_IsSeededFromItsEntityAndSpawnTick()
    {
        ParticleEffects effects = new();
        ParticleControlPoint here = new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

        effects.Update([(7, here, here, 3)], LongLived, Interval, tick: 10);

        effects.TrailEffect(7)!.Particles.Seed.ShouldBe(ParticleEffects.SeedFor((7L << 32) | 7L));
    }

    /// <remarks>
    /// The identity's Fibonacci hash — the upper 32 bits of it times <c>0x9E3779B97F4A7C15</c>, so neighbouring keys land
    /// far apart in the 4,096-entry table — and never 0, which is the engine's "unseeded".
    /// </remarks>
    [TestCase(0L, 1)]
    [TestCase(1L, unchecked((int)0x9E3779B9))]
    [TestCase(2L, 0x3C6EF372)]
    public void SeedFor_AnIdentity_IsItsFibonacciHashAndNeverZero(long identity, int expected) =>
        ParticleEffects.SeedFor(identity).ShouldBe(expected);

    /// <remarks>
    /// **An effect its caller stops offering, unstopped, is the owner's emission stopping** — the medic's beam when its
    /// patient dies is `UpdateEffects`' `StopEmission` (`tf_weapon_medigun.cpp:2374-2375`): no more particles are born,
    /// the live ones finish their lives, and the effect goes. Ours was held frozen — never stepped, never retired, drawn
    /// where its last particles stood: the owner's "old beams in the air". A beam emitting 66 a second, each living half a
    /// second, offered for ten ticks: a second and a half after the offer ends it is gone. The control is the tick after
    /// the offer ends, when it is still there and counted as no longer offered.
    /// </remarks>
    [Test]
    public void Bursts_AnEndlessEffectNoLongerOffered_StopsEmittingAndIsGoneWhenItsParticlesDie()
    {
        ParticleEffects effects = new();
        ParticleBurst beam = new(
            9, Beam, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 100);

        for (int tick = 100; tick <= 110; tick++)
        {
            effects.Bursts([beam], Interval, null, tick);
        }

        effects.Bursts([], Interval, null, tick: 111);

        effects.BurstCount.ShouldBe(1, "the control: its particles are still alive");
        effects.Census().ShouldContain(("test_beam", 1, 1));

        for (int tick = 112; tick <= 210; tick++)
        {
            effects.Bursts([], Interval, null, tick);
        }

        effects.BurstCount.ShouldBe(0);
    }

    /// <summary>A beam's shape: emits for ever, each particle lives half a second.</summary>
    private static readonly ParticleSystem Beam = new(
        Name: "test_beam",
        Emitters:
        [
            new ParticleFunction(
                Function: "emit_continuously",
                Name: "emitter",
                Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["emission_rate"] = new(DmxAttributeType.Real, 66d),
                }),
        ],
        Initializers:
        [
            new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["lifetime_min"] = new(DmxAttributeType.Real, 0.5d),
                ["lifetime_max"] = new(DmxAttributeType.Real, 0.5d),
            }),
        ],
        Operators: [new ParticleFunction("Lifespan Decay", "decay", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
        Renderers: [],
        Children: [],
        Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));

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
