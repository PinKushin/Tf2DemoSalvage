using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The legacy impact effects — `PerformCustomEffects` (`fx_impact.cpp`), `CSimpleEmitter`, `CDustParticle`,
/// `CFleckParticles`, `CTrailParticles` and `CParticleCollision` — against the published source (B415).
/// </summary>
public sealed class ImpactEffectsConformanceTests
{
    /// <summary>A floor at z = 0: the only surface in the world.</summary>
    private static readonly Func<Vector3, Vector3, BspTrace> Floor = static (from, to) =>
        from.Z > 0f && to.Z <= 0f
            ? new BspTrace(from.Z / (from.Z - to.Z), 0, (0f, 0f, 1f), false, 0f)
            : new BspTrace(1f, -1, default, false);

    /// <summary>A draw that answers the middle of every range.</summary>
    private static readonly Func<float, float, float> Middle = static (least, most) => (least + most) * 0.5f;

    [Test]
    public void Step_ADustParticle_DecaysToItsSpeedFloor()
    {
        // `decay = exp( log( 0.0001 ) · dt / 0.5 )`: half a second takes 1000 to 0.1, below 32, so it is set to 32.
        ImpactEmitter dust = new(ImpactEmitterKind.Dust);

        dust.Particles.Add(new ImpactParticle("m", Vector3.Zero, new Vector3(1000f, 0f, 0f), 5f));
        dust.Step(0.5f, Floor, Middle);

        dust.Particles[0].Velocity.X.ShouldBe(32f, 1e-3f);
    }

    [Test]
    public void Step_ADustParticle_DecaysItsRollToAFloor()
    {
        // `delta += delta · ( dt · −8 )`: 0.25 s takes 1 to −1, whose magnitude is not below 0.5; 0.1 s takes 1 to 0.2,
        // which is.
        ImpactEmitter dust = new(ImpactEmitterKind.Dust);

        dust.Particles.Add(new ImpactParticle("m", Vector3.Zero, Vector3.Zero, 5f) { RollDelta = 1f });
        dust.Step(0.1f, Floor, Middle);

        dust.Particles[0].RollDelta.ShouldBe(0.5f);
    }

    [Test]
    public void Step_ASimpleParticle_DiesAtItsDieTime()
    {
        ImpactEmitter simple = new(ImpactEmitterKind.Simple);

        simple.Particles.Add(new ImpactParticle("m", Vector3.Zero, Vector3.UnitX, 0.2f));
        simple.Step(0.1f, Floor, Middle);
        simple.Particles.Count.ShouldBe(1);
        simple.Step(0.1f, Floor, Middle);
        simple.Particles.Count.ShouldBe(0);
    }

    [Test]
    public void Move_ASlowFallOntoAFloor_Settles()
    {
        // `normal.z >= 0.5 && |velocity.z| <= 48`: left at the collision point, stopped.
        ImpactParticleCollision collision = new();

        collision.Setup(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, 64f, 128f, 800f, 0.3f, Floor);

        Vector3 position = new(0f, 0f, 0.1f);
        Vector3 velocity = new(0f, 0f, -20f);
        float roll = 3f;

        collision.Move(ref position, ref velocity, ref roll, 0.01f, Floor, Middle);

        velocity.ShouldBe(Vector3.Zero);
        roll.ShouldBe(0f);
    }

    [Test]
    public void Move_AFastFallOntoAFloor_BouncesDamped()
    {
        // Reflected, then `RandomFloat( dampen − 0.1, dampen + 0.1 )` — the middle, 0.3 — and the roll turned by −0.25.
        ImpactParticleCollision collision = new();

        collision.Setup(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, 64f, 128f, 800f, 0.3f, Floor);

        Vector3 position = new(0f, 0f, 1f);
        Vector3 velocity = new(10f, 0f, -200f);
        float roll = 4f;

        collision.Move(ref position, ref velocity, ref roll, 0.01f, Floor, Middle);

        velocity.X.ShouldBe(3f, 1e-4f);
        velocity.Z.ShouldBe((200f + 8f) * 0.3f, 1e-3f);
        roll.ShouldBe(-1f);
    }

    [Test]
    public void Move_WithNoPlaneFoundAtSetup_NeverCollides()
    {
        // Setup straight up finds nothing, so a particle then falling through the floor is not stopped by it.
        ImpactParticleCollision collision = new();

        collision.Setup(new Vector3(0f, 0f, 10f), Vector3.UnitZ, 64f, 128f, 0f, 0.3f, Floor);
        collision.PlaneCount.ShouldBe(0);

        Vector3 position = new(0f, 0f, 1f);
        Vector3 velocity = new(0f, 0f, -200f);
        float roll = 0f;

        collision.Move(ref position, ref velocity, ref roll, 0.01f, Floor, Middle);

        position.Z.ShouldBeLessThan(0f);
    }

    [Test]
    public void Step_ATrail_IsDampedLaterally()
    {
        // `attenuation = 1 − dt · m_flVelocityDampen`: 0.05 s at 8 is 0.6.
        ImpactEmitter sparks = new(ImpactEmitterKind.Trail) { VelocityDampen = 8f };

        sparks.Collision.Gravity = 0f;
        sparks.Particles.Add(new ImpactParticle("m", new Vector3(0f, 0f, 100f), new Vector3(100f, 0f, 0f), 1f));
        sparks.Step(0.05f, Floor, Middle);

        sparks.Particles[0].Velocity.X.ShouldBe(60f, 1e-4f);
    }

    [TestCase('C', 2, TestName = "Perform_Concrete_IsFlecksAndADustTrail")]
    [TestCase('T', 2, TestName = "Perform_Tile_IsFlecksAndADustTrail")]
    [TestCase('W', 2, TestName = "Perform_Wood_IsFlecksAndADustTrail")]
    [TestCase('D', 1, TestName = "Perform_Dirt_IsOneDustEmitter")]
    [TestCase('N', 1, TestName = "Perform_Sand_IsOneDustEmitter")]
    [TestCase('M', 1, TestName = "Perform_Metal_IsOneSparkEmitter")]
    [TestCase('V', 1, TestName = "Perform_Vent_IsOneSparkEmitter")]
    [TestCase('P', 3, TestName = "Perform_Computer_IsTheElectricSparksThreeEmitters")]
    public void Perform_AMaterial_IsItsBranchsEmitters(char material, int emitters)
    {
        Perform(material)!.Emitters.Count.ShouldBe(emitters);
    }

    [TestCase('G', TestName = "Perform_Grate_IsNothing")]
    [TestCase('F', TestName = "Perform_Flesh_IsNothing")]
    public void Perform_AMaterialWithNoBranch_IsNothing(char material)
    {
        Perform(material).ShouldBeNull();
    }

    [TestCase(SurfaceProperties.Sky)]
    [TestCase(SurfaceProperties.NoDraw)]
    [TestCase(SurfaceProperties.Hint)]
    [TestCase(SurfaceProperties.Skip)]
    public void Perform_ASurfaceTheEffectsRefuse_IsNothing(SurfaceProperties flags)
    {
        Perform('C', flags).ShouldBeNull();
    }

    [Test]
    public void Perform_Dirt_IsTwelveParticlesFromTheOrigin()
    {
        // Four of each of three kinds at full throttle, and the third kind's offset is computed and never used.
        ImpactEmitter dust = Perform('D')!.Emitters[0];

        dust.Kind.ShouldBe(ImpactEmitterKind.Dust);
        dust.Particles.Count.ShouldBe(12);
        dust.Particles.ShouldAllBe(particle => particle.Position == new Vector3(0f, 0f, 1f));
    }

    [Test]
    public void Perform_Metal_GlowsOnTheSurfaceForATenthOfASecond()
    {
        ImpactQuad glow = Perform('M')!.Quads.Single();

        glow.Material.ShouldBe(ImpactEffects.YellowFlare);
        glow.Origin.ShouldBe(new Vector3(0f, 0f, 2f), "one unit off the surface");
        glow.DieTime.ShouldBe(0.1f);
        glow.Alpha.ShouldBe((1f, 0f));
    }

    [Test]
    public void Perform_Concrete_TintsItsFlecksBySurfaceColour()
    {
        ImpactEmitter flecks = Perform('C')!.Emitters[0];

        // `MIN( 1, colour · ramp ) · 255` with the middle ramp of 1.0 and a colour of 0.5.
        flecks.Kind.ShouldBe(ImpactEmitterKind.Fleck);
        flecks.Particles.Select(fleck => fleck.Colour).Distinct().ShouldBe([((byte)127, (byte)127, (byte)127)]);
    }

    [Test]
    public void Build_ADustParticle_FadesAsItsSquareBelowThreeQuarters()
    {
        // `ramp = 1 − t`, squared below 0.75: half-way through, 0.25 — and 64 units from the eye, so no near fade.
        ImpactEffect effect = new();
        ImpactEmitter dust = new(ImpactEmitterKind.Dust);

        dust.Particles.Add(new ImpactParticle("m", new Vector3(100f, 0f, 0f), Vector3.Zero, 1f) { Lifetime = 0.5f, StartSize = 4, EndSize = 4 });
        effect.Emitters.Add(dust);

        Dictionary<string, List<DetailSpriteVertex>> corners = [];

        ImpactDraw.Build(effect, Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, static _ => 1f, corners);

        corners["m"].Count.ShouldBe(6);
        corners["m"][0].Alpha.ShouldBe(MathF.Round(0.25f * 254.9f) / 255f, 1e-6f);
    }

    [Test]
    public void Build_ASimpleParticleCloserThan16Units_IsNotDrawn()
    {
        ImpactEffect effect = new();
        ImpactEmitter simple = new(ImpactEmitterKind.Simple);

        simple.Particles.Add(new ImpactParticle("m", new Vector3(10f, 0f, 0f), Vector3.Zero, 1f) { StartAlpha = 255, EndAlpha = 255 });
        effect.Emitters.Add(simple);

        Dictionary<string, List<DetailSpriteVertex>> corners = [];

        ImpactDraw.Build(effect, Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, static _ => 1f, corners);

        corners["m"].ShouldBeEmpty();
    }

    private static ImpactEffect? Perform(char material, SurfaceProperties flags = SurfaceProperties.None) =>
        ImpactEffects.Perform(
            material,
            flags,
            new Vector3(0f, 0f, 1f),
            Vector3.UnitZ,
            -Vector3.UnitZ,
            static () => new Vector3(0.5f, 0.5f, 0.5f),
            Floor,
            Middle);
}
