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
    public void MovementBasic_WithDrag_CarriesTheEnginesTimeScaledFraction()
    {
        // `C_OP_BasicMovement::Operate` (particles.lib): the step carried is `exp( ln( 1 − drag ) · 29.999998 · dt )`,
        // times `dt / prevDt` — drag is a fraction lost per thirtieth of a second, not per step. At 66 ticks a second
        // and drag 0.1 that carries 0.9533, where `1 − drag` would carry 0.9.
        ParticleStore particles = new();

        particles.Add(new Vector3(1f, 0f, 0f), lives: 10f);

        Dictionary<string, DmxValue> drag = new(StringComparer.Ordinal)
        {
            ["drag"] = new DmxValue(DmxAttributeType.Real, Number: 0.1d),
        };

        Step(particles, "Movement Basic", drag, seconds: 1f / 66f, previous: Vector3.Zero);

        particles.PositionOf(0).X.ShouldBe(1f + MathF.Exp(MathF.Log(0.9f) * 29.999998f / 66f), 1e-5d);
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

    // **`C_OP_FadeAndKill::Operate`, read out of `client.dll`** (`FUN_107aeb00`, B415). These replace two tests that
    // encoded B373's reading — flagged *interpolated* at the time — and the engine contradicts both. Per lane:
    //
    //     life = age · rcp( lifetime )
    //     if ( in_start  ≤ life < in_end  )  alpha = lerp( S(t), initial·start_alpha, initial )
    //     if ( out_start ≤ life < out_end )  alpha = lerp( S(t), initial, initial·end_alpha )
    //     S(t) = 3t² − 2t³ on t clamped to [0, 1]         — 3.0 and 2.0 at 0x10b51fa0 / 0x10b51f90
    //     outside both windows alpha is NOT WRITTEN
    //
    // The old reading had `start_alpha` as the HELD level; it is where the fade-in STARTS, as a fraction of the
    // particle's own alpha. The difference is every explosion's core flash — `start_alpha 0` with an empty fade-in
    // window — which the old reading held at zero for its whole life.

    /// <remarks>
    /// **`rockettrail` does not fade in.** It declares `start_alpha 1` over `0..0.1`, so the fade-in runs from
    /// <c>initial · 1</c> to <c>initial</c> — a constant. The old test asserted 0.5 here, on a rising ramp the engine
    /// never draws; every rocket's smoke began invisible at the rocket and TF2's does not.
    /// </remarks>
    [Test]
    public void AlphaFadeAndDecay_RocketTrailInsideItsFadeIn_HoldsItsOwnAlpha()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 20f);
        particles.Tick(1f);

        Step(particles, "Alpha Fade and Decay", Rocket(), seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(1f, 0.001d);
    }

    /// <remarks>
    /// **The fade-out is a smoothstep, not a line.** A quarter through `rockettrail`'s life is a sixth of the way
    /// across its `0.1..1` fade-out; <c>S(1/6) = 3/36 − 2/216 = 0.0741</c>, so alpha is <c>0.9259</c> — where a linear
    /// ramp, which the old test asserted, gives 0.8333.
    /// </remarks>
    [Test]
    public void AlphaFadeAndDecay_RocketTrailInsideItsFadeOut_FollowsTheSmoothstep()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(1f);

        Step(particles, "Alpha Fade and Decay", Rocket(), seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(0.9259259f, 0.0005d);
    }

    /// <remarks>
    /// **An explosion's core flash, which drew nothing.** `Explosion_CoreFlash` declares `start_alpha 0` with
    /// `start_fade_in_time` = `end_fade_in_time` = 0 — an EMPTY window, since the test is <c>in_start ≤ life &lt;
    /// in_end</c> — and a fade-out from 0.7. So until 70% of its life it keeps its initializer's alpha. The old reading
    /// held it at <c>initial · start_alpha</c> = zero, and the heart of every explosion was invisible.
    /// </remarks>
    [Test]
    public void AlphaFadeAndDecay_AnEmptyFadeInWindow_LeavesTheInitializersAlpha()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 1f);
        particles.Tick(0.5f);

        Step(particles, "Alpha Fade and Decay", CoreFlash(), seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(1f, 0.001d);
    }

    /// <remarks>
    /// **The fade-in runs FROM <c>initial · start_alpha</c> TO <c>initial</c>**, on the smoothstep. A window of
    /// <c>0..0.5</c> sampled at a quarter of the life is halfway across it, and <c>S(0.5) = 0.5</c> — so from 0 to 1 it
    /// is 0.5; at an eighth, <c>S(0.25) = 0.15625</c>, which a linear ramp would put at 0.25.
    /// </remarks>
    [TestCase(1f, 4f, 0.5f)]
    [TestCase(0.5f, 4f, 0.15625f)]
    public void AlphaFadeAndDecay_InsideAFadeIn_RisesOnTheSmoothstep(float age, float lives, float expected)
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: lives);
        particles.Tick(age);

        Step(particles, "Alpha Fade and Decay", Windows(startAlpha: 0d, inFrom: 0d, inTo: 0.5d, outFrom: 0.9d, outTo: 1d),
            seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(expected, 0.001d);
    }

    /// <remarks>
    /// **Between the windows the operator writes nothing at all** — its stores are masked by the window tests. So an
    /// alpha another operator (or anything) set is left alone. Pinned with a value the initializer never gave, which
    /// an operator that re-held the "held level" would overwrite.
    /// </remarks>
    [Test]
    public void AlphaFadeAndDecay_BetweenItsWindows_WritesNothing()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(2f);
        particles.Fade(0, 0.3f);

        Step(particles, "Alpha Fade and Decay", Windows(startAlpha: 0d, inFrom: 0d, inTo: 0.1d, outFrom: 0.9d, outTo: 1d),
            seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(0.3f);
    }

    [Test]
    public void ColorFade_ReadsTheSpawnTint_SoItDoesNotConvergeEarly()
    {
        // **Reading the CURRENT tint would make every step a fresh interpolation from wherever the
        // last one landed**, converging on the target far faster than the declared window - the
        // same compounding shape that took a radius to 265, and equally invisible in one step.
        //
        // Ten steps at a fixed halfway point must leave the tint halfway, not at the target.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(2f);

        Dictionary<string, DmxValue> black = new(StringComparer.Ordinal)
        {
            ["color_fade"] = new DmxValue(DmxAttributeType.Colour, Vector: new Vector4(0f, 0f, 0f, 0f)),
            ["fade_start_time"] = new DmxValue(DmxAttributeType.Real, 0d),
            ["fade_end_time"] = new DmxValue(DmxAttributeType.Real, 1d),
        };

        for (int step = 0; step < 10; step++)
        {
            Step(particles, "Color Fade", black, seconds: 0f, previous: null);
        }

        // Halfway from 255 to 0, linear because ease_in_and_out is absent and defaults off.
        particles.TintOf(0).X.ShouldBe(127.5f, 0.01d);
    }

    // **`C_OP_OscillateScalar::Operate`, read out of `client.dll`** (`FUN_107a5220`, B415). Each step adds
    // `rate · dt · SinEst01( arg )` to the chosen field, with `arg = freq · ( multiplier · curtime + phase )`, or
    // `age · rcp( lifetime ) · freq · multiplier + phase` when proportional. `SinEst01SIMD` (`ssemath.h:3129`) is the
    // PARABOLA `x(4 − 4x)` over a period of 2, not a sine — so at an argument of 0.25 it gives exactly 0.75, where a
    // true sine gives 0.7071 and the 0.225-blended `Sin01SIMD` 0.7078.
    //
    // Most cases below hold multiplier 1, curtime 1 and phase 0 so the argument IS the frequency, and rate 2 over a
    // half-second step so the addition IS the sine estimate.

    [TestCase(0.25f, 0.75f)]
    [TestCase(1.25f, -0.75f)]
    [TestCase(-0.25f, -0.75f)]
    [TestCase(2.25f, 0.75f)]
    public void OscillateScalar_AtAnArgument_AddsTheParabolaNotASine(float argument, float expected)
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(1f);

        Step(particles, "Oscillate Scalar", Oscillation(field: 4, frequency: argument, proportional: false),
            seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(expected);
    }

    /// <remarks>
    /// **Proportional puts the LIFE FRACTION where the clock was.** Half-way through a two-second life with frequency
    /// 0.5, the argument is about 0.25 and the estimate about 0.75; read off the clock instead it would be 0.5 and 1.
    /// The fraction goes through `rcpps`, hence the tolerance.
    /// </remarks>
    [Test]
    public void OscillateScalar_WhenProportional_ReadsTheLifeFraction()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 2f);
        particles.Tick(1f);

        Step(particles, "Oscillate Scalar", Oscillation(field: 4, frequency: 0.5d, proportional: true),
            seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(0.75f, 0.001d);
    }

    [Test]
    public void OscillateScalar_BeforeItsStartTime_WritesNothing()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(1f);

        // A quarter through its life, and the window opens at a half.
        Step(particles, "Oscillate Scalar", Oscillation(field: 4, frequency: 0.25d, proportional: false, start: 0.5d),
            seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(0f);
    }

    /// <remarks>ALPHA, field 7 and the default, is the one field clamped to [0, 1] (`MINPS`/`MAXPS` at `0x107a56c8`).</remarks>
    [Test]
    public void OscillateScalar_OnAlpha_ClampsToOne()
    {
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 4f);
        particles.Tick(1f);
        particles.Fade(0, 0.9f);

        Step(particles, "Oscillate Scalar", Oscillation(field: 7, frequency: 0.25d, proportional: false),
            seconds: 0.5f, previous: null);

        particles.AlphaOf(0).ShouldBe(1f);
    }

    /// <summary>`Oscillate Scalar` with rate 2, multiplier 1 and phase 0, and a start/end window as life fractions.</summary>
    private static Dictionary<string, DmxValue> Oscillation(
        double field, double frequency, bool proportional, double start = 0d) =>
        new(StringComparer.Ordinal)
        {
            ["oscillation field"] = new DmxValue(DmxAttributeType.Whole, field),
            ["oscillation rate min"] = new DmxValue(DmxAttributeType.Real, 2d),
            ["oscillation rate max"] = new DmxValue(DmxAttributeType.Real, 2d),
            ["oscillation frequency min"] = new DmxValue(DmxAttributeType.Real, frequency),
            ["oscillation frequency max"] = new DmxValue(DmxAttributeType.Real, frequency),
            ["oscillation multiplier"] = new DmxValue(DmxAttributeType.Real, 1d),
            ["oscillation start phase"] = new DmxValue(DmxAttributeType.Real, 0d),
            ["proportional 0/1"] = new DmxValue(DmxAttributeType.Boolean, proportional ? 1d : 0d),
            ["start time min"] = new DmxValue(DmxAttributeType.Real, start),
            ["start time max"] = new DmxValue(DmxAttributeType.Real, start),
        };

    /// <summary>`Explosion_CoreFlash`'s own Alpha Fade and Decay parameters, as the shipped `.pcf` declares them.</summary>
    private static Dictionary<string, DmxValue> CoreFlash() =>
        Windows(startAlpha: 0d, inFrom: 0d, inTo: 0d, outFrom: 0.7d, outTo: 1d, endAlpha: 0d);

    /// <summary>A fade with explicit windows.</summary>
    private static Dictionary<string, DmxValue> Windows(
        double startAlpha, double inFrom, double inTo, double outFrom, double outTo, double endAlpha = 0d) =>
        new(StringComparer.Ordinal)
        {
            ["start_alpha"] = new DmxValue(DmxAttributeType.Real, startAlpha),
            ["end_alpha"] = new DmxValue(DmxAttributeType.Real, endAlpha),
            ["start_fade_in_time"] = new DmxValue(DmxAttributeType.Real, inFrom),
            ["end_fade_in_time"] = new DmxValue(DmxAttributeType.Real, inTo),
            ["start_fade_out_time"] = new DmxValue(DmxAttributeType.Real, outFrom),
            ["end_fade_out_time"] = new DmxValue(DmxAttributeType.Real, outTo),
        };

    /// <summary>The real rockettrail's own Alpha Fade and Decay parameters.</summary>
    private static Dictionary<string, DmxValue> Rocket() =>
        new(StringComparer.Ordinal)
        {
            ["start_alpha"] = new DmxValue(DmxAttributeType.Real, 1d),
            ["end_alpha"] = new DmxValue(DmxAttributeType.Real, 0d),
            ["start_fade_in_time"] = new DmxValue(DmxAttributeType.Real, 0d),
            ["end_fade_in_time"] = new DmxValue(DmxAttributeType.Real, 0.1d),
            ["start_fade_out_time"] = new DmxValue(DmxAttributeType.Real, 0.1d),
            ["end_fade_out_time"] = new DmxValue(DmxAttributeType.Real, 1d),
        };

    [Test]
    public void All_EveryOperator_IsFoundByTheNameAPcfUses()
    {
        // The registry is keyed by `functionName` because that is all a `.pcf` carries. A mismatch
        // between the key and the name in the file is silent - the operator simply never runs.
        IReadOnlyDictionary<string, IParticleOperator> all = ParticleOperators.All();

        all.ShouldContainKey("Movement Basic");
        all.ShouldContainKey("Lifespan Decay");
        all.ShouldContainKey("Alpha Fade Out Random");
        all.ShouldContainKey("Oscillate Scalar");

        foreach ((string named, IParticleOperator one) in all)
        {
            one.Named.ShouldBe(named);
        }
    }

    [Test]
    public void RemapScalar_CreationTimeToRadius_IsTheClampedLine()
    {
        // `C_OP_RemapScalar::Operate` (particles.lib): t = clamp( (in − inMin) / (inMax − inMin) ), out = lerp( outMin,
        // outMax, t ). `rocketjump_smoke`'s own mapping: born 0..2 s → radius 11..5.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);
        particles.Tick(1f);
        particles.Add(Vector3.Zero, lives: 10f);
        particles.Tick(2f);
        particles.Add(Vector3.Zero, lives: 10f);

        Step(particles, "Remap Scalar", Remap(8, 0f, 2f, 3, 11f, 5f), seconds: 0f, previous: null);

        particles.RadiusOf(0).ShouldBe(11f, 1e-5d);
        particles.RadiusOf(1).ShouldBe(8f, 1e-5d);
        particles.RadiusOf(2).ShouldBe(5f, 1e-5d, "born at 3 s, past the input's maximum");
    }

    [Test]
    public void RemapScalar_EqualInputBounds_IsAStepAtThem()
    {
        // `if ( inMin == inMax ) out = ( in >= inMax ) ? outMax : outMin` — no division.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);
        particles.Tick(1f);
        particles.Add(Vector3.Zero, lives: 10f);

        Step(particles, "Remap Scalar", Remap(8, 1f, 1f, 3, 2f, 7f), seconds: 0f, previous: null);

        particles.RadiusOf(0).ShouldBe(2f);
        particles.RadiusOf(1).ShouldBe(7f);
    }

    [Test]
    public void RemapScalar_IntoAlpha_ClampsItsOutputBoundsToOne()
    {
        // An output field in the mask `0x10080` — ALPHA (7) and ALPHA2 (16) — has both bounds clamped to [0, 1]
        // before the line is drawn.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);

        Step(particles, "Remap Scalar", Remap(8, 0f, 1f, 7, -1f, 3f), seconds: 0f, previous: null);

        particles.AlphaOf(0).ShouldBe(0f, "born at 0, so the clamped minimum");
    }

    [Test]
    public void RemapScalar_NothingDeclared_MapsAlphaOntoRadius()
    {
        // The unpack defaults: input field 7 (alpha) over 0..1, output field 3 (radius) over 0..1.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);
        particles.Alpha[0] = 0.25f;

        Step(particles, "Remap Scalar", None, seconds: 0f, previous: null);

        particles.RadiusOf(0).ShouldBe(0.25f, 1e-6d);
    }

    [Test]
    public void RotationSpinRoll_NoStopTime_TurnsByTheRateAsRevolutionsModTwoPi()
    {
        // `CGeneralSpin::InitParams` converts `spin_rate_degrees` to radians, and `Operate` then takes that as
        // REVOLUTIONS: `drot = fmod( dt · |rate · 2π|, 2π )` when `spin_stop_time` is 0. 36° is 0.6283 rad, so a half
        // second turns 0.5 · 0.6283 · 2π = 1.9739 rad — not the 0.314 the parameter's name suggests.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);

        Step(particles, "Rotation Spin Roll", Spin(36, 0, 0f), seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(0.5f * float.DegreesToRadians(36f) * MathF.Tau, 1e-5d);
    }

    [Test]
    public void RotationSpinRoll_WithAStopTime_SlowsAcrossThatFractionOfTheLife()
    {
        // `factor = max( 0, 1 − ( now − born ) / ( stop · life ) )`: half way through a stop window of 0.5 of a
        // 2-second life, the turn is halved; no `fmod` once a stop time is set.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 2f);
        particles.Tick(0.5f);

        Step(particles, "Rotation Spin Roll", Spin(36, 0, 0.5f), seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(0.5f * 0.5f * float.DegreesToRadians(36f) * MathF.Tau, 1e-5d);
    }

    [Test]
    public void RotationSpinRoll_PastTwoPi_WrapsBack()
    {
        // `if ( rot >= 2π ) rot −= 2π`.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);
        particles.Rotation[0] = 6f;

        Step(particles, "Rotation Spin Roll", Spin(36, 0, 0f), seconds: 0.5f, previous: null);

        particles.RotationOf(0).ShouldBe(6f + (0.5f * float.DegreesToRadians(36f) * MathF.Tau) - MathF.Tau, 1e-4d);
    }

    private static Dictionary<string, DmxValue> Spin(int degrees, int minimum, float stop) =>
        new(StringComparer.Ordinal)
        {
            ["spin_rate_degrees"] = new DmxValue(DmxAttributeType.Whole, Number: degrees),
            ["spin_rate_min"] = new DmxValue(DmxAttributeType.Whole, Number: minimum),
            ["spin_stop_time"] = new DmxValue(DmxAttributeType.Real, Number: stop),
        };

    private static Dictionary<string, DmxValue> Remap(
        int input, float inputMinimum, float inputMaximum, int output, float outputMinimum, float outputMaximum) =>
        new(StringComparer.Ordinal)
        {
            ["input field"] = new DmxValue(DmxAttributeType.Whole, Number: input),
            ["input minimum"] = new DmxValue(DmxAttributeType.Real, Number: inputMinimum),
            ["input maximum"] = new DmxValue(DmxAttributeType.Real, Number: inputMaximum),
            ["output field"] = new DmxValue(DmxAttributeType.Whole, Number: output),
            ["output minimum"] = new DmxValue(DmxAttributeType.Real, Number: outputMinimum),
            ["output maximum"] = new DmxValue(DmxAttributeType.Real, Number: outputMaximum),
        };

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
