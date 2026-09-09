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

        effects.Update([(7, new Vector3(0f, 0f, 0f))], Trail(), 1f / 66f);
        effects.Update([(7, new Vector3(100f, 0f, 0f))], Trail(), 1f / 66f);

        effects.Count.ShouldBe(1);

        List<DetailSpriteVertex> corners = [];

        effects.Build(Vector3.UnitX, Vector3.UnitZ, corners);

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

        for (int tick = 0; tick < 5; tick++)
        {
            effects.Update([(7, new Vector3(tick * 10f, 0f, 0f))], Trail(), 1f / 66f);
        }

        int laid = Count(effects);

        laid.ShouldBeGreaterThan(0);

        // The rocket is gone. One step later the trail is still there and no bigger.
        effects.Update([], Trail(), 1f / 66f);

        effects.Count.ShouldBe(1);
        Count(effects).ShouldBeLessThanOrEqualTo(laid);
    }

    [Test]
    public void Update_ARocketGoneLongEnough_ForgetsTheEffectEntirely()
    {
        // The trail fades and then the effect is dropped, so a demo full of rockets does not
        // accumulate empty effects for the whole recording.
        ParticleEffects effects = new();

        effects.Update([(7, Vector3.Zero)], Trail(), 1f / 66f);
        effects.Count.ShouldBe(1);

        // Well past the one-second lifetime the fixture declares.
        for (int tick = 0; tick < 100; tick++)
        {
            effects.Update([], Trail(), 1f / 66f);
        }

        effects.Count.ShouldBe(0);
    }

    [Test]
    public void Update_WithNoDefinition_DoesNothingRatherThanThrowing()
    {
        // The definition is null when the `.pcf` could not be read - no TF2 install, which is every
        // CI run. That must cost the trail and nothing else.
        ParticleEffects effects = new();

        effects.Update([(7, Vector3.Zero)], null, 1f / 66f);

        effects.Count.ShouldBe(0);
    }

    /// <summary>How many particles all the running effects hold.</summary>
    private static int Count(ParticleEffects effects)
    {
        List<DetailSpriteVertex> corners = [];

        effects.Build(Vector3.UnitX, Vector3.UnitZ, corners);

        return corners.Count / ParticleSprites.CornersPerParticle;
    }

    /// <summary>A system emitting steadily, with a one-second life.</summary>
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
