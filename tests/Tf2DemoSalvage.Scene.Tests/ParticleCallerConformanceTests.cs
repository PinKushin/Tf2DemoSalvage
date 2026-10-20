using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What `CParticleMgr` does with an effect each frame — its creation frame and its sleep (B557 rows 6 and 9).</summary>
/// <remarks>
/// <code>
/// // particlemgr.cpp:1572-1580 (ProcessPSystem)
/// if ( pNewEffect->GetFirstFrameFlag() ) { pNewEffect->Simulate( 0.0f, updateBboxOnly ); pNewEffect->SetFirstFrameFlag( false ); }
/// else if ( pNewEffect->ShouldSimulate() ) { pNewEffect->Simulate( s_flThreadedPSystemTimeStep, updateBboxOnly ); }
/// </code>
/// `m_bIsFirstFrame` is set true in the constructor (particles_new.cpp:53), so the frame an effect is made on simulates
/// nothing of that frame's time; `CParticleCollection::Simulate( 0 )` runs `SimulateFirstFrame` and stops at its
/// <c>dt &gt;= 1e-22</c> test (particles.obj). The next frame simulates its whole frame time.
/// </remarks>
[TestFixture]
public sealed class ParticleCallerConformanceTests
{
    private const float Interval = 1f / 66f;

    [Test]
    public void Bursts_FirstOfferedHalfWayThroughATick_SimulateNothingOnTheirCreationFrame()
    {
        ParticleEffects effects = new();
        ParticleBurst burst = Emitting(key: 1, tick: 100);

        // The frame before it fired, then the frame it is first offered on.
        effects.Bursts([], Interval, null, 99, 99.75d);
        effects.Bursts([burst], Interval, null, 100, 100.5d);
        float atCreation = effects.BurstParticles(1)!.Age;
        int bornAtCreation = effects.BurstParticles(1)!.Count;

        effects.Bursts([burst], Interval, null, 101, 101d);

        (atCreation, bornAtCreation).ShouldBe((0f, 0));
        effects.BurstParticles(1)!.Age.ShouldBe(Interval * 0.5f, 1e-6f, "the second frame's own half tick, and no more");
    }

    /// <remarks>The control: a seek's rebuild is our addition (D221) and still replays from the burst's own tick.</remarks>
    [Test]
    public void Bursts_FirstMetBySeek_ReplayFromTheirOwnTick()
    {
        ParticleEffects effects = new();

        effects.Bursts([Emitting(key: 1, tick: 100)], Interval, null, 100, 100.5d, seeked: true);

        effects.BurstParticles(1)!.Age.ShouldBe(Interval * 0.5f, 1e-6f);
    }

    [Test]
    public void Update_ATrailFirstSeenOnAPlayedFrame_SimulatesNothingOnItsCreationFrame()
    {
        ParticleEffects effects = new();
        ParticleSystem trail = Emitting(key: 1, tick: 0).Definition;

        // The frame before, with no rocket.
        effects.Update([], trail, Interval, null, 0, 0.5d);

        // A rocket that started on tick 1 appears in the frame at 1.25; then the next frame.
        effects.Update([(7, Along(5f), Along(0f), 0)], trail, Interval, null, 1, 1.25d);
        float atCreation = effects.TrailEffect(7)!.Particles.Age;
        int bornAtCreation = effects.TrailEffect(7)!.Particles.Count;

        effects.Update([(7, Along(10f), Along(0f), 1)], trail, Interval, null, 2, 2d);

        (atCreation, bornAtCreation).ShouldBe((0f, 0));
        effects.TrailEffect(7)!.Particles.Age.ShouldBe(Interval * 0.75f, 1e-6f, "the second frame's own time; no replay on it");
    }

    /// <remarks>The opening is not a played frame: a rocket fired at 50 and met at 120 replays along its path (B375).</remarks>
    [Test]
    public void Update_TheOpeningFrameEarlyInADemo_ReplaysATrailMetInFlight()
    {
        ParticleEffects effects = new();
        ParticleSystem trail = Emitting(key: 1, tick: 0).Definition;

        effects.Update([(7, Along(70f), Along(0f), 70)], trail, Interval, null, 120, 120d);

        effects.TrailEffect(7)!.Particles.Age.ShouldBe(70 * Interval, 1e-4f, "seventy ticks replayed");
    }

    /// <remarks>The trail twin of the burst seek control: a seek's frame is not played, so a trail met on it replays.</remarks>
    [Test]
    public void Update_ATrailFirstSeenOnASeek_ReplaysFromItsRocketsStart()
    {
        ParticleEffects effects = new();
        ParticleSystem trail = Emitting(key: 1, tick: 0).Definition;

        effects.Update([], trail, Interval, null, 0, 0.5d);
        // Started on tick 1, after the last frame — so only the seek flag tells this frame from a creation frame.
        effects.Update([(7, Along(5f), Along(0f), 2)], trail, Interval, null, 3, 3.25d, seeked: true);

        effects.TrailEffect(7)!.Particles.Age.ShouldBe(2 * Interval, 1e-6f, "two ticks replayed from the rocket's start");
    }

    private static ParticleControlPoint Along(float x) =>
        new(new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>Two particles a tick, each living a second, born on control point 0.</summary>
    private static ParticleBurst Emitting(long key, int tick) =>
        new(
            key,
            new ParticleSystem(
                Name: "test_caller",
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
