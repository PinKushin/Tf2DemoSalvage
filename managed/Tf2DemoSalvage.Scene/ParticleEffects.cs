using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The particle effects a scene is running, one per live projectile (B373).
/// </summary>
/// <remarks>
/// **A rocket's trail follows the rocket**, so an effect is owned per ENTITY and stepped with it —
/// `ParticleProp()->Create( "rockettrail", PATTACH_POINT_FOLLOW, "trail" )`
/// (`c_tf_projectile_rocket.cpp:67`) attaches to the projectile and moves with it, rather than
/// being a one-shot at the spawn point.
///
/// **An effect outlives its projectile by design.** A rocket explodes and its trail hangs in the
/// air for the particles' remaining lifetime; removing the effect with the entity would cut the
/// trail off at the blast, which is the opposite of what an explosion looks like. So an effect
/// whose entity is gone stops EMITTING and keeps stepping until it is empty.
///
/// **Which system a projectile uses is the engine's own choice** and is read from
/// `CreateTrails`: `rockettrail` normally, `rockettrail_underwater` when the origin is in
/// `MASK_WATER`, `rockettrail_airstrike` for a launcher with the `mini_rockets` attribute. Only the
/// first is resolved here — the other two need the water volume and the launcher's attributes, and
/// picking one without them would be a guess wearing a citation.
/// </remarks>
public sealed class ParticleEffects
{
    /// <summary>The class whose trail this draws.</summary>
    /// <remarks>
    /// **Only the rocket for now, and stated rather than implied.** Pipebombs, arrows and flares
    /// each name their own system, and adding one is adding a row here once its definition has been
    /// read — not a change to this class.
    /// </remarks>
    public const string RocketClass = "CTFProjectile_Rocket";

    /// <summary>The system a rocket's trail uses.</summary>
    public const string RocketTrail = "rockettrail";

    /// <summary>The live effects, by the entity they follow.</summary>
    private readonly Dictionary<int, ParticleEffect> _running = [];

    /// <summary>The entities that still exist this tick, reused to avoid allocating per frame.</summary>
    private readonly HashSet<int> _alive = [];

    /// <summary>How many effects are running.</summary>
    public int Count => _running.Count;

    /// <summary>Steps every effect, starting one for each projectile that has none.</summary>
    /// <param name="projectiles">The live projectiles this tick, with their positions.</param>
    /// <param name="definition">The system to run, or null when it could not be read.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="projectiles"/> is null.</exception>
    /// <remarks>
    /// **Stepped on the timeline's own interval**, which is the same clock `RagdollProps` receives —
    /// a trail that advanced on wall-clock time would stretch when the viewer stutters and compress
    /// when it scrubs.
    /// </remarks>
    public void Update(
        IReadOnlyList<(int Entity, Vector3 At)> projectiles,
        ParticleSystem? definition,
        float seconds)
    {
        ArgumentNullException.ThrowIfNull(projectiles);

        if (definition is null)
        {
            return;
        }

        _alive.Clear();

        foreach ((int entity, Vector3 at) in projectiles)
        {
            _alive.Add(entity);

            if (!_running.TryGetValue(entity, out ParticleEffect? effect))
            {
                effect = new ParticleEffect(definition);
                _running[entity] = effect;
            }

            effect.Step(at, seconds);
        }

        // **An effect whose rocket is gone keeps stepping without emitting**, so the trail it
        // already laid fades on its own schedule rather than vanishing with the explosion.
        List<int>? finished = null;

        foreach ((int entity, ParticleEffect effect) in _running)
        {
            if (_alive.Contains(entity))
            {
                continue;
            }

            effect.Fade(seconds);

            if (effect.Particles.Count == 0)
            {
                (finished ??= []).Add(entity);
            }
        }

        foreach (int entity in finished ?? [])
        {
            _running.Remove(entity);
        }
    }

    /// <summary>Builds every live effect's quads for this frame's camera.</summary>
    /// <param name="right">The camera's right vector.</param>
    /// <param name="up">The camera's up vector.</param>
    /// <param name="into">Where corners are appended; NOT cleared.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void Build(Vector3 right, Vector3 up, ICollection<DetailSpriteVertex> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (ParticleEffect effect in _running.Values)
        {
            ParticleSprites.Build(effect.Particles, right, up, into);
        }
    }

    /// <summary>Forgets every effect, for a demo change.</summary>
    /// <remarks>
    /// **A new demo must not inherit the last one's trails**, which is the stale-pairing fault the
    /// map's own collision and detail props each document.
    /// </remarks>
    public void Clear() => _running.Clear();
}
