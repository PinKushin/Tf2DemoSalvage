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
/// 0 and steps nothing. **A seek is ours, not the engine's**: it rebuilds the effect from its start on a fixed clock,
/// so the picture after a seek is the same however the viewer got there.
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

    [Test]
    public void Bursts_ReachedBySeekFromDifferentPlaces_AreTheSame()
    {
        ParticleBurst burst = Emitting(key: 1, tick: 100) with { History = static tick => (Along(tick), null) };

        ParticleEffects direct = new();
        direct.Bursts([burst with { At = Along(110.5f) }], Interval, null, 110, 110.5d);

        ParticleEffects played = new();

        // Played, a seek forward, a seek back, and a step back inside one tick.
        foreach (double shown in (double[])[100d, 100.5d, 101.25d, 103d, 140d, 110.75d])
        {
            played.Bursts([burst with { At = Along((float)shown) }], Interval, null, (int)shown, shown);
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
