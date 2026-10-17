using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Particles are simulated once a FRAME, by the frame's time, on the frame's control points (D221, B539).</summary>
/// <remarks>
/// <code>
/// // cdll_client_int.cpp:2184 — view->OnRenderStart(): the player and the view models placed for this frame
/// // cdll_client_int.cpp:2234 — ParticleMgr()->Simulate( gpGlobals->frametime );
/// // particle_property.cpp:427-446 — OnParticleSystemUpdated → UpdateParticleEffect → UpdateControlPoint (:527-640)
/// </code>
/// Every frame re-reads each followed point and steps the effect by that frame's time; a paused client's frame time is
/// 0 and steps nothing, and still reads its points (particlemgr.cpp:1841-1853). **A seek's rebuild is our addition,
/// not the engine's**: the effect is replayed from its start on a fixed clock, so it is the same after any seek to a
/// moment — and may differ, within a frame's emission, from playing to it.
/// </remarks>
public sealed class ParticleFrameClockConformanceTests
{
    private const float Interval = 1f / 66f;

    [Test]
    public void Bursts_PlayedFrameByFrame_StepOncePerFrameByTheFramesTime()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        foreach (double shown in (double[])[100d, 100.25d, 100.5d, 101d])
        {
            effects.Bursts([burst], Interval, null, (int)shown, shown);
        }

        effects.BurstSteps(1).ShouldBe(3, "three frames moved the clock, each stepped once");
        effects.BurstParticles(1)!.Age.ShouldBe(Interval, 1e-6f, "a quarter, a quarter and a half of a tick");
    }

    [Test]
    public void Bursts_PlayedFrameByFrame_ReadEachFramesControlPoint()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Locked(Emitting(key: 1, tick: 100));

        effects.Bursts([burst with { At = Along(10f) }], Interval, null, 100, 100d);
        effects.Bursts([burst with { At = Along(15f) }], Interval, null, 100, 100.5d);

        Span(effects.BurstParticles(1)!).ShouldBe((15f, 15f), "born on the point this frame read, between two ticks");

        effects.Bursts([burst with { At = Along(20f) }], Interval, null, 100, 100.75d);

        Span(effects.BurstParticles(1)!).ShouldBe((20f, 20f), "and carried by it on the next frame, still inside the tick");
    }

    [Test]
    public void Bursts_ShownTimeUnchanged_StepNothing()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        effects.Bursts([burst], Interval, null, 100, 100.5d);
        int steps = effects.BurstSteps(1);

        effects.Bursts([burst], Interval, null, 100, 100.5d);

        effects.BurstSteps(1).ShouldBe(steps, "a paused frame's time is 0");
    }

    /// <remarks>`UpdateNewEffects` updates every effect's control points before choosing what to simulate (particlemgr.cpp:1841-1853).</remarks>
    [Test]
    public void Bursts_ShownTimeUnchanged_StillReadTheFramesControlPoint()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        effects.Bursts([burst with { At = Along(10f) }], Interval, null, 100, 100.5d);
        effects.Bursts([burst with { At = Along(30f) }], Interval, null, 100, 100.5d);

        effects.BurstEffect(1)!.ControlPoint(0).At.X.ShouldBe(30f);
    }

    /// <remarks>A paused frame's pose is the last one's: the hitboxes are read on the first still frame and not again.</remarks>
    [Test]
    public void Bursts_SeveralStillFrames_ReadTheModelOnce()
    {
        ParticleEffects effects = new();
        int asked = 0;
        ParticleBurst burst = Emitting(key: 1, tick: 100) with
        {
            Model = () =>
            {
                asked++;
                return ([], Vector3.Zero);
            },
        };

        effects.Bursts([burst], Interval, null, 100, 100.5d);
        asked = 0;

        for (int frame = 0; frame < 5; frame++)
        {
            effects.Bursts([burst], Interval, null, 100, 100.5d);
        }

        asked.ShouldBe(1);
    }

    /// <remarks>A played frame is one `Simulate( frametime )` however long it is — 8x at 30 fps, or a stall.</remarks>
    [Test]
    public void Bursts_ALongPlayedFrame_IsOneStep()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        effects.Bursts([burst], Interval, null, 100, 100d);
        effects.Bursts([burst], Interval, null, 140, 140d);

        effects.BurstSteps(1).ShouldBe(1, "forty ticks played in one frame are one simulation, not a replay");
    }

    /// <remarks>`StopEmission` lands before the frame's `Simulate`, so a frame across the stop emits only up to it.</remarks>
    [Test]
    public void Bursts_AFrameAcrossTheStop_EmitsOnlyUpToIt()
    {
        ParticleEffects split = new();
        ParticleEffects ticked = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100) with { StopTick = 110 };

        split.Bursts([burst], Interval, null, 100, 100d);
        split.Bursts([burst], Interval, null, 112, 112d);
        ticked.Bursts([burst], Interval, null, 112, 112d, seeked: true);

        split.BurstParticles(1)!.Count.ShouldBe(ticked.BurstParticles(1)!.Count, "the control: a replay a tick at a time stops at 110");
    }

    /// <remarks>
    /// **The control on the rebuild**: playing to a moment and seeking to it are allowed to differ, by the frames' sizes,
    /// and no more than one frame's emission.
    /// </remarks>
    [Test]
    public void Bursts_PlayedAgainstSoughtToOneMoment_DifferByLessThanAFramesEmission()
    {
        ParticleEffects played = new();
        ParticleEffects sought = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        for (double shown = 100d; shown <= 110.5d; shown += 0.35d)
        {
            played.Bursts([burst], Interval, null, (int)shown, shown);
        }

        played.Bursts([burst], Interval, null, 110, 110.5d);
        sought.Bursts([burst], Interval, null, 110, 110.5d, seeked: true);

        played.BurstParticles(1)!.Age.ShouldBe(sought.BurstParticles(1)!.Age, 1e-5f);
        Math.Abs(played.BurstParticles(1)!.Count - sought.BurstParticles(1)!.Count).ShouldBeLessThanOrEqualTo(1);
    }

    [Test]
    public void Update_AStillFrame_ReadsTheTrailsPoint()
    {
        ParticleEffects effects = new();
        ParticleSystem trail = Emitting(key: 1, tick: 0).Definition;

        effects.Update([(7, Along(5f), null, 0)], trail, Interval, null, 0, 0d);
        effects.Update([(7, Along(5f), null, 0)], trail, Interval, null, 0, 0.5d);
        effects.Update([(7, Along(25f), null, 0)], trail, Interval, null, 0, 0.5d);

        effects.TrailEffect(7)!.ControlPoint(0).At.X.ShouldBe(25f);
    }

    /// <remarks>**The rebuild is ours** (D221): the same after any seek to a moment, not the same as playing to it.</remarks>
    [Test]
    public void Bursts_ReachedBySeekFromDifferentPlaces_AreTheSame()
    {
        ParticleBurst burst = Emitting(key: 1, tick: 100) with { History = static tick => (Along(tick), null) };

        ParticleEffects direct = new();
        direct.Bursts([burst with { At = Along(110.5f) }], Interval, null, 110, 110.5d, seeked: true);

        ParticleEffects played = new();

        // Played, then seeks forward and back, then a step back inside one tick.
        foreach ((double shown, bool seeked) in ((double, bool)[])[(100d, false), (100.5d, false), (101.25d, false), (103d, false), (140d, true), (110.75d, true)])
        {
            played.Bursts([burst with { At = Along((float)shown) }], Interval, null, (int)shown, shown, seeked);
        }

        played.Bursts([burst with { At = Along(110.5f) }], Interval, null, 110, 110.5d);

        ParticleStore a = direct.BurstParticles(1)!;
        ParticleStore b = played.BurstParticles(1)!;

        b.Count.ShouldBe(a.Count);
        b.Age.ShouldBe(a.Age);

        for (int index = 0; index < a.Count; index++)
        {
            b.PositionOf(index).ShouldBe(a.PositionOf(index));
        }
    }

    [Test]
    public void Update_APlayedTrail_StepsOncePerFrameByTheFramesTime()
    {
        ParticleEffects effects = new();
        ParticleControlPoint here = Along(5f);
        ParticleSystem trail = Emitting(key: 1, tick: 0).Definition;

        // From the clock's own start, so the first call steps nothing.
        effects.Update([(7, here, null, 0)], trail, Interval, null, 0, 0d);
        effects.Update([(7, here, null, 0)], trail, Interval, null, 0, 0.5d);
        effects.Update([(7, here, null, 0)], trail, Interval, null, 1, 1d);

        effects.TrailEffect(7)!.Particles.Age.ShouldBe(Interval, 1e-6f);
    }

    private static ParticleControlPoint Along(float x) =>
        new(new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

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

    private static ParticleBurst Locked(ParticleBurst burst) =>
        burst with
        {
            Definition = burst.Definition with
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

    /// <summary>Two particles a tick, each living a second, born on control point 0.</summary>
    private static ParticleBurst Emitting(long key, int tick) =>
        new(
            key,
            new ParticleSystem(
                Name: "test_frame_clock",
                Emitters:
                [
                    new ParticleFunction(
                        "emit_continuously",
                        "emitter",
                        new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                        {
                            ["emission_rate"] = new(DmxAttributeType.Real, 132d),
                        }),
                ],
                Initializers: [],
                Operators: [],
                Renderers: [],
                Children: [],
                Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
            Along(1f),
            tick);
}
