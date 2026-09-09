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

    /// <summary>
    /// The renderer whose parameters clock the sheet — the most-used one TF2 ships, by the census.
    /// </summary>
    private const string AnimatedSprites = "render_animated_sprites";

    /// <summary>The demo tick this last advanced to, so a still advances nothing.</summary>
    private int _tick;

    /// <summary>The live effects, by the entity they follow.</summary>
    private readonly Dictionary<int, ParticleEffect> _running = [];

    /// <summary>The entities that still exist this tick, reused to avoid allocating per frame.</summary>
    private readonly HashSet<int> _alive = [];

    /// <summary>How many effects are running.</summary>
    public int Count => _running.Count;

    /// <summary>Steps every effect, starting one for each projectile that has none.</summary>
    /// <param name="projectiles">
    /// The live projectiles this tick: where each is, where it STARTED, and how many ticks ago
    /// that was. The last two are what let a trail met mid-flight be replayed along its path
    /// instead of appearing as a single puff at the rocket (B375); pass a null start for a
    /// projectile whose history is unknown, and it simply begins empty.
    /// </param>
    /// <param name="definition">The system to run, or null when it could not be read.</param>
    /// <param name="seconds">How long this step is.</param>
    /// <param name="others">
    /// Every system that could be a CHILD, by name — `rockettrail` names two, and a child reference
    /// is by name rather than by value. Null runs the trail alone, which is a rocket with smoke and
    /// no glow.
    /// </param>
    /// <param name="tick">
    /// The demo tick being shown. The simulation advances by how far this MOVED since the last
    /// call, so a viewer parked on one tick advances it not at all — which is what a paused engine
    /// does, and what stops a still from piling every particle on one spot (B375).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="projectiles"/> is null.</exception>
    /// <remarks>
    /// **Stepped on the timeline's own interval**, which is the same clock `RagdollProps` receives —
    /// a trail that advanced on wall-clock time would stretch when the viewer stutters and compress
    /// when it scrubs.
    /// </remarks>
    public void Update(
        IReadOnlyList<(int Entity, ParticleControlPoint At, ParticleControlPoint? From, int Ticks)>
            projectiles,
        ParticleSystem? definition,
        float seconds,
        IReadOnlyDictionary<string, ParticleSystem>? others = null,
        int tick = 0)
    {
        ArgumentNullException.ThrowIfNull(projectiles);

        if (definition is null)
        {
            return;
        }

        // **The simulation runs on DEMO time, not on frames.** This used to step once per rendered
        // frame, so a viewer sitting on one tick at 294 fps advanced the trail three hundred times
        // a second with the emitter frozen in place — every particle born at the same point, which
        // is exactly the dense puff the comparison against real TF2 showed where the engine has a
        // trail stretching back down the flight path (B375).
        //
        // **A paused demo advances nothing**, which is also what the engine does: particles are
        // stepped by the game clock, and a paused clock steps them not at all.
        int advanced = tick - _tick;

        _tick = tick;

        if (advanced <= 0 || advanced > 200)
        {
            // Backwards, unchanged, or a jump too large to replay honestly. A seek is handled by
            // the per-projectile replay below rather than by stepping every effect through it.
            advanced = 0;
        }

        _alive.Clear();

        foreach ((int entity, ParticleControlPoint at, ParticleControlPoint? from, int ticks) in
            projectiles)
        {
            _alive.Add(entity);

            if (!_running.TryGetValue(entity, out ParticleEffect? effect))
            {
                effect = new ParticleEffect(definition, others);
                _running[entity] = effect;
            }

            // **Replay when the trail is EMPTY, not only on the frame it was created.** Keying it
            // to creation meant a projectile that appeared during a seek — before the tick settled,
            // when its history was not yet what it would be — got its one chance and missed, and
            // the trail stayed empty for as long as the viewer sat on that tick.
            // **A trail has a HISTORY, and a seek does not create one.** Measured against real TF2
            // at `cp_process_f12`: the engine's smoke stretches back along the rocket's whole flight
            // while ours was a single puff at the rocket. An effect met mid-flight has emitted
            // nothing, so it is replayed from where the projectile started — which is what TF2 does
            // the long way round, by restarting the demo and fast-forwarding through it (B375).
            //
            // **Straight-line replay is faithful for a rocket**: `DT_TFBaseRocket` networks
            // `m_vInitialVelocity` and nothing accelerates it, so interpolating spawn to now is the
            // path rather than an approximation of it. It would NOT be for anything that arcs,
            // which is why this takes the two ends rather than assuming them.
            if (effect.Empty && from is { } start && ticks > 0)
            {
                for (int step = 1; step <= ticks; step++)
                {
                    float along = (float)step / ticks;

                    effect.Step(
                        new ParticleControlPoint(
                            Vector3.Lerp(start.At, at.At, along),
                            at.Forward,
                            at.Right,
                            at.Up),
                        seconds);
                }

                continue;
            }

            for (int step = 0; step < advanced; step++)
            {
                effect.Step(at, seconds);
            }
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

            for (int step = 0; step < advanced; step++)
            {
                effect.Fade(seconds);
            }

            // **`Empty` and not `Particles.Count`**, because a parent is not finished while a child
            // still has particles — *"make sure all children are finished"* (`particles.h:1630`).
            // Dropping on the parent's own count alone cuts a rocket's fire off the moment its
            // smoke runs out.
            if (effect.Empty)
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
    /// <param name="materials">Every particle material this map loaded, by its normalised name.</param>
    /// <returns>One batch per material that has quads this frame, reused between frames.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The frame clock is read from each effect's OWN renderer**, not from a constant here. Two
    /// systems drawn in the same frame can animate at different rates, and `rockettrail`'s three is
    /// its own number rather than a property of sprite sheets.
    ///
    /// **Batches rather than one corner list, because a rocket is three systems on three
    /// materials** — smoke, burst and fire — and an additive glow cannot share a draw call with
    /// translucent smoke. The returned list and its corner lists are REUSED between frames, so a
    /// caller that needs them after the next `Build` must copy.
    /// </remarks>
    public IReadOnlyList<ParticleBatch> Build(
        Vector3 right,
        Vector3 up,
        IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        foreach (List<DetailSpriteVertex> corners in _byMaterial.Values)
        {
            corners.Clear();
        }

        foreach (ParticleEffect effect in _running.Values)
        {
            Gather(effect, right, up, materials);
        }

        _batches.Clear();

        foreach ((string named, List<DetailSpriteVertex> corners) in _byMaterial)
        {
            if (corners.Count > 0 && materials.TryGetValue(named, out ParticleMaterial material))
            {
                _batches.Add(new ParticleBatch(corners, material));
            }
        }

        return _batches;
    }

    /// <summary>Builds one effect's quads and every child's, into their own materials' groups.</summary>
    /// <remarks>
    /// **Grouped by material NAME rather than per effect**, so two rockets in flight share one draw
    /// per material instead of costing two each. The engine batches the same way, and the reason is
    /// the same: a draw call is per material, and the material is the thing that differs.
    /// </remarks>
    private void Gather(
        ParticleEffect effect,
        Vector3 right,
        Vector3 up,
        IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        foreach (ParticleEffect child in effect.Children)
        {
            Gather(child, right, up, materials);
        }

        string named = MaterialOf(effect.System);

        if (!materials.TryGetValue(named, out ParticleMaterial material))
        {
            // A material this project could not resolve draws nothing rather than drawing the
            // chequer: an unreadable particle texture is a missing effect, not a missing surface.
            return;
        }

        if (!_byMaterial.TryGetValue(named, out List<DetailSpriteVertex>? corners))
        {
            corners = [];
            _byMaterial[named] = corners;
        }

        ParticleFunction? renderer = null;

        foreach (ParticleFunction one in effect.System.Renderers)
        {
            if (string.Equals(one.Function, AnimatedSprites, StringComparison.Ordinal))
            {
                renderer = one;
                break;
            }
        }

        ParticleSprites.Build(
            effect.Particles,
            right,
            up,
            corners,
            material.Sequences,
            (float)(renderer?.Number("animation rate", 1d) ?? 1d),
            (renderer?.Number("use animation rate as FPS", 0d) ?? 0d) != 0d,
            (renderer?.Number("animation_fit_lifetime", 0d) ?? 0d) != 0d);
    }

    /// <summary>The material a definition names, normalised the way a lookup key must be.</summary>
    /// <remarks>
    /// **A `.pcf` writes `effects\rocketrailsmoke.vmt` with a BACKSLASH and an extension**, and the
    /// archives are keyed with forward slashes and none. Normalising in one place is the whole of
    /// `docs/memory/lookups-must-match-exactly.md`: two spellings of one path is a lookup that
    /// misses silently and draws nothing.
    /// </remarks>
    public static string MaterialOf(ParticleSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        return system.Parameters.TryGetValue("material", out DmxValue value) &&
            value.Text is { Length: > 0 } declared
            ? declared.Replace('\\', '/')
                .Replace(".vmt", string.Empty, StringComparison.OrdinalIgnoreCase)
            : string.Empty;
    }

    /// <summary>This frame's corners, by material name, reused so a frame costs no allocation.</summary>
    private readonly Dictionary<string, List<DetailSpriteVertex>> _byMaterial =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The batches handed back, reused for the same reason.</summary>
    private readonly List<ParticleBatch> _batches = [];

    /// <summary>Forgets every effect, for a demo change.</summary>
    /// <remarks>
    /// **A new demo must not inherit the last one's trails**, which is the stale-pairing fault the
    /// map's own collision and detail props each document.
    /// </remarks>
    public void Clear() => _running.Clear();
}
