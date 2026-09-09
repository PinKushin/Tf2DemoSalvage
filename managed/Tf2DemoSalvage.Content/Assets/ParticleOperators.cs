using System;
using System.Collections.Generic;
using System.Numerics;

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
/// this reads. *Interpolated:* that drag scales the inherited step linearly.
/// </remarks>
public sealed class MovementBasic : IParticleOperator
{
    /// <inheritdoc/>
    public string Named => "Movement Basic";

    /// <inheritdoc/>
    public void Operate(ParticleStore particles, ParticleFunction parameters, float seconds)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);

        Vector4 gravity = parameters.Vector("gravity", default);
        float drag = (float)parameters.Number("drag", 0d);

        for (int index = 0; index < particles.Count; index++)
        {
            Vector3 position = particles.Position[index];
            Vector3 carried = (position - particles.Previous[index]) * (1f - drag);

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
/// <c>Alpha Fade and Decay</c> — the fade a rocket trail actually uses.
/// </summary>
/// <remarks>
/// **Implemented because the real effect asks for it, which the frequency ranking did not say.**
/// `rockettrail` names this and `Color Fade`, not `Alpha Fade Out Random` — ranking by what TF2
/// ships is not the same as what ONE effect needs, and running the definition end to end is what
/// showed the difference (B373).
///
/// **Its four times are all life FRACTIONS**, read from the shipped definition:
///
/// <code>
/// start_alpha 1   start_fade_in_time  0     end_fade_in_time  0.1
/// end_alpha   0   start_fade_out_time 0.1   end_fade_out_time 1
/// </code>
///
/// So alpha rises from zero to `start_alpha` across the fade-in window, holds, then falls to
/// `end_alpha` across the fade-out window. *Interpolated:* that both ramps are linear, and that the
/// windows are expressed against the life fraction rather than in seconds.
///
/// **"and Decay" is the second half of the name and it is not decoration** — this operator also
/// ends the particle, which is why an effect carrying it needs no separate `Lifespan Decay`.
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

        float startAlpha = (float)parameters.Number("start_alpha", 1d);
        float endAlpha = (float)parameters.Number("end_alpha", 0d);

        float inFrom = (float)parameters.Number("start_fade_in_time", 0d);
        float inTo = (float)parameters.Number("end_fade_in_time", 0d);
        float outFrom = (float)parameters.Number("start_fade_out_time", 1d);
        float outTo = (float)parameters.Number("end_fade_out_time", 1d);

        for (int index = 0; index < particles.Count; index++)
        {
            float through = particles.Through(index);

            // **The particle's OWN spawn alpha is what these scale**, which is the same rule
            // `RadiusAtBirth` records for radius and `GetReadInitialAttributes` (`particles.h:602`)
            // states in general. The file settles it for this operator: `rockettrail` declares
            // `Alpha Random` 96..128 of 255 AND `start_alpha 1`, so reading `start_alpha` as an
            // absolute means the operator overwrites the initializer on the first frame and
            // `Alpha Random` can never do anything — on this effect or any other that pairs them.
            float born = particles.AlphaAtBirth[index];

            if (through < inTo)
            {
                // Rising into the particle's own alpha.
                particles.Alpha[index] = born * startAlpha * Ramp(through, inFrom, inTo);
            }
            else if (through <= outFrom)
            {
                // Held between the two windows.
                particles.Alpha[index] = born * startAlpha;
            }
            else
            {
                particles.Alpha[index] = born *
                    (startAlpha + ((endAlpha - startAlpha) * Ramp(through, outFrom, outTo)));
            }
        }

        particles.Reap();
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
