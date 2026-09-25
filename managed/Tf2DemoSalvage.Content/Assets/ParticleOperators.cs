using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One of Valve's particle operators, applied to a live collection.</summary>
/// <remarks>
/// **A strategy per operator, resolved by the name the file carries**, which is the shape the
/// format already has: a `.pcf` names a class in `functionName` and a new operator is a new type
/// rather than another branch. That is the open/closed rule this repository asks for, and here it
/// also matches the engine, whose operators are separate classes registered by name.
/// </remarks>
public interface IParticleOperator
{
    /// <summary>The <c>functionName</c> a <c>.pcf</c> uses for this operator.</summary>
    /// <remarks>Called <c>Named</c> rather than <c>Function</c> because CA1716 reserves the latter.</remarks>
    public string Named { get; }

    /// <summary>Applies it to every live particle.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="parameters">This instance's declared parameters.</param>
    /// <param name="seconds">How long this step is.</param>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds);
}

/// <summary>
/// The operators this project implements, and what each one's behaviour is grounded in (B373).
/// </summary>
/// <remarks>
/// **The ranking decided which ones exist here.** `particles operators` counted what TF2's 134
/// `.pcf` files actually instantiate — 10,456 systems, 110 distinct operators — and the head of
/// that distribution is what a rocket trail uses. Implementing the tail first would be building
/// for unusual hats.
///
/// **Evidence, stated per operator, because it is NOT uniform.** `src/particles/*.cpp` is absent
/// from the SDK, so nothing here is transcribed from an implementation:
///
/// | grounding | which |
/// |---|---|
/// | **read-from-source** | the particle attributes themselves, and that movement is VERLET — `PREV_XYZ` is documented "for verlet integration" (`particles.h:68`) |
/// | **read-from-data** | every parameter name and its declared value, out of the shipped `.pcf` files through this project's own reader |
/// | **interpolated** | how a named parameter combines — that `lifetime_min`/`lifetime_max` bound a uniform draw, that a fade runs over the 0..1 life fraction |
///
/// **The interpolated half is not verified and must not be reported as parity.** It produces
/// plausible motion from Valve's own numbers, which is worth having and is not the same claim. What
/// would settle it is a capture of the same effect in TF2 beside ours
/// (`docs/findings/24-reference-capture.md`), and until that exists this is a simulation of the
/// declared parameters rather than a reproduction of the engine.
/// </remarks>
public static class ParticleOperators
{
    /// <summary>Every operator this implements, by the name a <c>.pcf</c> uses.</summary>
    /// <returns>A lookup from <c>functionName</c> to its implementation.</returns>
    public static IReadOnlyDictionary<string, IParticleOperator> All()
    {
        Dictionary<string, IParticleOperator> all = new(StringComparer.Ordinal);

        foreach (IParticleOperator one in (IParticleOperator[])
        [
            new MovementBasic(),
            new LifespanDecay(),
            new AlphaFadeOut(),
            new AlphaFadeIn(),
            new RadiusScale(),
            new AlphaFadeAndDecay(),
            new ColourFade(),
            new OscillateScalar(),
            new RemapScalar(),
            new RotationSpinRoll(),
        ])
        {
            all[one.Named] = one;
        }

        return all;
    }
}

/// <summary>
/// <c>Movement Basic</c> — the integrator, and the most-used operator in the game.
/// </summary>
/// <remarks>
/// **Verlet, and that is read from source rather than chosen.** `particles.h:68` declares
/// `PREV_XYZ` as "prev coordinates for verlet integration", so a particle carries where it WAS and
/// not how fast it is going. The step is therefore
///
/// <code>
/// next = position + (position - previous) * (1 - drag) + gravity * dt²
/// </code>
///
/// **An Euler integrator with a stored velocity would be a different simulation wearing the same
/// parameters** — it responds to a change in `dt` differently, which is exactly the kind of
/// divergence that looks right in a still frame.
///
/// **`drag` and `gravity` are the declared parameters**, measured on `rockettrail.pcf`:
/// `Movement Basic` there declares `drag 0` and a `gravity` vector, and omits nothing else that
/// this reads.
///
/// **How drag scales the step was read out of `particles.lib`** (`C_OP_BasicMovement::Operate`, B396), and it
/// REPLACED a reading this class carried as *interpolated* — `( 1 − drag )` per step. The engine's is
/// `exp( ln( 1 − drag ) · 29.999998 · dt ) · dt / prevDt`: drag is lost per thirtieth of a second, whatever the step,
/// so at 66 ticks a second and drag 0.1 a particle keeps 0.953 of its step, not 0.9. Drag 0, which is the rocket
/// trail's, is unchanged. **The same operator then applies the system's constraints**, which
/// <see cref="ParticleEffect"/> does after it. *Not built:* force generators (`C_OP_*Force`).
/// </remarks>
public sealed class MovementBasic : IParticleOperator
{
    /// <summary>`0x41efffff`: drag is a fraction lost per thirtieth of a second.</summary>
    private const float DragPerSecond = 29.999998f;

    /// <inheritdoc/>
    public string Named => "Movement Basic";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        Vector4 gravity = parameters.Vector("gravity", default);
        float drag = MathF.Max(0f, (float)parameters.Number("drag", 0d));

        // `C_OP_BasicMovement::Operate`: the fraction carried is `exp( ln( 1 − drag ) · 29.999998 · dt ) · dt / prevDt`.
        float carry = MathF.Exp(MathF.Log(1f - drag) * DragPerSecond * seconds);

        if (particles.PreviousStep > 0f)
        {
            carry *= seconds / particles.PreviousStep;
        }

        for (int index = 0; index < particles.Count; index++)
        {
            Vector3 position = particles.Position[index];
            Vector3 carried = (position - particles.Previous[index]) * carry;

            Vector3 next = position + carried +
                (new Vector3(gravity.X, gravity.Y, gravity.Z) * seconds * seconds);

            particles.Previous[index] = position;
            particles.Position[index] = next;
        }
    }
}

/// <summary>
/// <c>Lifespan Decay</c> — removes a particle once it has outlived its duration.
/// </summary>
/// <remarks>
/// **The one operator whose behaviour its name fully determines**: `LIFE_DURATION` is a published
/// attribute and a decay operator ends the particle when its age reaches it. No parameters are read.
/// </remarks>
public sealed class LifespanDecay : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Lifespan Decay";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);

        _ = parameters;
        _ = seconds;

        particles.Reap();
    }
}

/// <summary>
/// <c>Alpha Fade Out Random</c> — takes alpha to zero over the end of a particle's life.
/// </summary>
/// <remarks>
/// *Interpolated*, and the inference is named rather than hidden: the parameters are
/// `fade_out_time_min` and `fade_out_time_max`, expressed as a FRACTION of the life rather than in
/// seconds — which is why <see cref="ParticleStore.Through"/> exists. A particle fades from
/// full alpha at `1 - fade` to zero at death.
/// </remarks>
public sealed class AlphaFadeOut : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Alpha Fade Out Random";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        // **The default is 1 and not 0**, because a fade operator carrying no time still fades:
        // absent means "over the whole life", and zero would mean "never", which is the sentinel
        // trap this codebase keeps meeting.
        float over = (float)parameters.Number("fade_out_time_max", 1d);

        if (over <= 0f)
        {
            return;
        }

        for (int index = 0; index < particles.Count; index++)
        {
            float through = particles.Through(index);
            float from = 1f - over;

            if (through <= from)
            {
                continue;
            }

            particles.Alpha[index] = Math.Clamp(1f - ((through - from) / over), 0f, 1f);
        }
    }
}

/// <summary>
/// <c>Alpha Fade In Random</c> — brings alpha up from zero over the start of a life.
/// </summary>
/// <remarks>
/// *Interpolated*, the mirror of <see cref="AlphaFadeOut"/>: `fade_in_time_max` as a fraction of
/// the life, alpha rising from zero at birth to full at that point.
/// </remarks>
public sealed class AlphaFadeIn : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Alpha Fade In Random";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        float over = (float)parameters.Number("fade_in_time_max", 0.25d);

        if (over <= 0f)
        {
            return;
        }

        for (int index = 0; index < particles.Count; index++)
        {
            float through = particles.Through(index);

            if (through >= over)
            {
                continue;
            }

            particles.Alpha[index] = Math.Clamp(through / over, 0f, 1f);
        }
    }
}

/// <summary>
/// <c>Radius Scale</c> — scales a particle's radius across its life.
/// </summary>
/// <remarks>
/// *Interpolated*: `radius_start_scale` and `radius_end_scale` interpolate over the life fraction.
/// **Both default to 1**, so an operator declaring neither leaves the radius alone — absent must
/// not mean zero here, or every particle it touches would vanish.
///
/// **It scales the SPAWN radius and not the current one, and that is read from source.**
/// `particles.h:602` declares `GetReadInitialAttributes`, *"Used when an operator needs to read the
/// attributes of a particle at spawn time"* — an operator that reads what it wrote compounds. It did:
/// running the real `rockettrail` definition for twenty steps took a radius to **265** before this
/// was fixed, and the one-step unit test could not see it.
/// </remarks>
public sealed class RadiusScale : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Radius Scale";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        float from = (float)parameters.Number("radius_start_scale", 1d);
        float to = (float)parameters.Number("radius_end_scale", 1d);

        for (int index = 0; index < particles.Count; index++)
        {
            float through = Math.Clamp(particles.Through(index), 0f, 1f);

            particles.Radius[index] =
                particles.RadiusAtBirth[index] * (from + ((to - from) * through));
        }
    }
}

/// <summary>
/// <c>Remap Scalar</c> — one attribute's value drawn as a straight line into another's.
/// </summary>
/// <remarks>
/// **`C_OP_RemapScalar::Operate`, read out of `particles.lib`** (`builtin_particle_ops.obj`, whose symbols are
/// intact), with its fields and defaults from `C_OP_RemapScalar_UnpackInit`:
///
/// <code>
/// input field 7 (ALPHA), input minimum 0, input maximum 1, output field 3 (RADIUS), output minimum 0, output maximum 1
/// if ( 1 &lt;&lt; output field ) &amp; 0x10080:  clamp both output bounds to [0, 1]         // ALPHA, ALPHA2
/// per particle:  target = inMin == inMax ? ( in >= inMax ? outMax : outMin )
///                                        : lerp( outMin, outMax, clamp( ( in − inMin ) / ( inMax − inMin ), 0, 1 ) )
///                out = ( target − out ) · strength + out
/// </code>
///
/// **The float `Operate` is given is the operator's fade STRENGTH, not the step** — and this project's runner does
/// not compute an operator fade, so it is taken as 1, which is what every `operator start/end fade` left at zero
/// gives. **`CREATION_TIME` is relative to the system**, which is <see cref="ParticleStore.Born"/>, so
/// `rocketjump_smoke`'s born-0-to-2-seconds is a radius falling from 11 to 5 along the trail.
///
/// **Only the attributes the store holds are mapped**: input from lifetime, radius, rotation, alpha, creation time
/// and trail length; output to radius, rotation, alpha and trail length. Anything else — `ALPHA2`, which
/// `rocketjump_smoke` writes and whose reader is the closed renderer — is left alone rather than guessed at.
/// </remarks>
public sealed class RemapScalar : IParticleOperator
{
    /// <summary>The attributes this reads and writes, by Valve's number (`particles.h:63`).</summary>
    private const int LifeDuration = 1, Radius = 3, Rotation = 4, Alpha = 7, CreationTime = 8, TrailLength = 10;

    /// <summary>`ALPHA` and `ALPHA2` — the output fields whose bounds are clamped to [0, 1].</summary>
    private const int ClampedOutputs = 0x10080;

    /// <inheritdoc/>
    public string Named => "Remap Scalar";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        int input = (int)parameters.Number("input field", Alpha);
        int output = (int)parameters.Number("output field", Radius);
        float inputMinimum = (float)parameters.Number("input minimum", 0d);
        float inputMaximum = (float)parameters.Number("input maximum", 1d);
        float outputMinimum = (float)parameters.Number("output minimum", 0d);
        float outputMaximum = (float)parameters.Number("output maximum", 1d);

        if (output is < 0 or > 31 || Written(particles, output) is not { } into)
        {
            return;
        }

        if (ClampsOutput(output))
        {
            outputMinimum = Math.Clamp(outputMinimum, 0f, 1f);
            outputMaximum = Math.Clamp(outputMaximum, 0f, 1f);
        }

        for (int index = 0; index < particles.Count; index++)
        {
            if (Read(particles, input, index) is not { } value)
            {
                return;
            }

            into[index] = Target(value, inputMinimum, inputMaximum, outputMinimum, outputMaximum);
        }
    }

    /// <summary>The line from the input bounds to the output bounds, clamped; a step when the input bounds meet.</summary>
    internal static float Target(float value, float inputMinimum, float inputMaximum, float outputMinimum, float outputMaximum)
    {
        // Equal bounds are the engine's own `==`: no division, a step at the bound.
#pragma warning disable S1244 // Floating point numbers should not be tested for equality — the engine's own comparison
        if (inputMinimum == inputMaximum)
#pragma warning restore S1244
        {
            return value >= inputMaximum ? outputMaximum : outputMinimum;
        }

        float along = Math.Clamp((value - inputMinimum) / (inputMaximum - inputMinimum), 0f, 1f);

        return outputMinimum + (along * (outputMaximum - outputMinimum));
    }

    /// <summary>Whether an output field's bounds are clamped to [0, 1] — `ALPHA` and `ALPHA2`.</summary>
    internal static bool ClampsOutput(int field) => field is >= 0 and <= 31 && ((1 << field) & ClampedOutputs) != 0;

    /// <summary>The stream an output field names, or null for one the store does not hold.</summary>
    internal static float[]? Written(ParticleStore particles, int field) => field switch
    {
        LifeDuration => particles.Lifetime,
        Radius => particles.Radius,
        Rotation => particles.Rotation,
        Alpha => particles.Alpha,
        TrailLength => particles.TrailLength,
        _ => null,
    };

    /// <summary>One particle's value of an input field, or null for one the store does not hold.</summary>
    internal static float? Read(ParticleStore particles, int field, int index) => field switch
    {
        CreationTime => particles.Born[index],
        _ => Written(particles, field)?[index],
    };
}

/// <summary>
/// <c>Rotation Spin Roll</c> — turns each particle's card, slowing across a stop window.
/// </summary>
/// <remarks>
/// **`CGeneralSpin::InitParams` and `Operate`, read out of `particles.lib`** (`C_OP_Spin` names attribute 4, `ROTATION`):
///
/// <code>
/// rate = spin_rate_degrees · π/180;  minimum = spin_rate_min · π/180          // InitParams; both declared as ints
/// if rate · strength == 0: nothing
/// drot = dt · |rate · strength · 2π|;  if spin_stop_time == 0: drot = fmod( drot, 2π );  negated when the rate is
/// least = dt · |minimum · 2π|
/// per particle:  factor = max( 0, 1 − ( now − born ) / ( spin_stop_time · life ) )      // 1 when the stop time is 0
///                rot += max( factor · drot, least );  rot −= 2π if ≥ 2π;  rot += 2π if ≤ −2π
/// </code>
///
/// **The rate is converted to radians and then used as revolutions** — multiplied by 2π a second time. That is the
/// engine's arithmetic and it is reproduced: 36° a second turns a card 3.95 radians a second. Strength is 1, as for
/// <see cref="RemapScalar"/>.
/// </remarks>
public sealed class RotationSpinRoll : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Rotation Spin Roll";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        float rate = (int)parameters.Number("spin_rate_degrees", 0d) * (MathF.PI / 180f);
        float minimum = (int)parameters.Number("spin_rate_min", 0d) * (MathF.PI / 180f);
        float stop = (float)parameters.Number("spin_stop_time", 0d);

        if (rate == 0f)
        {
            return;
        }

        float turn = seconds * MathF.Abs(rate * MathF.Tau);

        if (stop == 0f)
        {
            turn %= MathF.Tau;
        }

        if (rate < 0f)
        {
            turn = -turn;
        }

        float least = seconds * MathF.Abs(minimum * MathF.Tau);

        for (int index = 0; index < particles.Count; index++)
        {
            float scale = stop == 0f ? 0f : 1f / (stop * particles.Lifetime[index]);
            float factor = MathF.Max(0f, 1f - ((particles.Age - particles.Born[index]) * scale));
            float rotation = particles.Rotation[index] + MathF.Max(factor * turn, least);

            if (rotation >= MathF.Tau)
            {
                rotation -= MathF.Tau;
            }
            else if (rotation <= -MathF.Tau)
            {
                rotation += MathF.Tau;
            }

            particles.Rotation[index] = rotation;
        }
    }
}

/// <summary>
/// <c>Movement Lock to Control Point</c> — carries particles along as their control point moves.
/// </summary>
/// <remarks>
/// **`C_OP_PositionLock::Operate`, read out of `particles.lib`**, with its fields and defaults from its unpack table:
///
/// <code>
/// control_point_number 0, start_fadeout_min/max 1, end_fadeout_min/max 1, both exponents 1, distance fade range 0,
/// lock rotation 0
/// first call:  if the stored previous point is the origin, store this one (so the first delta is zero)
/// delta = ( cp − prev ) · strength;   share = min( now − born, dt ) / dt          // a particle born mid-step
/// if start_fadeout_min &lt; 1:  per particle, per FRAME:
///     start = pow( rand, exp·4 fixed / 4 ) · ( start_max − start_min ) + start_min;   end likewise
///     weight = 1 − clamp( ( clamp( age / life ) − start ) / ( end − start ) )      // else weight = strength
/// if distance fade range:  d = min( 1, |pos + delta·share − cp| / range );  f = d / ( ( 1 − d ) · 3 + 1 )   // Bias 0.2
///                          delta ·= 1 − f;  weight = 1 − f · weight
/// lock rotation:  pos and prev lerp toward ( cur · prev⁻¹ ) · p by weight · share
/// otherwise:      pos += delta · share · weight-of-the-window;  prev likewise
/// then store cp as the previous point
/// </code>
///
/// **Stateful, so it is not an <see cref="IParticleOperator"/>**: the engine keeps the previous point in the operator's
/// per-collection context, and <see cref="ParticleEffect"/> holds one of these per declared function. **The per-frame
/// draw is keyed, not a stream**: `RandSIMD` draws afresh every frame, which this reproduces by keying the table on the
/// particle and the store's step count, so a replay gives the same picture. `MatrixInvert` of a 3×4 is its transpose,
/// as Valve's own assumes orthonormal.
/// </remarks>
public sealed class MovementLock
{
    /// <summary>The <c>functionName</c> a <c>.pcf</c> uses.</summary>
    public const string Named = "Movement Lock to Control Point";

    /// <summary>`PreCalcBiasParameter( 0.2 )`: `1 / 0.2 − 2`.</summary>
    private const float BiasOfAFifth = 3f;

    /// <summary>Which of <see cref="ParticleRandom"/>'s channels the window's two draws take, before the step is added.</summary>
    private const int StartDraw = 131, EndDraw = 132;

    /// <summary>The point last seen — the context's `m_vPrevPosition` and `m_matPrevTransform`.</summary>
    private ParticleControlPoint _previous;

    /// <summary>Carries every particle along with the control point's move since the last call.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="parameters">The declared function.</param>
    /// <param name="seconds">The collection's step, `m_flDt`.</param>
    /// <param name="point">The control point it names, where it is now.</param>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds, ParticleControlPoint point)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        if (_previous.At == Vector3.Zero)
        {
            _previous = point;
        }

        float startLeast = (float)parameters.Number("start_fadeout_min", 1d);
        float startMost = (float)parameters.Number("start_fadeout_max", 1d);
        int startExponent = (int)((float)parameters.Number("start_fadeout_exponent", 1d) * 4f);
        float endLeast = (float)parameters.Number("end_fadeout_min", 1d);
        float endMost = (float)parameters.Number("end_fadeout_max", 1d);
        int endExponent = (int)((float)parameters.Number("end_fadeout_exponent", 1d) * 4f);
        float range = (float)parameters.Number("distance fade range", 0d);
        bool rotate = parameters.Number("lock rotation", 0d) != 0d;

        Vector3 delta = point.At - _previous.At;
        bool window = startLeast < 1f;

        for (int index = 0; index < particles.Count; index++)
        {
            float share = MathF.Min(particles.Age - particles.Born[index], seconds) / seconds;
            float weight = 1f;

            if (window)
            {
                float life = Math.Clamp((particles.Age - particles.Born[index]) / particles.Lifetime[index], 0f, 1f);
                int id = particles.Id[index] + (particles.Steps * 2);
                float start = (Power(ParticleRandom.Sample(id, StartDraw), startExponent) * (startMost - startLeast)) + startLeast;
                float end = (Power(ParticleRandom.Sample(id, EndDraw), endExponent) * (endMost - endLeast)) + endLeast;

                weight = 1f - Math.Clamp((life - start) / (end - start), 0f, 1f);

                if (weight <= 0f)
                {
                    continue;
                }
            }

            Vector3 moved = delta * share * weight;

            if (range != 0f)
            {
                float away = MathF.Min(1f, (particles.Position[index] + moved - point.At).Length() / range);
                float faded = away / (((1f - away) * BiasOfAFifth) + 1f);

                moved *= 1f - faded;
                weight = 1f - (faded * weight);
            }

            if (rotate)
            {
                particles.Position[index] = Toward(particles.Position[index], point, weight * share);
                particles.Previous[index] = Toward(particles.Previous[index], point, weight * share);
            }
            else
            {
                particles.Position[index] += moved;
                particles.Previous[index] += moved;
            }
        }

        _previous = point;
    }

    /// <summary>`Pow_FixedPoint_Exponent_SIMD`: <paramref name="value"/> to the power of a quarter-unit exponent.</summary>
    private static float Power(float value, int quarters) => MathF.Pow(value, quarters / 4f);

    /// <summary>A point lerped by <paramref name="weight"/> toward where `cur · prev⁻¹` puts it.</summary>
    private Vector3 Toward(Vector3 at, ParticleControlPoint now, float weight)
    {
        // A Source matrix's columns are forward, LEFT and up; its inverse is the transpose.
        Vector3 local = at - _previous.At;
        Vector3 carried = now.At +
                          (now.Forward * Vector3.Dot(local, _previous.Forward)) +
                          (-now.Right * Vector3.Dot(local, -_previous.Right)) +
                          (now.Up * Vector3.Dot(local, _previous.Up));

        return at + ((carried - at) * weight);
    }
}

/// <summary>
/// <c>Alpha Fade and Decay</c> — the fade a rocket trail actually uses.
/// </summary>
/// <remarks>
/// **Implemented because the real effect asks for it, which the frequency ranking did not say.**
/// `rockettrail` names this and `Color Fade`, not `Alpha Fade Out Random` — ranking by what TF2
/// ships is not the same as what ONE effect needs, and running the definition end to end is what
/// showed the difference (B373).
///
/// **`C_OP_FadeAndKill::Operate`, read out of `client.dll`** (`FUN_107aeb00`, B415) — which REPLACED a reading this
/// class carried from B373 and flagged *interpolated*: that alpha rose from zero to `start_alpha`, held there, and fell
/// linearly. The engine does something else on every one of those points:
///
/// <code>
/// life = age · rcp( lifetime )                                   // rcpps, no refinement
/// if ( in_start  ≤ life &lt; in_end  )  alpha = S(t)·(initial − initial·start_alpha) + initial·start_alpha
/// if ( out_start ≤ life &lt; out_end )  alpha = S(t)·(initial·end_alpha − initial) + initial
/// S(t) = 3t² − 2t³,  t clamped to [0, 1]                        // 3.0 at 0x10b51fa0, 2.0 at 0x10b51f90
/// outside both windows, alpha is NOT WRITTEN
/// </code>
///
/// **So `start_alpha` is where the fade-in BEGINS, as a fraction of the particle's own alpha, not a level it holds.**
/// It was invisible in the rocket trail — `start_alpha 1` makes the fade-in a constant, which the old reading drew as a
/// ramp up from zero at the rocket that TF2 does not have — and fatal in an explosion: `Explosion_CoreFlash` declares
/// `start_alpha 0` with an EMPTY fade-in window (<c>0 ≤ life &lt; 0</c> is never true), so it keeps its initializer's
/// alpha until 70% of its life. The old reading held it at zero for all of it, and the heart of every blast drew
/// nothing — found by counting each child's live particles when a picture of an explosion showed one streak.
///
/// **The fraction and the window widths use `rcpps`**, the approximate reciprocal, with no Newton step; reproduced
/// with <c>Sse.ReciprocalScalar</c> so the smoothstep lands on the same float.
///
/// **"and Decay" is the second half of the name and it is not decoration** — this operator also ends the particle
/// (the engine appends <c>lifetime ≤ age</c> lanes to the collection's kill list), which is why an effect carrying it
/// needs no separate `Lifespan Decay`.
/// </remarks>
public sealed class AlphaFadeAndDecay : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Alpha Fade and Decay";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        // The unpack table's defaults, read from `client.dll`: `start_alpha` "1", `end_alpha` "0",
        // `start_fade_in_time` "0", `end_fade_in_time` "0.5", `start_fade_out_time` "0.5", `end_fade_out_time` "1".
        float startAlpha = (float)parameters.Number("start_alpha", 1d);
        float endAlpha = (float)parameters.Number("end_alpha", 0d);

        float inFrom = (float)parameters.Number("start_fade_in_time", 0d);
        float inTo = (float)parameters.Number("end_fade_in_time", 0.5d);
        float outFrom = (float)parameters.Number("start_fade_out_time", 0.5d);
        float outTo = (float)parameters.Number("end_fade_out_time", 1d);

        float acrossIn = Reciprocal(inTo - inFrom);
        float acrossOut = Reciprocal(outTo - outFrom);

        for (int index = 0; index < particles.Count; index++)
        {
            float age = particles.AgeOf(index);
            float lifetime = particles.Lifetime[index];

            if (lifetime <= age)
            {
                // A dying lane is masked out of both writes and goes on the kill list; `Reap` below is that list.
                continue;
            }

            float life = Reciprocal(lifetime) * age;

            // **The particle's OWN spawn alpha is what both fades are fractions of** — the operator reads the INITIAL
            // alpha stream (`GetReadInitialAttributes`, `particles.h:602`), which is why `Alpha Random` survives it.
            float born = particles.AlphaAtBirth[index];

            if (inFrom <= life && life < inTo)
            {
                float from = born * startAlpha;

                particles.Alpha[index] = (Smooth((life - inFrom) * acrossIn) * (born - from)) + from;
            }

            if (outFrom <= life && life < outTo)
            {
                particles.Alpha[index] = (Smooth((life - outFrom) * acrossOut) * ((born * endAlpha) - born)) + born;
            }
        }

        particles.Reap();
    }

    /// <summary><c>rcpps</c> — the approximate reciprocal the engine uses here, with no refinement step.</summary>
    internal static float Reciprocal(float value) =>
        Sse.IsSupported
            ? Sse.ReciprocalScalar(Vector128.CreateScalar(value)).ToScalar()
            : 1f / value;

    /// <summary>The engine's smoothstep, <c>3t² − 2t³</c>, on <c>t</c> clamped to [0, 1] — in its own operand order.</summary>
    private static float Smooth(float raw)
    {
        float t = MathF.Min(1f, MathF.Max(0f, raw));

        return (t * t * 3f) - (t * 2f * t * t);
    }

    /// <summary>Where a value sits between two bounds, clamped — 0 before, 1 after.</summary>
    /// <remarks>
    /// **A zero-width window is 1 rather than a division by zero**, which is the case a definition
    /// carrying `start_fade_in_time` equal to `end_fade_in_time` produces — and it means "already
    /// finished", not "never".
    /// </remarks>
    internal static float Ramp(float at, float from, float to) =>
        to - from <= float.Epsilon ? 1f : Math.Clamp((at - from) / (to - from), 0f, 1f);
}

/// <summary>
/// <c>Color Fade</c> — takes a particle's tint toward a declared colour across its life.
/// </summary>
/// <remarks>
/// **The other operator the real rocket trail names.** Its parameters, from the shipped definition:
/// `color_fade` is the target, `fade_start_time` and `fade_end_time` bound the window as life
/// fractions, and `ease_in_and_out` selects a smoothstep rather than a straight line.
///
/// **The FROM colour is the spawn tint, not the current one** — the same reason `Radius Scale`
/// reads a spawn radius (`GetReadInitialAttributes`, `particles.h:602`). Reading the current tint
/// would make every step a fresh interpolation from wherever the last one landed, which converges
/// on the target far too quickly and cannot be seen in a single-step test.
/// </remarks>
public sealed class ColourFade : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Color Fade";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        _ = seconds;

        Vector4 target = parameters.Vector("color_fade", default);
        float from = (float)parameters.Number("fade_start_time", 0d);
        float to = (float)parameters.Number("fade_end_time", 1d);
        bool eased = parameters.Number("ease_in_and_out", 0d) != 0d;

        Vector3 fade = new(target.X, target.Y, target.Z);

        for (int index = 0; index < particles.Count; index++)
        {
            float at = AlphaFadeAndDecay.Ramp(particles.Through(index), from, to);

            // Smoothstep, which is what "ease in and out" means: 3t² − 2t³.
            float mixed = eased ? at * at * (3f - (2f * at)) : at;

            particles.Tint[index] = Vector3.Lerp(particles.TintAtBirth[index], fade, mixed);
        }
    }
}

/// <summary><c>Oscillate Scalar</c> — nudges one attribute along a wave, a step at a time (B415).</summary>
/// <remarks>
/// **`C_OP_OscillateScalar::Operate`, read out of `client.dll`** (`FUN_107a5220`), with the members its unpack table
/// names and the defaults it gives them — the trace is in `docs/findings/58`. For a particle whose lifetime is above
/// zero and whose time lies in <c>[start, end)</c>:
///
/// <code>
/// t     = start/end proportional ? rcp( lifetime ) · age : age
/// start = r₁₁ · ( start max − start min ) + start min        end likewise with r₁₂
/// freq  = r₀ · ( freq max − freq min ) + freq min            rate likewise with r₁
/// arg   = proportional ? rcp( lifetime ) · age · freq · multiplier + phase
///                      : freq · ( multiplier · curtime + phase )
/// field = SinEst01( arg ) · ( rate · dt ) + field            ALPHA alone clamped to [0, 1]
/// </code>
///
/// **`SinEst01SIMD` is a parabola, not a sine** (`ssemath.h:3129`): <c>x(4 − 4x)</c> on each half of a period of 2,
/// with the sign put back from the half and the argument's own sign. The constants are the SDK's, checked in the binary.
///
/// **Not reproduced:** the operator's strength (its fade-in and fade-out as an OPERATOR), which is 1 for every effect
/// this project draws; and fields this store does not hold, which are left alone.
/// </remarks>
public sealed class OscillateScalar : IParticleOperator
{
    /// <summary>Which table entries the per-particle draws read — the engine's own offsets from the particle's id.</summary>
    private const int FrequencyDraw = 0;

    /// <summary>The rate's draw.</summary>
    private const int RateDraw = 1;

    /// <summary>The start time's draw.</summary>
    private const int StartDraw = 11;

    /// <summary>The end time's draw.</summary>
    private const int EndDraw = 12;

    /// <summary>Where this operator's draws sit in <see cref="ParticleRandom"/>, clear of every initializer's.</summary>
    private const int Draws = 3840;

    /// <summary>The attribute index of <c>ALPHA</c>, the one field clamped.</summary>
    private const int AlphaField = 7;

    /// <inheritdoc/>
    public string Named => "Oscillate Scalar";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        int named = (int)parameters.Number("oscillation field", AlphaField);

        float[]? field = named switch
        {
            1 => particles.Lifetime,
            3 => particles.Radius,
            4 => particles.Rotation,
            AlphaField => particles.Alpha,
            8 => particles.Born,
            10 => particles.TrailLength,
            _ => null,
        };

        if (field is null)
        {
            return;
        }

        float rateLeast = (float)parameters.Number("oscillation rate min", 0d);
        float rateWidth = (float)parameters.Number("oscillation rate max", 0d) - rateLeast;
        float frequencyLeast = (float)parameters.Number("oscillation frequency min", 1d);
        float frequencyWidth = (float)parameters.Number("oscillation frequency max", 1d) - frequencyLeast;
        float startLeast = (float)parameters.Number("start time min", 0d);
        float startWidth = (float)parameters.Number("start time max", 0d) - startLeast;
        float endLeast = (float)parameters.Number("end time min", 1d);
        float endWidth = (float)parameters.Number("end time max", 1d) - endLeast;
        float multiplier = (float)parameters.Number("oscillation multiplier", 2d);
        float phase = (float)parameters.Number("oscillation start phase", 0.5d);
        bool proportional = parameters.Number("proportional 0/1", 1d) != 0d;
        bool windowProportional = parameters.Number("start/end proportional", 1d) != 0d;

        // Both hoisted out of the loop in the engine, in these operand orders.
        float clock = (multiplier * particles.Age) + phase;

        for (int index = 0; index < particles.Count; index++)
        {
            float lifetime = particles.Lifetime[index];
            float age = particles.AgeOf(index);
            int id = particles.Id[index];

            float t = windowProportional ? AlphaFadeAndDecay.Reciprocal(lifetime) * age : age;

            float start = (ParticleRandom.Sample(id, Draws + StartDraw) * startWidth) + startLeast;
            float end = (ParticleRandom.Sample(id, Draws + EndDraw) * endWidth) + endLeast;

            if (!(lifetime > 0f && start <= t && t < end))
            {
                continue;
            }

            float frequency = (ParticleRandom.Sample(id, Draws + FrequencyDraw) * frequencyWidth) + frequencyLeast;
            float rate = (ParticleRandom.Sample(id, Draws + RateDraw) * rateWidth) + rateLeast;

            float argument = proportional
                ? (AlphaFadeAndDecay.Reciprocal(lifetime) * age * frequency * multiplier) + phase
                : frequency * clock;

            float value = (SinEst01(argument) * (rate * seconds)) + field[index];

            field[index] = named == AlphaField ? MathF.Max(MathF.Min(value, 1f), 0f) : value;
        }
    }

    /// <summary><c>SinEst01SIMD</c> — Valve's parabolic sine on a period of 2, one lane of it.</summary>
    /// <remarks>
    /// <c>Mod2SIMDPositiveInput</c> finds the even integer at or below <c>|x|</c> by adding 2²³, clearing the lowest
    /// bit and subtracting 2²³ again (so the add rounds to an integer and the mask makes it even), stepping back by two
    /// when that rounded up.
    /// </remarks>
    private static float SinEst01(float x)
    {
        const float TwoToThe23 = 8388608f;

        float magnitude = MathF.Abs(x);

        float even = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(magnitude + TwoToThe23) & ~1)
            - TwoToThe23;

        if (even > magnitude)
        {
            even -= 2f;
        }

        float reduced = magnitude - even;
        bool odd = reduced >= 1f;
        float within = odd ? reduced - 1f : reduced;

        float estimate = within * (4f - (within * 4f));

        return (x < 0f) != odd ? -estimate : estimate;
    }
}
