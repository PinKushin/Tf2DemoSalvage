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

    /// <summary>The one-shots, by their caller's key. See <see cref="Bursts"/>.</summary>
    private readonly Dictionary<long, RunningBurst> _bursts = [];

    /// <summary>The tick <see cref="Bursts"/> last advanced to, so a backward seek can be recognised.</summary>
    private int _burstTick;

    /// <summary>How many effects are running.</summary>
    public int Count => _running.Count;

    /// <summary>How many one-shots are running.</summary>
    public int BurstCount => _bursts.Count;

    /// <summary>How many times one burst has been stepped, for a test or a diagnostic.</summary>
    /// <param name="key">The caller's key for it.</param>
    /// <returns>The step count, or −1 when no burst has that key.</returns>
    /// <remarks>
    /// **The value the code USED, carried out, not recomputed from the tick** (B243). A count derived here as
    /// `tick − burst.Tick` would agree with the arithmetic rather than with what was done, and the whole question
    /// is whether the stepping matches that arithmetic.
    /// </remarks>
    // Stryker disable once : removing TryGetValue leaves 'running' undeclared in ternary, CS0165 — B410.
    public int BurstSteps(long key) => _bursts.TryGetValue(key, out RunningBurst running) ? running.Stepped : -1;

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

    /// <summary>Steps every one-shot, starting one for each that is offered and not already running.</summary>
    /// <param name="live">
    /// The bursts whose effects should be running at this tick — for explosions, everything that fired inside the
    /// caller's window. A burst already running stays running whether or not it is still offered.
    /// </param>
    /// <param name="seconds">How long one tick is.</param>
    /// <param name="others">Every system that could be a CHILD, by name, as <see cref="Update"/> takes it.</param>
    /// <param name="tick">The demo tick being shown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="live"/> is null.</exception>
    /// <remarks>
    /// **A burst's state is a function of how many TICKS have passed since it fired, never of how many times this
    /// was called.** A viewer drawing three hundred frames a second calls this five times a tick, and a burst
    /// stepped per call would run five times too fast — the fault B375 found in the rocket trail, where a paused
    /// viewer piled every particle of a flight onto one point.
    ///
    /// **So a burst met part way through catches up**, which is what a viewer seeking into the middle of an
    /// explosion must see: half-finished, not beginning.
    ///
    /// **And a backward seek rebuilds, because stepping is one-way.** An effect that has run forty ticks cannot be
    /// asked for its state at ten; it is thrown away and replayed from its own tick. `CorpsePhysics` does exactly
    /// this with the physics world (D179) and for the same reason. The cost is bounded by the caller's window:
    /// nothing is replayed further than the oldest burst it still offers.
    ///
    /// **A burst no longer offered keeps running until it is empty.** Cutting an explosion off because the
    /// window moved past its start is the same mistake as removing a rocket's trail with the rocket.
    /// </remarks>
    public void Bursts(
        IReadOnlyList<ParticleBurst> live,
        float seconds,
        IReadOnlyDictionary<string, ParticleSystem>? others,
        int tick)
    {
        ArgumentNullException.ThrowIfNull(live);

        // **A seek backwards throws every burst away rather than trying to unwind one.** Each is then rebuilt
        // below from whatever the caller still offers, so the answer at this tick does not depend on how the
        // viewer arrived at it — which is the property a scrub needs and a live client never does.
        //
        // **This is NOT redundant with the per-burst rebuild below**, which was the first thing sabotage said
        // about it: for a burst the caller still offers, `Stepped > wanted` rebuilds it anyway and removing this
        // line reddened nothing. What it alone handles is a burst the caller has stopped offering — one that
        // fired AFTER the tick now being shown. Without it that effect keeps running and keeps drawing, so
        // scrubbing back before an explosion leaves the explosion on screen.
        if (tick < _burstTick)
        {
            _bursts.Clear();
        }

        _burstTick = tick;

        foreach (ParticleBurst burst in live)
        {
            int wanted = tick - burst.Tick;

            if (wanted < 0)
            {
                // Offered before it fires. Nothing to show yet, and starting it would run it early.
                continue;
            }

            // Stryker disable all : 'running' is declared by the TryGetValue before a '||' and read after it, so the
            // Logical mutator's switch leaves it unassigned (CS0165) and Safe Mode drops the method — B410.
            if (!_bursts.TryGetValue(burst.Key, out RunningBurst running) || running.Stepped > wanted)
            {
                running = new RunningBurst(new ParticleEffect(burst.Definition, others), 0);
            }

            // Stryker restore all

            // **Counted by the loop, not assigned from `wanted`.** Writing the arithmetic back is what B243
            // warns about and it happened here: with `Stepped = wanted` the step count agreed with the
            // subtraction rather than with the stepping, and replacing this whole loop with a single `Step`
            // reddened nothing at all.
            int taken = running.Stepped;

            while (taken < wanted)
            {
                running.Effect.Step(burst.At, seconds);
                taken++;
            }

            _bursts[burst.Key] = running with { Stepped = taken };
        }

        Retire();
    }

    /// <summary>Drops every burst that has run out of particles and will make no more.</summary>
    /// <remarks>
    /// **`Finished` and not `Empty`, which is the difference between the two halves of the engine's own
    /// sentence**: *"IsFinished returns true when a system has no particles and won't be creating any more"*
    /// (`particles.h:1119`). A trail can be dropped on emptiness because `Update` has already stopped its
    /// emission by then; a burst has nothing stopping it from outside, so a system whose emitter has a start
    /// time is empty on its first step and must not be thrown away before it emits.
    /// </remarks>
    private void Retire()
    {
        List<long>? finished = null;

        foreach ((long key, RunningBurst running) in _bursts)
        {
            if (running.Effect.Finished)
            {
                (finished ??= []).Add(key);
            }
        }

        foreach (long key in finished ?? [])
        {
            _bursts.Remove(key);
        }
    }

    /// <summary>One running one-shot, and how far it has been stepped.</summary>
    private readonly record struct RunningBurst(ParticleEffect Effect, int Stepped);

    /// <summary>Builds every live effect's quads for this frame's camera.</summary>
    /// <param name="eye">Where the camera is — a sprite trail turns about its own length to face it.</param>
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
        Vector3 eye,
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
            Gather(effect, eye, right, up, materials);
        }

        foreach (RunningBurst running in _bursts.Values)
        {
            Gather(running.Effect, eye, right, up, materials);
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
        Vector3 eye,
        Vector3 right,
        Vector3 up,
        IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        foreach (ParticleEffect child in effect.Children)
        {
            Gather(child, eye, right, up, materials);
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

        // **EVERY renderer the system declares runs, in its own order** — the engine walks the definition's
        // renderer list and calls each one's `Render`. `Explosion_FlyingEmbers` declares two, a sprite trail and an
        // animated sprite, so an ember is a streak with a glowing head; taking the first match drew half of it.
        //
        // **A renderer this project does not implement draws NOTHING, rather than being drawn as one it does.**
        // The fallback used to be a null renderer handed to `ParticleSprites.Build`, which does not skip — it draws
        // whole-texture billboards. **Seen, not reasoned about**: an explosion at `cp_process_f12` tick 21880 came
        // out as fifteen hard orange quads, which were the `render_sprite_trail` children drawn as sprites (B415).
        //
        // **A system with no renderer at all draws nothing**, which is what `ExplosionCore_Wall` is: a pure parent
        // whose children do the work, carrying a `material` nothing draws with.
        foreach (ParticleFunction renderer in effect.System.Renderers)
        {
            switch (renderer.Function)
            {
                case AnimatedSprites:
                    ParticleSprites.Build(
                        effect.Particles,
                        right,
                        up,
                        corners,
                        material.Sequences,
                        (float)renderer.Number("animation rate", 1d),
                        renderer.Number("use animation rate as FPS", 0d) != 0d,
                        renderer.Number("animation_fit_lifetime", 0d) != 0d);
                    break;

                // `C_OP_RenderSpriteTrail`, read out of `client.dll` — see `ParticleSpriteTrails`. Its defaults are
                // the unpack table's: `animation rate` 0.1, `min length` 0, `max length` 2000, `length fade in
                // time` 0.
                case SpriteTrail:
                    ParticleSpriteTrails.Build(
                        effect.Particles,
                        eye,
                        corners,
                        material.Sequences,
                        (float)renderer.Number("animation rate", 0.1d),
                        (float)renderer.Number("min length", 0d),
                        (float)renderer.Number("max length", 2000d),
                        (float)renderer.Number("length fade in time", 0d));
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>The velocity-stretched renderer — <c>C_OP_RenderSpriteTrail</c>'s own name.</summary>
    private const string SpriteTrail = "render_sprite_trail";

    /// <summary>The material a definition names, normalised the way a lookup key must be.</summary>
    /// <remarks>
    /// **A `.pcf` writes `effects\rocketrailsmoke.vmt` with a BACKSLASH and an extension**, and the
    /// archives are keyed with forward slashes and none. Normalising in one place is the whole of
    /// `docs/memory/key-a-lookup-on-the-question.md#lookups-must-match-exactly`: two spellings of one path is a lookup that
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
    public void Clear()
    {
        _running.Clear();
        _bursts.Clear();
        _burstTick = 0;
    }
}

/// <summary>One one-shot effect to run at a fixed point — an explosion, an impact, a decal puff (B415).</summary>
/// <param name="Key">
/// What identifies this burst between frames. It must be stable for one event and distinct between events: two
/// rockets landing on the same tick are two bursts, and a key that collapsed them would draw one explosion.
/// </param>
/// <param name="Definition">
/// The system to run. **Per burst rather than per call**, because two explosions in one frame genuinely differ —
/// a rocket against a wall is `ExplosionCore_Wall` and one in mid air is `ExplosionCore_MidAir`.
/// </param>
/// <param name="At">Where it is and which way up, which is control point 0 and its orientation.</param>
/// <param name="Tick">The demo tick it fired on, which is what its age is measured from.</param>
public readonly record struct ParticleBurst(
    long Key,
    ParticleSystem Definition,
    ParticleControlPoint At,
    int Tick);
