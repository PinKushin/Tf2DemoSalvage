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
    /// <summary>How long a particle lives when its system declares no <c>Lifetime Random</c>.</summary>
    /// <remarks>
    /// **A fallback and not the answer.** `ParticleSystems.Spawn` runs the initializer where one
    /// exists and overwrites this; a system with none has nothing else to say, and one second is the
    /// same number the code that read the initializer here used to return.
    /// </remarks>
    private const float DefaultLifetime = 1f;

    /// <summary>The definition this is an instance of.</summary>
    public ParticleSystem System { get; }

    /// <summary>Its live particles.</summary>
    public ParticleStore Particles { get; } = new();

    /// <summary>The operators this run can apply, by name.</summary>
    private readonly IReadOnlyDictionary<string, IParticleOperator> _operators;

    /// <summary>The fraction of a particle owed from previous steps.</summary>
    private float _owed;

    /// <summary>Whether this effect and every child of it has run out of particles.</summary>
    /// <remarks>
    /// **A parent is not finished while a child still has particles**, which the engine states in
    /// as many words — *"make sure all children are finished"* (`particles.h:1630`). Dropping an
    /// effect on its own emptiness would cut a rocket's fire off the moment its smoke ran out.
    /// </remarks>
    public bool Empty
    {
        get
        {
            if (Particles.Count > 0)
            {
                return false;
            }

            foreach (ParticleEffect child in Children)
            {
                if (!child.Empty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The systems this one runs alongside itself.</summary>
    /// <remarks>
    /// **A child is a full collection, not a decoration.** `rockettrail` declares two —
    /// `rockettrail_burst` and `rockettrail_fire` — each with its own emitters, operators and
    /// MATERIAL, which is why drawing them needs a draw per material rather than one more batch.
    /// Without them a rocket has smoke and no glow.
    /// </remarks>
    public IReadOnlyList<ParticleEffect> Children { get; }

    /// <summary>Starts an instance of one system.</summary>
    /// <param name="system">The definition.</param>
    /// <param name="others">
    /// Every system that could be a child, by name, or null for an instance with none. A child is
    /// referred to BY NAME and may live in another file, so the caller resolves rather than this.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    /// <remarks>
    /// **A child that cannot be resolved is skipped rather than throwing**, because a `.pcf` names
    /// children across files and this project reads one. Skipping loses a glow; refusing loses the
    /// trail as well.
    ///
    /// **A system is never its own child**, and the guard is not paranoia: a cycle here would
    /// recurse until the stack ran out, at load, on a file this project does not control.
    /// </remarks>
    public ParticleEffect(
        ParticleSystem system, IReadOnlyDictionary<string, ParticleSystem>? others = null)
    {
        ArgumentNullException.ThrowIfNull(system);

        System = system;
        _operators = ParticleOperators.All();

        List<ParticleEffect> children = [];

        foreach (string named in system.Children)
        {
            if (others is not null &&
                !string.Equals(named, system.Name, StringComparison.OrdinalIgnoreCase) &&
                others.TryGetValue(named, out ParticleSystem? child))
            {
                children.Add(new ParticleEffect(child, Without(others, system.Name)));
            }
        }

        Children = children;
    }

    /// <summary>The same lookup with one name removed, so a cycle cannot recurse for ever.</summary>
    private static Dictionary<string, ParticleSystem> Without(
        IReadOnlyDictionary<string, ParticleSystem> systems, string named)
    {
        Dictionary<string, ParticleSystem> rest = new(systems, StringComparer.OrdinalIgnoreCase);

        rest.Remove(named);

        return rest;
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
    public void Step(ParticleControlPoint at, float seconds)
    {
        // **A child gets the parent's control point, position AND orientation.** Read from source:
        // `SetControlPoint` and `SetControlPointOrientation` each walk `m_Children` and pass the
        // same values down (`particles.h:1595`, `:1629`). So a rocket's fire and burst follow the
        // rocket exactly as its smoke does, rather than being placed once where it spawned.
        foreach (ParticleEffect child in Children)
        {
            child.Step(at, seconds);
        }

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
        foreach (ParticleEffect child in Children)
        {
            child.Fade(seconds);
        }

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
    private void Emit(ParticleControlPoint at, float seconds)
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

                if (ParticleSystems.Spawn(System, Particles, at, DefaultLifetime, seconds) < 0)
                {
                    // At `max_particles`. Dropping the owed fraction too, because a system at its
                    // cap has not banked a debt — it simply did not emit.
                    _owed = 0f;
                    return;
                }
            }
        }
    }

}
