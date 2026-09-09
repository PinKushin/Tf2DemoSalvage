using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The effects a scene runs, one per live projectile (B373).
/// </summary>
[TestFixture]
public sealed class ParticleEffectsTests
{
    [Test]
    public void Update_AProjectile_GetsOneEffectThatFollowsIt()
    {
        // A trail is attached to the rocket - PATTACH_POINT_FOLLOW - so it moves with it rather
        // than staying where the rocket was when it spawned.
        ParticleEffects effects = new();

        // **The tick has to MOVE**, because the simulation advances on demo time rather than on
        // calls: a viewer parked on one tick advances nothing, which is what a paused engine does
        // (B375). Passing the same tick twice here would step the trail not at all.
        effects.Update([Rocket(0f)], Trail(), 1f / 66f, null, 1);
        effects.Update([Rocket(100f)], Trail(), 1f / 66f, null, 2);

        effects.Count.ShouldBe(1);

        List<DetailSpriteVertex> corners = Corners(effects);

        // Particles were laid at both ends of the flight, so the trail spans them.
        float leftmost = float.MaxValue;
        float rightmost = float.MinValue;

        foreach (DetailSpriteVertex corner in corners)
        {
            leftmost = MathF.Min(leftmost, corner.X);
            rightmost = MathF.Max(rightmost, corner.X);
        }

        (rightmost - leftmost).ShouldBeGreaterThan(50f);
    }

    [Test]
    public void Update_ARocketThatIsGone_KeepsItsTrailAndStopsEmitting()
    {
        // **A rocket explodes and its trail hangs in the air.** Removing the effect with the entity
        // would cut the trail off at the blast, which is the opposite of what an explosion looks
        // like - so the effect outlives the entity and fades on the particles' own schedule.
        //
        // **And it must stop EMITTING**, which is the bug this test exists for: stepping a dead
        // effect as though it were alive kept emitting at the trail's own position and grew it for
        // ever.
        ParticleEffects effects = new();

        for (int tick = 1; tick <= 5; tick++)
        {
            effects.Update([Rocket(tick * 10f)], Trail(), 1f / 66f, null, tick);
        }

        int laid = Count(effects);

        laid.ShouldBeGreaterThan(0);

        // The rocket is gone. One step later the trail is still there and no bigger.
        effects.Update([], Trail(), 1f / 66f, null, 6);

        effects.Count.ShouldBe(1);
        Count(effects).ShouldBeLessThanOrEqualTo(laid);
    }

    [Test]
    public void Update_ARocketGoneLongEnough_ForgetsTheEffectEntirely()
    {
        // The trail fades and then the effect is dropped, so a demo full of rockets does not
        // accumulate empty effects for the whole recording.
        ParticleEffects effects = new();

        effects.Update([Rocket(0f)], Trail(), 1f / 66f, null, 1);
        effects.Count.ShouldBe(1);

        // Well past the one-second lifetime the fixture declares.
        for (int tick = 2; tick < 102; tick++)
        {
            effects.Update([], Trail(), 1f / 66f, null, tick);
        }

        effects.Count.ShouldBe(0);
    }

    [Test]
    public void Update_WithNoDefinition_DoesNothingRatherThanThrowing()
    {
        // The definition is null when the `.pcf` could not be read - no TF2 install, which is every
        // CI run. That must cost the trail and nothing else.
        ParticleEffects effects = new();

        effects.Update([Rocket(0f)],null, 1f / 66f);

        effects.Count.ShouldBe(0);
    }

    [Test]
    public void Update_TheSameTickTwice_AdvancesNothing()
    {
        // **A paused demo advances no particles, and the engine is the same.** This used to step
        // once per CALL, so a viewer parked on one tick at 294 fps advanced the trail three hundred
        // times a second with the emitter frozen — every particle born at one point, which is the
        // dense puff the comparison against real TF2 showed (B375).
        ParticleEffects effects = new();

        effects.Update([Rocket(0f)], Trail(), 1f / 66f, null, 1);

        int laid = Count(effects);

        laid.ShouldBeGreaterThan(0);

        // Twenty more calls on the SAME tick, as a still frame makes.
        for (int again = 0; again < 20; again++)
        {
            effects.Update([Rocket(0f)], Trail(), 1f / 66f, null, 1);
        }

        Count(effects).ShouldBe(laid);
    }

    [Test]
    public void Update_AProjectileMetMidFlight_ReplaysItsTrailAlongThePath()
    {
        // **A seek gives an effect no history, and TF2 gets one by restarting and fast-forwarding.**
        // Met at x=500 having started at x=0 sixty ticks ago, the trail must span that path rather
        // than sit at the rocket — which is the whole difference the golden comparison showed.
        ParticleEffects effects = new();

        effects.Update(
            [(7, At(500f), At(0f), 60)], Trail(), 1f / 66f, null, 1);

        List<DetailSpriteVertex> corners = Corners(effects);

        corners.ShouldNotBeEmpty();

        float leftmost = float.MaxValue;
        float rightmost = float.MinValue;

        foreach (DetailSpriteVertex corner in corners)
        {
            leftmost = MathF.Min(leftmost, corner.X);
            rightmost = MathF.Max(rightmost, corner.X);
        }

        // The path is 500 units; particles live one second and 60 ticks is under that, so the
        // trail should cover most of it. A puff at the rocket spans a couple of units.
        (rightmost - leftmost).ShouldBeGreaterThan(300f);
    }

    [Test]
    public void Update_AProjectileWithNoHistory_StartsEmptyRatherThanGuessing()
    {
        // **The control for the replay**: without a start and an age there is nothing to replay
        // from, so the trail begins at the rocket and fills in as the demo plays. That is what
        // ordinary playback does, and it is why the test above is about the HISTORY rather than
        // about a trail existing at all.
        ParticleEffects effects = new();

        effects.Update([Rocket(500f)], Trail(), 1f / 66f, null, 1);

        foreach (DetailSpriteVertex corner in Corners(effects))
        {
            corner.X.ShouldBeInRange(495f, 505f);
        }
    }

    /// <summary>How many particles all the running effects hold.</summary>
    private static int Count(ParticleEffects effects)
    {
        List<DetailSpriteVertex> corners = Corners(effects);

        return corners.Count / ParticleSprites.CornersPerParticle;
    }

    /// <summary>A system emitting steadily, with a one-second life.</summary>
    /// <summary>Every corner the effects build this frame, across every material.</summary>
    /// <remarks>
    /// **The test system's material resolves to one entry with no sheet and no sequences**, which is
    /// what a particle drawn from a non-sheet texture takes: the whole image. These tests are about
    /// where quads ARE, not what is on them.
    /// </remarks>
    private static List<DetailSpriteVertex> Corners(ParticleEffects effects)
    {
        List<DetailSpriteVertex> corners = [];

        foreach (ParticleBatch batch in effects.Build(Vector3.UnitX, Vector3.UnitZ, Materials()))
        {
            corners.AddRange(batch.Corners);
        }

        return corners;
    }

    /// <summary>The one material the test system names.</summary>
    private static Dictionary<string, ParticleMaterial> Materials() =>
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            [ParticleEffects.MaterialOf(Trail())] =
                new ParticleMaterial(null, [], SpriteBlend.Translucent),
        };

    /// <summary>A rocket somewhere along the x axis, facing along it.</summary>
    /// <remarks>
    /// **Oriented rather than <c>Unoriented</c>**, because these tests are about a trail FOLLOWING
    /// a rocket and a control point with no facing is a different subject.
    /// </remarks>
    private static ParticleControlPoint At(float x) =>
        new(new Vector3(x, 0f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>One rocket at a place, with NO recorded history behind it.</summary>
    /// <remarks>
    /// **A null start is the case these tests are about**: they follow a trail forward from the
    /// moment it appears, which is what playback does. Replaying a history on first sight is a
    /// different behaviour with its own test (B375).
    /// </remarks>
    private static (int, ParticleControlPoint, ParticleControlPoint?, int) Rocket(float x) =>
        (7, At(x), null, 0);

    private static ParticleSystem Trail()
    {
        Dictionary<string, DmxValue> none = new(StringComparer.Ordinal);

        return new ParticleSystem(
            "rockettrail",
            [
                new ParticleFunction("emit_continuously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 66d),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 1d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 1d),
                    }),
            ],
            [],
            [],
            [],
            none);
    }
}
