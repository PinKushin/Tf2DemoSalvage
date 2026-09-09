using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One running instance of a particle system — emit, operate, reap (B373).
/// </summary>
/// <remarks>
/// **This is the order the engine runs and the order matters.** A particle born this step must be
/// operated on this step or it appears at its spawn state for one frame, and reaping last means a
/// particle that died this step is not drawn. `CParticleCollection::Simulate` is the shape being
/// followed; the operators themselves are `ParticleOperators`.
///
/// **The emitter is `emit_continuously` and its rate is a FRACTION per step**, which is why the
/// remainder is carried. At the shipped `emission_rate 128` and a 66-tick clock that is 1.94
/// particles a step — truncating each step would emit one and lose a third of the trail, and
/// rounding would emit two and inflate it.
///
/// <code>
/// emit_continuously   emission_rate 128   emission_start_time 0   emission_duration 0
/// </code>
///
/// **`emission_duration` of zero means FOREVER**, not "never" — the one place in this file where a
/// zero is a sentinel rather than a quantity, and reading it the other way emits nothing at all
/// (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
/// </remarks>
public sealed class ParticleEffect
{
    /// <summary>The definition this is an instance of.</summary>
    public ParticleSystem System { get; }

    /// <summary>Its live particles.</summary>
    public ParticleStore Particles { get; } = new();

    /// <summary>The operators this run can apply, by name.</summary>
    private readonly IReadOnlyDictionary<string, IParticleOperator> _operators;

    /// <summary>The fraction of a particle owed from previous steps.</summary>
    private float _owed;

    /// <summary>Starts an instance of one system.</summary>
    /// <param name="system">The definition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    public ParticleEffect(ParticleSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        System = system;
        _operators = ParticleOperators.All();
    }

    /// <summary>How many of this system's operators this project implements.</summary>
    /// <returns>The count, against <see cref="ParticleSystem.Operators"/>.</returns>
    /// <remarks>
    /// **Reported rather than silently skipped**, because a missing operator is invisible in the
    /// result: the effect still draws, just wrongly. `rockettrail` is 4 of 4; a system reaching for
    /// something unimplemented should be able to say so.
    /// </remarks>
    public int Implemented()
    {
        int known = 0;

        foreach (ParticleFunction one in System.Operators)
        {
            if (_operators.ContainsKey(one.Function))
            {
                known++;
            }
        }

        return known;
    }

    /// <summary>Advances the effect one step, emitting at the declared rate.</summary>
    /// <param name="at">Where the emitter is — the rocket's own position.</param>
    /// <param name="seconds">How long the step is.</param>
    /// <remarks>
    /// **Emit, operate, reap.** A particle born this step is operated on this step, so it never
    /// appears at its raw spawn state; reaping last means one that died this step is gone before
    /// anything draws it.
    /// </remarks>
    public void Step(Vector3 at, float seconds)
    {
        Particles.Tick(seconds);

        Emit(at, seconds);

        foreach (ParticleFunction one in System.Operators)
        {
            if (_operators.TryGetValue(one.Function, out IParticleOperator? run))
            {
                run.Operate(Particles, one, seconds);
            }
        }

        Particles.Reap();
    }

    /// <summary>Advances without emitting, for an effect whose emitter is gone.</summary>
    /// <param name="seconds">How long the step is.</param>
    /// <remarks>
    /// **A rocket explodes and its trail hangs in the air**, fading on the particles' own schedule
    /// rather than vanishing with the blast. So the effect outlives the entity, and this is what it
    /// does in the meantime: operate and reap, emit nothing.
    ///
    /// **Not `Step` with the last position**, which is the bug this replaced — that keeps emitting
    /// at wherever the trail happens to be and grows it for ever.
    /// </remarks>
    public void Fade(float seconds)
    {
        Particles.Tick(seconds);

        foreach (ParticleFunction one in System.Operators)
        {
            if (_operators.TryGetValue(one.Function, out IParticleOperator? run))
            {
                run.Operate(Particles, one, seconds);
            }
        }

        Particles.Reap();
    }

    /// <summary>Emits this step's share of particles, carrying the remainder.</summary>
    private void Emit(Vector3 at, float seconds)
    {
        foreach (ParticleFunction emitter in System.Emitters)
        {
            if (!string.Equals(emitter.Function, "emit_continuously", StringComparison.Ordinal))
            {
                continue;
            }

            float rate = (float)emitter.Number("emission_rate", 0d);
            float duration = (float)emitter.Number("emission_duration", 0d);
            float from = (float)emitter.Number("emission_start_time", 0d);

            // Zero duration is FOREVER, which is the sentinel this class's remarks name.
            if (Particles.Age < from || (duration > 0f && Particles.Age > from + duration))
            {
                continue;
            }

            _owed += rate * seconds;

            while (_owed >= 1f)
            {
                _owed -= 1f;

                if (ParticleSystems.Spawn(System, Particles, at, Lifetime()) < 0)
                {
                    // At `max_particles`. Dropping the owed fraction too, because a system at its
                    // cap has not banked a debt — it simply did not emit.
                    _owed = 0f;
                    return;
                }
            }
        }
    }

    /// <summary>How long a new particle lives, from the system's own initializer.</summary>
    /// <remarks>
    /// **`Lifetime Random` is an INITIALIZER rather than an operator**, so it runs once at birth and
    /// is read here rather than every step. `rockettrail` declares `lifetime_min` and
    /// `lifetime_max` both 0.2, which with `emission_rate 128` is the ~26 particles a steady trail
    /// carries.
    ///
    /// *Interpolated:* that the two bound a uniform draw. They are equal on this effect, so the
    /// distribution does not matter here and the midpoint is used rather than a random number —
    /// which also keeps a replay of the same demo identical, as
    /// `docs/DECISIONS.md` D136 asks of anything that would otherwise need a seed.
    /// </remarks>
    private float Lifetime()
    {
        foreach (ParticleFunction one in System.Initializers)
        {
            if (!string.Equals(one.Function, "Lifetime Random", StringComparison.Ordinal))
            {
                continue;
            }

            float least = (float)one.Number("lifetime_min", 1d);
            float most = (float)one.Number("lifetime_max", least);

            return (least + most) / 2f;
        }

        return 1f;
    }
}
