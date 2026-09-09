using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The particle operators, and what each assertion is grounded in (B373).
/// </summary>
/// <remarks>
/// **These pin the DECLARED behaviour, not visual parity with TF2.** The operator implementations
/// are absent from the SDK, so what can be asserted here is that a particle does what its
/// parameters say — the integrator is the one Valve's header names, a fade runs over the fraction
/// its parameter gives, an absent parameter takes the documented default. What CANNOT be asserted
/// without a capture of the same effect in TF2 is that the result looks like the engine's, and
/// `docs/RISKS.md` B373 says so rather than letting a green suite imply it.
/// </remarks>
[TestFixture]
public sealed class ParticleOperatorConformanceTests
{
    [Test]
    public void MovementBasic_AParticleWithNoDragOrGravity_CarriesItsStepForwardUnchanged()
    {
        // **Verlet, and this is the assertion that says so** — `particles.h:68` declares PREV_XYZ
        // "for verlet integration", so velocity is implied by the gap between position and
        // previous. A particle one unit ahead of where it was moves one more unit, with no stored
        // velocity anywhere.
        //
        // **An Euler integrator would need a velocity to carry and has none**, so it would leave
        // this particle still. That is the difference this test exists to catch.
        ParticleStore particles = new();

        particles.Add(new Vector3(1f, 0f, 0f), lives: 10f);

        // Place it as though it had already travelled one unit along x.
        Step(particles, "Movement Basic", None, seconds: 0f, previous: new Vector3(0f, 0f, 0f));

        particles.PositionOf(0).X.ShouldBe(2f, 0.0001d);
    }

    [Test]
    public void MovementBasic_WithGravity_AddsItScaledByTheSquareOfTheStep()
    {
        // Verlet applies an acceleration as `a * dt²`, not `a * dt` — a distinction invisible at
        // one fixed step size and glaring at another, which is why the value is asserted rather
        // than the direction.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);

        Dictionary<string, DmxValue> gravity = new(StringComparer.Ordinal)
        {
            ["gravity"] = new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, -8f, 0f)),
        };

        Step(particles, "Movement Basic", gravity, seconds: 0.5f, previous: Vector3.Zero);

        // -8 * 0.5 * 0.5 = -2
        particles.PositionOf(0).Z.ShouldBe(-2f, 0.0001d);
    }

    [Test]
    public void LifespanDecay_AParticlePastItsDuration_IsRemovedAndAYoungerOneIsNot()
    {
        // The control is the second particle: an operator that removed everything would pass a
        // test that only checked the first.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Tick(2f);
        particles.Add(Vector3.Zero, lives: 10f);

        Step(particles, "Lifespan Decay", None, seconds: 0f, previous: null);

        particles.Count.ShouldBe(1);
        particles.LifetimeOf(0).ShouldBe(10f);
    }

    [Test]
    public void AlphaFadeOut_HalfwayThroughItsFadeWindow_IsHalfTransparent()
    {
        // `fade_out_time_max` is a FRACTION of the life, so a fade of 0.5 begins halfway through.
        // Asserted at three-quarters, where a linear fade is exactly half faded.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(3f);

        Dictionary<string, DmxValue> fade = new(StringComparer.Ordinal)
        {
            ["fade_out_time_max"] = new DmxValue(DmxAttributeType.Real, 0.5d),
        };

        Step(particles, "Alpha Fade Out Random", fade, seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(0.5f, 0.0001d);
    }

    [Test]
    public void RadiusScale_WithNeitherScaleDeclared_LeavesTheRadiusAlone()
    {
        // **Absent must not mean zero here or every particle would vanish**, which is the sentinel
        // trap this codebase keeps meeting. Both scales default to 1.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(2f);

        Step(particles, "Radius Scale", None, seconds: 0f, previous: null);

        particles.RadiusOf(0).ShouldBe(1f, 0.0001d);
    }

    [Test]
    public void RadiusScale_RunEveryStepAsTheEngineDoes_DoesNotCompoundItsOwnOutput()
    {
        // **The defect a one-step test cannot see, and it was found by running Valve's own
        // rockettrail definition for twenty steps: the radius reached 265.**
        //
        // An operator runs every frame, so one that reads the CURRENT radius and multiplies it
        // writes its own input next step. `particles.h:602` names the mechanism that avoids it —
        // `GetReadInitialAttributes`, "used when an operator needs to read the attributes of a
        // particle at spawn time" — so a scale reads the spawn radius and writes the current one.
        //
        // Twenty steps at a scale of 2 must therefore end at 2, not at 2^20.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 100f);

        Dictionary<string, DmxValue> doubled = new(StringComparer.Ordinal)
        {
            ["radius_start_scale"] = new DmxValue(DmxAttributeType.Real, 2d),
            ["radius_end_scale"] = new DmxValue(DmxAttributeType.Real, 2d),
        };

        for (int step = 0; step < 20; step++)
        {
            Step(particles, "Radius Scale", doubled, seconds: 0f, previous: null);
        }

        particles.RadiusOf(0).ShouldBe(2f, 0.0001d);
    }

    [Test]
    public void All_EveryOperator_IsFoundByTheNameAPcfUses()
    {
        // The registry is keyed by `functionName` because that is all a `.pcf` carries. A mismatch
        // between the key and the name in the file is silent - the operator simply never runs.
        IReadOnlyDictionary<string, IParticleOperator> all = ParticleOperators.All();

        all.ShouldContainKey("Movement Basic");
        all.ShouldContainKey("Lifespan Decay");
        all.ShouldContainKey("Alpha Fade Out Random");

        foreach ((string named, IParticleOperator one) in all)
        {
            one.Named.ShouldBe(named);
        }
    }

    /// <summary>An operator that declares nothing, so every parameter takes its default.</summary>
    private static readonly Dictionary<string, DmxValue> None = new(StringComparer.Ordinal);

    /// <summary>Runs one operator over the store, optionally placing the particle's previous point.</summary>
    private static void Step(
        ParticleStore particles,
        string named,
        IReadOnlyDictionary<string, DmxValue> parameters,
        float seconds,
        Vector3? previous)
    {
        if (previous is { } was)
        {
            particles.Previous[0] = was;
        }

        ParticleOperators.All()[named].Operate(
            particles, new ParticleFunction(named, named, parameters), seconds);
    }
}
