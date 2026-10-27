using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

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
    private double _tick = double.NaN;

    /// <summary>The live effects, by the entity they follow.</summary>
    private readonly Dictionary<int, ParticleEffect> _running = [];

    /// <summary>The trails made on a played frame, which are never replayed from their rocket's start.</summary>
    private readonly HashSet<int> _playedFrom = [];

    /// <summary>The entities that still exist this tick, reused to avoid allocating per frame.</summary>
    private readonly HashSet<int> _alive = [];

    /// <summary>The one-shots, by their caller's key. See <see cref="Bursts"/>.</summary>
    private readonly Dictionary<long, RunningBurst> _bursts = [];

    /// <summary>The moment <see cref="Bursts"/> last showed; NaN before the first call, which no burst fired after.</summary>
    private double _burstShown = double.NaN;

    /// <summary>The tick <see cref="Bursts"/> last advanced to, so a backward seek can be recognised.</summary>
    private int _burstTick;

    /// <summary>How many effects are running.</summary>
    public int Count => _running.Count;

    /// <summary>The sheet each system's material carries, for `Lifetime From Sequence`; null before a map's materials load.</summary>
    public Func<ParticleSystem, IReadOnlyList<SheetSequence>?>? Sheets { get; set; }

    /// <summary>How many one-shots are running.</summary>
    public int BurstCount => _bursts.Count;

    /// <summary>Stopwatch ticks spent replaying rebuilt bursts, ever — a seek's cost, which D217's checkpoints are to carry.</summary>
    public long ReplayTicks { get; private set; }

    /// <summary>Steps taken replaying rebuilt bursts, ever.</summary>
    public long ReplaySteps { get; private set; }

    /// <summary>The most one frame simulates — `if( flTimeDelta &gt; 0.1f ) flTimeDelta = 0.1f;` (particlemgr.cpp:1947).</summary>
    private const float FrameStepLimit = 0.1f;

    /// <summary>Effects held, summed over every call of <see cref="Bursts"/> and <see cref="Update"/> — alive per frame.</summary>
    public long Alive { get; private set; }

    /// <summary>Steps taken on played frames, bursts and trails, counted at each step; a replay's are <see cref="ReplaySteps"/>.</summary>
    public long Steps { get; private set; }

    /// <summary>Played frames on which a burst stood asleep, once per burst per frame (`BuildParticleSimList`'s skip).</summary>
    public long Slept { get; private set; }

    /// <summary>Effects with particles handed to the draw by the scene's <see cref="Build(Vector3, Vector3, Vector3, IReadOnlyDictionary{string, ParticleMaterial}, CollationView)"/>.</summary>
    public long Gathered { get; private set; }

    /// <summary>What a finished burst keeps in place of its effect: nothing to draw, nothing to hold.</summary>
    private static readonly ParticleEffect Spent = SpentMarker();

    /// <summary>The marker, simulated once so `IsFinished`'s first-frame flag is clear and it reads finished.</summary>
    private static ParticleEffect SpentMarker()
    {
        ParticleEffect spent = new(
            new ParticleSystem("spent", [], [], [], [], [], new Dictionary<string, DmxValue>(StringComparer.Ordinal)), null, null, 0);

        spent.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0f);

        return spent;
    }

    /// <summary>How many times a burst was built from its start, ever — counted where it is built.</summary>
    public int BurstBuilds { get; private set; }

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

    /// <summary>One running burst's own particles, for a test or a diagnostic.</summary>
    /// <param name="key">The caller's key for it.</param>
    /// <returns>The store, or null when no burst has that key.</returns>
    // Stryker disable once : removing TryGetValue leaves 'running' undeclared in ternary, CS0165 — B410.
    public ParticleStore? BurstParticles(long key) =>
        _bursts.TryGetValue(key, out RunningBurst running) ? running.Effect.Particles : null;

    /// <summary>One running burst's effect, children included, for the viewer's pixel pick.</summary>
    /// <param name="key">The caller's key for it.</param>
    /// <returns>The effect, or null when no burst has that key.</returns>
    // Stryker disable once : removing TryGetValue leaves 'running' undeclared in ternary, CS0165 — B410.
    public ParticleEffect? BurstEffect(long key) =>
        _bursts.TryGetValue(key, out RunningBurst running) ? running.Effect : null;

    /// <summary>One projectile's trail, children included, for a test or a diagnostic.</summary>
    /// <param name="entity">The projectile's entity index.</param>
    /// <returns>The effect, or null when that entity has none.</returns>
    public ParticleEffect? TrailEffect(int entity) => _running.GetValueOrDefault(entity);

    /// <summary>The seed an effect's identity fixes — the engine's seeded collection, for an effect a seek must reproduce (B469).</summary>
    /// <param name="identity">What tells this effect from every other: a burst's key, a trail's entity and spawn tick.</param>
    /// <returns>A non-zero seed.</returns>
    /// <remarks>
    /// **The TF2 client seeds every collection with <c>(int)this + Plat_MSTime()</c>** — a pointer and a clock, which no
    /// demo carries and no replay could repeat. D136's rule is to reproduce the mechanism and fix the input the way a
    /// replay needs, and the engine has a path for exactly that: a non-zero seed passed to `Init` is kept as is
    /// (<c>m_bIsScrubbable</c>, read in `particles.lib`). **Hashed rather than used raw** because the seed is added to
    /// every table index, so two keys one apart would draw the same numbers one particle apart; the Fibonacci hash puts
    /// neighbours far apart, as the engine's pointers, a whole collection apart, are. **Never 0**, which `Init` would
    /// take for "unseeded".
    /// </remarks>
    public static int SeedFor(long identity)
    {
        int seed = (int)(((ulong)identity * 0x9E3779B97F4A7C15UL) >> 32);

        return seed != 0 ? seed : 1;
    }

    /// <summary>How many collections <see cref="NextCreatedSeed"/> has seeded in this process.</summary>
    private static long _created;

    /// <summary>A seed for a collection with no identity a seek could fix — a HUD particle panel's, a model panel's (B490).</summary>
    /// <returns>A non-zero seed, different for every call.</returns>
    /// <remarks>
    /// **The engine's own input here is <c>(int)this + Plat_MSTime()</c>**, distinct per collection because two live
    /// collections never share an address. The stand-in keeps that distinctness and drops the clock: the creation count,
    /// through <see cref="SeedFor"/>. A panel runs on wall-clock time, so nothing seeks it; the count makes a run repeat
    /// itself given the same order of creation, which is the most a wall-clock panel can promise.
    /// </remarks>
    public static int NextCreatedSeed() => SeedFor(global::System.Threading.Interlocked.Increment(ref _created));

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
    /// <param name="shown">The moment shown, fraction included, which the trails are stepped to (D221); null for <paramref name="tick"/>.</param>
    /// <param name="seeked">Whether this frame landed by a seek; every trail is then dropped and replayed from its rocket's start.</param>
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
        int tick = 0,
        double? shown = null,
        bool seeked = false)
    {
        ArgumentNullException.ThrowIfNull(projectiles);

        if (definition is null)
        {
            return;
        }

        double now = shown ?? tick;

        // **The simulation runs on DEMO time, not on frames.** This used to step once per rendered
        // frame, so a viewer sitting on one tick at 294 fps advanced the trail three hundred times
        // a second with the emitter frozen in place — every particle born at the same point, which
        // is exactly the dense puff the comparison against real TF2 showed where the engine has a
        // trail stretching back down the flight path (B375).
        //
        // **A paused demo advances nothing**, which is also what the engine does: particles are
        // stepped by the game clock, and a paused clock steps them not at all.
        //
        // **Once a FRAME, by the frame's demo time** (D221): `ParticleMgr()->Simulate( gpGlobals->frametime )`
        // (cdll_client_int.cpp:2234), however long the frame. **A seek replays a trail along its rocket's path** — the
        // replay below, from the projectile's start — rather than stepping it on today's point: every trail is dropped
        // and rebuilt, our addition as the bursts' is (D221).
        double advanced = now - _tick;
        double previous = _tick;

        _tick = now;
        _seconds = seconds;

        // NaN before the first call: the opening is not a played frame, whatever tick it opens on.
        bool played = !(seeked || double.IsNaN(advanced) || advanced < 0d || advanced > 200d);

        if (!played)
        {
            // **A seek throws every trail away, as `Bursts` does** (B420): backwards, or too far forwards to replay.
            // A trail whose rocket is gone fades only by ticks played forward, and a seek plays none — so kept, it
            // stayed for good and was gathered on every paused frame, and a later rocket reusing its entity index
            // inherited its particles. The rockets still flying replay their own from where they started, below.
            _running.Clear();
            _playedFrom.Clear();
            _trailSleep.Clear();
            _orphans.Clear();
            advanced = 0d;
        }

        // A played frame is one step, of at most 0.1 s (`UpdateAllEffects`, particlemgr.cpp:1947).
        float last = MathF.Min((float)(advanced * seconds), FrameStepLimit);

        _alive.Clear();

        foreach ((int entity, ParticleControlPoint at, ParticleControlPoint? from, int ticks) in
            projectiles)
        {
            _alive.Add(entity);

            // **A projectile on an index whose last one is gone is a new projectile** (Fable review, item 3): the orphan
            // trail is dropped rather than inherited, which one asleep with live particles would otherwise be for good
            // (B420). The engine deletes the entity and its effect stops; the new one gets its own.
            // ponytail: drops that orphan's remaining particles; key trails by (entity, start) if a fading tail must outlive
            // its index's reuse.
            if (_orphans.Remove(entity))
            {
                _running.Remove(entity);
                _playedFrom.Remove(entity);
                _trailSleep.Remove(entity);
            }

            if (!_running.TryGetValue(entity, out ParticleEffect? effect))
            {
                // Entity indices are reused, so the trail is identified by its entity AND the tick its track began.
                effect = new ParticleEffect(definition, others, Sheets, SeedFor(((long)(tick - ticks) << 32) | (uint)entity));
                _running[entity] = effect;

                // `StartEmission` at creation: asleep at now plus the no-draw time unless a draw pushes it on (finding 84).
                _trailSleep[entity] = now + (ParticleEffect.SleepAfter(definition) / seconds);

                // **Made in the frame its rocket appeared, it simulates nothing of that frame** (B557 row 6):
                // `Simulate( 0.0f )` on the first frame (particlemgr.cpp:1572-1575), and it is never replayed. A rocket that
                // started before the last frame was met mid-flight — a seek, or the opening — and replays below (B375).
                if (played && tick - ticks > previous)
                {
                    _playedFrom.Add(entity);
                    effect.Step(at, 0f);
                    continue;
                }
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
            if (effect.Empty && from is { } start && ticks > 0 && !_playedFrom.Contains(entity))
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

            if (last > 0f && TrailAsleep(entity, effect, now))
            {
                // Asleep, it is not simulated and its clock stands; its point is still read (particlemgr.cpp:1841-1853),
                // and one that moved simulates its bounds only (particlemgr.cpp:1785-1789), as a burst's does (B557 row 9).
                effect.SetControlPoint(0, at);

                if (effect.HasMoved())
                {
                    effect.SimulateBoundsOnly(last);
                }

                Slept++;
            }
            else if (last > 0f)
            {
                effect.Step(at, last);
                Steps++;
            }
            else
            {
                // A still frame reads the point all the same (particlemgr.cpp:1841-1853), as a burst's does.
                effect.SetControlPoint(0, at);
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

            _orphans.Add(entity);

            if (last > 0f && TrailAsleep(entity, effect, now))
            {
                Slept++;
            }
            else if (last > 0f)
            {
                effect.Fade(last);
                Steps++;
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
            _playedFrom.Remove(entity);
            _trailSleep.Remove(entity);
            _orphans.Remove(entity);
        }

        Alive += _running.Count;
    }

    /// <summary>
    /// Whether a trail sits out this frame: collated by a view, at or past its sleep time with live particles of its own —
    /// `BuildParticleSimList`'s skip, `flNow` against `m_flNextSleepTime` (particlemgr.cpp:1775).
    /// </summary>
    private bool TrailAsleep(int entity, ParticleEffect effect, double now) =>
        _collating && _trailSleep.TryGetValue(entity, out double sleepAt) && now >= sleepAt && effect.Particles.Count > 0;

    /// <summary>Steps every one-shot, starting one for each that is offered and not already running.</summary>
    /// <param name="live">
    /// The bursts whose effects should be running at this tick — for explosions, everything that fired inside the
    /// caller's window. A burst already running stays running whether or not it is still offered.
    /// </param>
    /// <param name="seconds">How long one tick is.</param>
    /// <param name="others">Every system that could be a CHILD, by name, as <see cref="Update"/> takes it.</param>
    /// <param name="tick">The demo tick being shown.</param>
    /// <param name="shown">The moment shown, fraction included, which the bursts are stepped to; null for <paramref name="tick"/>.</param>
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
    ///
    /// **Played, a burst is stepped once a FRAME by the frame's demo time, on the frame's control points** (D221):
    /// `ParticleMgr()->Simulate( gpGlobals->frametime )` (cdll_client_int.cpp:2234) runs every frame after the view
    /// models are placed (`:2184`), and `UpdateControlPoint` re-reads each followed point (particle_property.cpp:427-446,
    /// 527-640). **However long the frame**: a played frame is one `Simulate( frametime )`, which cuts itself into
    /// sub-steps. A frame that does not move the clock — a paused one — steps nothing and still reads its points.
    ///
    /// **A seek rebuilds, and that is OUR addition, not the engine's** (D221): on <paramref name="seeked"/>, or a clock
    /// that went backwards, the effect is replayed from its start a tick at a time, each at that tick's points, then the
    /// fraction — so an effect met mid-life is shown mid-life, and the same after any seek to that moment.
    /// </remarks>
    /// <param name="seeked">Whether this frame landed by a transport jump rather than by playing; false for a played
    /// frame of any length.</param>
    public void Bursts(
        IReadOnlyList<ParticleBurst> live,
        float seconds,
        IReadOnlyDictionary<string, ParticleSystem>? others,
        int tick,
        double? shown = null,
        bool seeked = false)
    {
        ArgumentNullException.ThrowIfNull(live);

        double now = shown ?? tick;

        // The frame's simulation time and tick, which a draw in `Build` pushes the sleep time from.
        _burstNow = now;
        _seconds = seconds;

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

        // A clock that went backwards landed as surely as a flagged seek did.
        bool landedHere = seeked || tick < _burstTick;

        if (landedHere)
        {
            _spent.Clear();
        }

        _burstTick = tick;
        _offered.Clear();

        double previousShown = _burstShown;

        _burstShown = now;

        // By reference, not `foreach`: a by-value walk copied the whole reference-holding struct per element through a
        // write barrier (B592, profile 2026-10-24). The caller's list is not changed while this runs.
        // Held for the frame so a slot can name its offer by index and a worker read it by reference.
        _offerList = live as List<ParticleBurst>;
        _offerArray = _offerList is null ? live as ParticleBurst[] ?? [.. live] : null;

        ReadOnlySpan<ParticleBurst> offered = Offered;

        // **Three passes, as `UpdateNewEffects` makes them** (particlemgr.cpp:1841-1904, finding 103): everything that reads
        // or writes this collection or asks a caller's callback, serially and in offer order — the engine's `Update` and
        // `BuildParticleSimList`; the played steps, one effect each, on `r_threaded_particles`' fork-join — its
        // `ProcessPSystem`; then every write, in offer order again, so `_bursts` is mutated in exactly the serial sequence
        // and enumerates in the serial order — as `DetectChanges` runs after the join.
        _slotsUsed = 0;
        _slotsFlushed = 0;

        for (int each = 0; each < offered.Length; each++)
        {
            ref readonly ParticleBurst burst = ref offered[each];

            // **A key offered twice sees its first offer's write**, as the serial loop did: what is pending lands first.
            if (!_offered.Add(burst.Key))
            {
                FlushBursts(seconds);
            }

            double wanted = now - burst.Tick;

            if (wanted < 0d)
            {
                // Offered before it fires. Nothing to show yet, and starting it would run it early.
                continue;
            }

            // **Finished while playing, it is gone** — `CParticleMgr` removes an effect once `IsFinished` (particles.h:1119)
            // and only a new dispatch makes another. An entity's stopless dispatch is offered for the rest of the demo, so
            // an owner unposed for a frame dropped them all, and posed again replayed each from its own tick: 90 rocket-jump
            // smokes, a 248 ms frame on f12 every few seconds (finding 84).
            //
            // **And one past its definition's whole life is not built by playing**: it would have finished. Out of view is
            // not dormant, so the engine made it at its dispatch, and one inside its life IS built, replayed from its own
            // tick. A held one is never skipped: asleep, its clock stands. Built anyway, a player out of view at the seek had
            // every past rocket-jump smoke, all long finished, replayed in one frame (finding 84).
            if (_spent.Contains(burst.Key))
            {
                continue;
            }

            if (!landedHere && !_bursts.ContainsKey(burst.Key) && wanted * seconds > LifeOf(burst.Definition, others))
            {
                _spent.Add(burst.Key);
                continue;
            }

            double noDraw = ParticleEffect.SleepAfter(burst.Definition) / seconds;

            // Stryker disable all : 'running' is declared by the TryGetValue before a '||' and read after it, so the
            // Logical mutator's switch leaves it unassigned (CS0165) and Safe Mode drops the method — B410.
            bool rebuild = !_bursts.TryGetValue(burst.Key, out RunningBurst running) ||
                seeked ||
                running.SimulatedTo > wanted;

            if (rebuild)
            {
                // **None is made on a dormant owner** (c_particle_system.cpp:189-190): replayed or played, the same answer.
                // **Nor one the caller cannot place**: with no point to replay on it waits, and is built — replayed from its
                // own tick — once it can be placed. Only one already running sleeps.
                if (burst.DormantAt?.Invoke(burst.Tick) == true || burst.Undrawn)
                {
                    // Removed in the write pass, in offer order: a remove frees a slot a later insert reuses.
                    NextSlot(each).Remove = true;
                    continue;
                }

                // **Replayed from its own tick, whatever its age** — a seek answers what playing through shows, so it walks
                // the same ticks under the same rules; the replay stops early only once the effect can change no more.
                // `StartEmission` at creation sets the sleep time to now plus the no-draw time (particles.obj, Ghidra).
                running = new RunningBurst(new ParticleEffect(burst.Definition, others, Sheets, SeedFor(burst.Key)), 0, 0d, false)
                {
                    NextSleep = noDraw, LastAt = burst.At,
                };
                BurstBuilds++;
            }
            else if (running.Effect.Finished)
            {
                // **Finished and still offered: kept, and not stepped** — `IsFinished` has no particles and will make none
                // (particles.h:1119); stepping it is work with nothing to show. Kept so the offer does not rebuild it (B415).
                continue;
            }

            // Stryker restore all

            if (burst.End is { } end)
            {
                running.Effect.SetControlPoint(1, end);
            }

            BurstSlot slot = NextSlot(each);

            slot.Running = running;
            slot.Rebuild = rebuild;
            slot.Wanted = wanted;
            slot.NoDraw = noDraw;

            // **Counted by the loop, not assigned from `wanted`** (B243): the count says what was stepped.
            slot.Taken = running.Stepped;
            slot.ReachedTo = running.SimulatedTo;

            // **The creation frame simulates nothing of its own time** (B557 row 6): `ProcessPSystem` gives a new effect
            // `Simulate( 0.0f )` and clears its first-frame flag (particlemgr.cpp:1572-1575), and the next frame simulates
            // its whole frame time. Only for a burst that fired since the last frame, offered on a played one: one met
            // later than that — a seek, the opening, a window that moved — is replayed from its tick as before (D221).
            if (rebuild && !seeked && burst.Tick > previousShown)
            {
                running.Effect.Step(burst.At, 0f);
                slot.ReachedTo = wanted;
            }

            slot.NextSleep = running.NextSleep;
            slot.WasDormant = running.Dormant;
            slot.StopCleared = running.StopCleared;

            // **Undrawn, it follows the points it last had** — the caller could not place it this frame.
            slot.Current = burst.Undrawn ? running.LastAt : burst.At;

            // Asked once, and only when there is a step to take: posing a model is not free. Caller code, so asked here.
            slot.Posed = slot.ReachedTo < wanted && !burst.Undrawn ? burst.Model?.Invoke() : null;

            // A rebuild replays whole ticks first, each where its points were then (B527); the last step — the only one a
            // played frame takes — runs to this frame's moment on this frame's points.
            // `StopEmission` lands before the frame's `Simulate`, so a step that crosses the stop is split there.
            slot.StopAt = burst.StopTick is { } stopping ? stopping - burst.Tick : double.MaxValue;

            if (rebuild)
            {
                // **A replay asks its caller for every tick's points** (`History`) and dormancy, which is caller code: run
                // here, serially, as `Update` runs `UpdateControlPoint` before the parallel simulation (particlemgr.cpp:1847).
                long replayFrom = System.Diagnostics.Stopwatch.GetTimestamp();

                Advance(slot, burst, seconds);
                slot.ReplayTicks = System.Diagnostics.Stopwatch.GetTimestamp() - replayFrom;
            }
            else
            {
                // A played step's ends and dormancy are asked now, on this thread; the workers read only the answers.
                Plan(slot, burst);
            }
        }

        FlushBursts(seconds);

        // Nothing past the join holds the caller's list.
        _offerList = null;
        _offerArray = null;

        FadeUnoffered(now, seconds, seeked);
        Retire();
        Alive += _bursts.Count;
    }

    /// <summary>This frame's offers, by reference: the caller's own list or array, or a copy of anything else.</summary>
    private ReadOnlySpan<ParticleBurst> Offered =>
        _offerList is { } list ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list) : _offerArray;

    /// <summary>The caller's list for the frame, when it is one; the caller does not change it while <see cref="Bursts"/> runs.</summary>
    private List<ParticleBurst>? _offerList;

    /// <summary>The caller's array, or a copy of a list of another kind, for the frame.</summary>
    private ParticleBurst[]? _offerArray;

    /// <summary>The fork the played steps run on — `r_threaded_particles`' lane over the shared pool, which the viewer sets.</summary>
    public WorkerLane Workers { get; set; } = new() { Threshold = DefaultWorkerThreshold };

    /// <summary>The particle lane's threshold: a list of this many played steps or fewer runs serially.</summary>
    /// <remarks>0, 4 and 16 measured the same on f12 (docs/measurements/2026-10-26-threaded-particles-and-sun.md); kept as a guard for tiny lists.</remarks>
    public const int DefaultWorkerThreshold = 4;

    /// <summary>The next unit of this frame's burst work, reused between frames.</summary>
    private BurstSlot NextSlot(int offer)
    {
        if (_slotsUsed == _slots.Count)
        {
            _slots.Add(new BurstSlot());
        }

        BurstSlot slot = _slots[_slotsUsed++];

        slot.Offer = offer;
        slot.Remove = false;
        slot.Advanced = false;
        slot.SleptHere = false;
        slot.ReplayTicks = 0L;
        slot.Ends.Clear();
        return slot;
    }

    /// <summary>Steps every pending played burst on the workers, then writes every pending burst in offer order.</summary>
    /// <remarks>
    /// **The join is here**, before any write, as `ParallelProcess` returns before `DetectChanges` (particlemgr.cpp:1869-1904).
    /// Only the slots with a step to take are handed out, so <see cref="WorkerLane.Threshold"/> counts real work.
    /// </remarks>
    private void FlushBursts(float seconds)
    {
        _toAdvance.Clear();

        for (int at = _slotsFlushed; at < _slotsUsed; at++)
        {
            if (!_slots[at].Remove && !_slots[at].Advanced)
            {
                _toAdvance.Add(at);
            }
        }

        _flushSeconds = seconds;
        Workers.For(_toAdvance.Count, _advanceSlot ??= AdvanceSlot);

        for (int at = _slotsFlushed; at < _slotsUsed; at++)
        {
            Commit(_slots[at]);
        }

        _slotsFlushed = _slotsUsed;
    }

    /// <summary>One worker's unit: a played burst's steps, which touch its own effect and slot and nothing else.</summary>
    /// <remarks>
    /// **`Advance` is static** so it cannot reach this collection, its counters or a caller's callback: everything it
    /// reads was asked serially into the slot, which is what makes the job `ProcessPSystem`'s and nothing more.
    /// </remarks>
    private void AdvanceSlot(int index)
    {
        BurstSlot slot = _slots[_toAdvance[index]];

        Advance(slot, Offered[slot.Offer], _flushSeconds);
    }

    /// <summary>
    /// A played burst's step ends and the owner's dormancy for each, asked of the caller on this thread: `Advance` walks
    /// the same ends, so a worker never calls caller code.
    /// </summary>
    /// <remarks>
    /// **The ends do not depend on the effect**: a sleeping step and a stepped one both move to the same end, and a
    /// finished one stops early, so this list is the steps `Advance` takes or a longer one.
    /// </remarks>
    private static void Plan(BurstSlot slot, in ParticleBurst burst)
    {
        double reachedTo = slot.ReachedTo;

        while (reachedTo < slot.Wanted)
        {
            double next = NextEnd(slot, burst, reachedTo);

            slot.Ends.Add((next, burst.DormantAt?.Invoke(burst.Tick + (int)Math.Floor(next)) == true));
            reachedTo = next;
        }
    }

    /// <summary>Where the step from <paramref name="reachedTo"/> ends.</summary>
    private static double NextEnd(BurstSlot slot, in ParticleBurst burst, double reachedTo)
    {
        double wanted = slot.Wanted;
        double next = slot.Rebuild && Math.Floor(reachedTo) + 1d <= wanted ? Math.Floor(reachedTo) + 1d : wanted;

        // **A played frame stays one step (D221) unless the owner's dormancy changes inside it**: then it is cut at
        // the tick of the change, so a gap shorter than the frame is not stepped over. Dormancy changes on a
        // snapshot, which lands on a tick.
        if (!slot.Rebuild && burst.DormantAt is { } asked)
        {
            next = DormancyChange(asked, burst.Tick, reachedTo, wanted);
        }

        if (reachedTo < slot.StopAt && next > slot.StopAt)
        {
            next = slot.StopAt;
        }

        return next;
    }

    /// <summary>Steps one burst's effect from where it reached to the frame's moment.</summary>
    /// <remarks>A rebuild asks its caller as it goes, on the calling thread; a played burst reads what <see cref="Plan"/> asked.</remarks>
    private static void Advance(BurstSlot slot, in ParticleBurst burst, float seconds)
    {
        slot.Advanced = true;

        RunningBurst running = slot.Running;
        bool rebuild = slot.Rebuild;
        double wanted = slot.Wanted;
        double noDraw = slot.NoDraw;
        int taken = slot.Taken;
        double reachedTo = slot.ReachedTo;
        double nextSleep = slot.NextSleep;
        bool wasDormant = slot.WasDormant;
        bool stopCleared = slot.StopCleared;
        ParticleControlPoint current = slot.Current;
        (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)? posed = slot.Posed;
        bool sleptHere = false;
        int planned = 0;

        // **A played frame simulates at most 0.1 s**, its pieces together: `UpdateAllEffects` clamps the frame time
        // (particlemgr.cpp:1947) and hands it to `UpdateNewEffects` (:1995). A replay is not a frame.
        float budget = rebuild ? float.MaxValue : FrameStepLimit;

        while (reachedTo < wanted)
        {
            // **A dormant tick stops emission and still simulates**: `CParticleCollection::SetDormant(true)` is
            // `StopEmission(false, false, false)`, `SetDormant(false)` is `StartEmission(false)`, and `Simulate` never
            // asks (particles.obj, Ghidra). The tick asked is the one the step simulates INTO — the frame drawn at it.
            (double next, bool dormant) = rebuild ? Asked(slot, burst, reachedTo) : slot.Ends[planned++];

            // **Waking is `StartEmission`**: the emitters' `SkipToTime`, and the sleep time set to now plus the no-draw
            // time. `SetDormant` (`0x000e5c60`) checks emission-stopped (`+0x69`) only on ENTERING dormancy, so an
            // effect already stopped never goes dormant; once dormant, the wake calls `StartEmission` whatever stopped
            // since, and that clears the stop.
            bool stoppedHere = !stopCleared && burst.StopTick is { } stopsAt && burst.Tick + reachedTo >= stopsAt;

            if (wasDormant && !dormant)
            {
                running.Effect.StartEmission();
                nextSleep = reachedTo + noDraw;
                // Only a stop already landed is cleared; one still to come stops the effect when it arrives. Same basis
                // as `stoppedHere`: the time the step starts from, which is where the wake runs.
                stopCleared = burst.StopTick is { } landed && burst.Tick + reachedTo >= landed;
                stoppedHere = false;
            }

            wasDormant = dormant && (wasDormant || !stoppedHere);

            // **`SetDormant` writes the flag `IsFinished` reads** (`0xe5c60`, `0xe3c60`): a dormant effect is never
            // finished, so an empty one waits for its owner rather than being let go (finding 86, review item 1).
            running.Effect.Dormant = wasDormant;

            // **Asleep, its clock stands**: `BuildParticleSimList` skips an effect at or past `m_flNextSleepTime` while
            // `m_nActiveParticles > 0` (particlemgr.cpp:1775-1776) — the root collection's own count; its children
            // are simulated with it and never sleep apart. Only a draw pushes the time on.
            if (reachedTo >= nextSleep && running.Effect.Particles.Count > 0)
            {
                // **Unless it moved** (B557 row 9): then `Simulate( dt, true )`, bounds only — the frame's time and
                // the previous points kept, nothing simulated (particlemgr.cpp:1785-1789). The points are read first,
                // as `Update` reads them before `BuildParticleSimList` (:1841-1853).
                running.Effect.SetControlPoint(0, current);

                if (running.Effect.HasMoved())
                {
                    running.Effect.SimulateBoundsOnly((float)((next - reachedTo) * seconds));
                }

                reachedTo = next;
                sleptHere = true;
                continue;
            }

            // **Finished can change no more** — no particles and none to come (particles.h:1119) — so the rest of a
            // replay would step nothing.
            if (taken > 0 && running.Effect.Finished)
            {
                reachedTo = wanted;
                break;
            }

            int reached = burst.Tick + (int)next;
            bool past = rebuild && next < wanted && burst.History is not null;

            (ParticleControlPoint at, ParticleControlPoint? second) =
                past ? burst.History!(reached) : (current, burst.End);

            if (second is { } then)
            {
                running.Effect.SetControlPoint(1, then);
            }

            // The entity's hitboxes, where control point 0 is on this step (B538).
            if (posed is { } model)
            {
                running.Effect.SetHitboxes(0, Carried(model.Boxes, at.At - model.PosedAt));
            }

            float step = MathF.Min((float)((next - reachedTo) * seconds), budget);
            budget -= step;

            // A stopped effect only fades: its particles live out their lives and no more are born. **Its points
            // still follow** — `StopEmission` leaves the effect in the property's list (particle_property.cpp:251).
            if (dormant || (!stopCleared && burst.StopTick is { } stop && burst.Tick + reachedTo >= stop))
            {
                running.Effect.SetControlPoint(0, at);
                running.Effect.Fade(step);
            }
            else
            {
                running.Effect.Step(at, step);
            }

            // A replayed tick is a frame that was drawn: `Render` pushes the sleep time on.
            if (rebuild && next < wanted)
            {
                nextSleep = Math.Max(nextSleep, next + noDraw);
            }

            reachedTo = next;
            taken++;
        }

        slot.Taken = taken;
        slot.ReachedTo = reachedTo;
        slot.NextSleep = nextSleep;
        slot.WasDormant = wasDormant;
        slot.StopCleared = stopCleared;
        slot.SleptHere = sleptHere;
    }

    /// <summary>A rebuild's step end and dormancy, asked of the caller as the step is taken.</summary>
    private static (double Next, bool Dormant) Asked(BurstSlot slot, in ParticleBurst burst, double reachedTo)
    {
        double next = NextEnd(slot, burst, reachedTo);

        return (next, burst.DormantAt?.Invoke(burst.Tick + (int)Math.Floor(next)) == true);
    }

    /// <summary>Writes one burst's frame back, in offer order: the counters, a still frame's points, and its entry.</summary>
    private void Commit(BurstSlot slot)
    {
        ref readonly ParticleBurst burst = ref Offered[slot.Offer];

        if (slot.Remove)
        {
            _bursts.Remove(burst.Key);
            return;
        }

        RunningBurst running = slot.Running;
        int taken = slot.Taken;
        double nextSleep = slot.NextSleep;
        ParticleControlPoint current = slot.Current;

        // **This frame's draw** — `CParticleCollection::Render` sets `m_flNextSleepTime` to at least the last simulation
        // time plus the no-draw time (particles.obj, Ghidra); a frame the caller could not place draws nothing. **Only
        // with no view to collate by**: given one, `Build` pushes it for what the view collated (finding 84, D1).
        if (!_collating && !burst.Undrawn)
        {
            nextSleep = Math.Max(nextSleep, slot.Wanted + slot.NoDraw);
        }

        if (slot.Rebuild)
        {
            ReplayTicks += slot.ReplayTicks;
            ReplaySteps += taken;
        }
        else
        {
            // A played frame's work, counted where it was written back (B243); a rebuild's is the replay counter's.
            Steps += taken - running.Stepped;
            Slept += slot.SleptHere ? 1 : 0;
        }

        // **A frame that steps nothing still reads its points**: `UpdateNewEffects` calls every effect's `Update`
        // — `UpdateControlPoint` — before it decides what to simulate (particlemgr.cpp:1841-1853), at any frame time.
        bool stillRead = running.StillRead;

        if (taken == running.Stepped)
        {
            running.Effect.SetControlPoint(0, current);

            // **The hitboxes once per still, not once per frame**: a still frame's pose is the last one's, so the
            // first still frame after a step reads them and the rest of the pause has nothing new to read.
            if (!stillRead && !burst.Undrawn && burst.Model?.Invoke() is { } still)
            {
                running.Effect.SetHitboxes(0, Carried(still.Boxes, current.At - still.PosedAt));
            }

            stillRead = true;
        }
        else
        {
            stillRead = false;
        }

        bool stopped = !slot.StopCleared && burst.StopTick is { } stopTick && _burstShown >= stopTick;

        // **A finished effect keeps only a marker**: its store is let go, its name and place in the census kept, so the
        // owner's continued offer neither rebuilds it nor holds its buffers.
        bool spent = running.Effect.Finished;

        _bursts[burst.Key] = running with
        {
            Effect = spent ? Spent : running.Effect,
            Stepped = taken, SimulatedTo = slot.ReachedTo, Stopped = stopped, StillRead = stillRead, Tick = burst.Tick,
            Name = burst.Definition.Name, Unoffered = false, CutShort = false,
            NextSleep = nextSleep, Dormant = slot.WasDormant, LastAt = current, StopCleared = slot.StopCleared,
        };
    }

    /// <summary>One burst's work for a frame: what the serial pass decided, and what its steps reached.</summary>
    /// <remarks>The offer is named by its index, not copied: a copy of the reference-holding struct is B592's write barrier.</remarks>
    private sealed class BurstSlot
    {
        public int Offer;
        public RunningBurst Running;
        public bool Remove;
        public bool Rebuild;
        public bool Advanced;
        public double Wanted;
        public double NoDraw;
        public double StopAt;
        public int Taken;
        public double ReachedTo;
        public double NextSleep;
        public bool WasDormant;
        public bool StopCleared;
        public bool SleptHere;
        public long ReplayTicks;
        public ParticleControlPoint Current;
        public (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)? Posed;

        /// <summary>A played burst's step ends and dormancy, asked serially before the workers run.</summary>
        public readonly List<(double Next, bool Dormant)> Ends = [];
    }

    /// <summary>This frame's burst units, reused; <see cref="_slotsUsed"/> are live and <see cref="_slotsFlushed"/> written.</summary>
    private readonly List<BurstSlot> _slots = [];

    /// <summary>The slots a flush hands the workers, reused.</summary>
    private readonly List<int> _toAdvance = [];

    private int _slotsUsed;
    private int _slotsFlushed;
    private float _flushSeconds;
    private Action<int>? _advanceSlot;

    /// <summary>
    /// An effect its caller no longer offers is its owner's emission stopping — `StopEmission`: no particle is born, the
    /// live ones age out where they are, and <see cref="Retire"/> takes it when it is empty.
    /// </summary>
    /// <remarks>
    /// **It was held frozen instead** — never stepped, never retired, and drawn — so a heal beam whose patient died, or
    /// any effect whose offer simply ended, stood in the air where its last particles were (owner, 2026-10-17: "drawing
    /// old beams in the air"). `CWeaponMedigun::UpdateEffects` stops the beam with `StopEmission` when the target changes
    /// or is gone (`tf_weapon_medigun.cpp:2369-2397`); every other owner that stops offering one does the same here.
    /// </remarks>
    /// <param name="now">The tick being shown, fraction included.</param>
    /// <param name="seconds">Seconds per tick.</param>
    /// <param name="seeked">Whether this frame is a seek's: what the target tick does not offer is history, not a fade.</param>
    /// <remarks>
    /// **A tick at a time**, as `CParticleMgr::UpdateNewEffects` simulates a frame, **on the points it was last offered**,
    /// which its last step already set: `StopEmission` leaves the effect in its owner's list so its points follow
    /// (`particle_property.cpp:251`), and an effect no longer offered has nothing newer to follow. A feed whose owner still
    /// moves keeps offering it, with a stop tick — the dispatches do. **A seek drops them**: arriving at a tick fresh, only
    /// what that tick offers is built, so a feed must offer a fading effect for as long as it shows (BurstEffects does).
    /// </remarks>
    private void FadeUnoffered(double now, float seconds, bool seeked)
    {
        _unoffered.Clear();

        foreach (long key in _bursts.Keys)
        {
            if (!_offered.Contains(key))
            {
                _unoffered.Add(key);
            }
        }

        foreach (long key in _unoffered)
        {
            if (seeked)
            {
                _bursts.Remove(key);
                continue;
            }

            RunningBurst running = _bursts[key];
            double wanted = now - running.Tick;
            double reached = running.SimulatedTo;

            // Nothing left to fade: the rest of the gap changes nothing, and Retire takes it.
            while (reached < wanted && !running.Effect.Empty)
            {
                double next = Math.Min(Math.Floor(reached) + 1d, wanted);

                running.Effect.Fade((float)((next - reached) * seconds));
                reached = next;
            }

            // Cut short when the offer ended before its own stop or its end: the first unoffered frame decides.
            bool cutShort = running.Unoffered ? running.CutShort : !running.Stopped && !running.Effect.Finished;

            _bursts[key] = running with { SimulatedTo = reached, Stopped = true, Unoffered = true, CutShort = cutShort };
        }
    }

    /// <summary>How many bursts run, by effect name, and how many of those their caller no longer offers.</summary>
    /// <remarks>An instrument for leaks: a name whose count climbs, or whose unoffered count never drains, outlives its owner.</remarks>
    /// <returns>One row per effect name: running, of those no longer offered, and finished markers still held.</returns>
    public IReadOnlyList<(string Name, int Live, int Unoffered, int Spent)> Census()
    {
        Dictionary<string, (int Live, int Unoffered, int Spent)> rows = new(StringComparer.Ordinal);

        foreach (RunningBurst running in _bursts.Values)
        {
            (int live, int unoffered, int spent) = rows.GetValueOrDefault(running.Name);

            // A finished effect its owner still offers is a marker: counted apart, since it holds nothing.
            int orphan = running.Unoffered ? 1 : 0;
            rows[running.Name] = running.Effect.Finished
                ? (live, unoffered, spent + 1)
                : (live + 1, unoffered + orphan, spent);
        }

        List<(string Name, int Live, int Unoffered, int Spent)> census = new(rows.Count);

        foreach ((string name, (int live, int unoffered, int spent)) in rows)
        {
            census.Add((name, live, unoffered, spent));
        }

        return census;
    }

    /// <summary>
    /// Where a played step from <paramref name="from"/> must end so its owner's dormancy is one state throughout: the
    /// step is asked at the tick it simulates into, so it ends at the last whole tick still in the first tick's state,
    /// or at <paramref name="to"/> when every tick of the span agrees.
    /// </summary>
    private static double DormancyChange(Func<int, bool> dormantAt, int fired, double from, double to)
    {
        int first = (int)Math.Floor(from) + 1;
        int last = (int)Math.Floor(to);
        bool state = dormantAt(fired + first);

        for (int at = first + 1; at <= last; at++)
        {
            if (dormantAt(fired + at) != state)
            {
                return at - 1;
            }
        }

        return to;
    }

    /// <summary>The keys no longer offered on the last call, reused.</summary>
    private readonly List<long> _unoffered = [];

    /// <summary>A model's boxes moved rigidly; the same list when they have not moved.</summary>
    private static IReadOnlyList<ModelHitbox> Carried(IReadOnlyList<ModelHitbox> boxes, Vector3 by)
    {
        if (by == Vector3.Zero)
        {
            return boxes;
        }

        ModelHitbox[] moved = new ModelHitbox[boxes.Count];

        for (int index = 0; index < moved.Length; index++)
        {
            moved[index] = boxes[index].Moved(by);
        }

        return moved;
    }

    /// <summary>Drops every burst that has run out of particles and will make no more, once it is no longer offered.</summary>
    /// <remarks>
    /// **`Finished` and not `Empty`, which is the difference between the two halves of the engine's own
    /// sentence**: *"IsFinished returns true when a system has no particles and won't be creating any more"*
    /// (`particles.h:1119`). A trail can be dropped on emptiness because `Update` has already stopped its
    /// emission by then; a burst has nothing stopping it from outside, so a system whose emitter has a start
    /// time is empty on its first step and must not be thrown away before it emits.
    ///
    /// **And not while the caller still offers it.** Dropped then, the next call finds it missing, builds it again
    /// and replays it from its own tick — every frame, for every finished tracer and explosion in the window (B415).
    /// </remarks>
    private void Retire()
    {
        _finished.Clear();

        foreach ((long key, RunningBurst running) in _bursts)
        {
            // A stopped effect whose emitter runs for ever is never FINISHED; stopped and empty lets it go.
            if ((running.Effect.Finished || (running.Stopped && running.Effect.Empty)) && !_offered.Contains(key))
            {
                _finished.Add(key);
            }
        }

        foreach (long key in _finished)
        {
            // **Only one that ended on its own definition is spent** (Fable review, item 1): one the offer cut short was
            // faded by us, not stopped by the engine, so offered again inside its life it is built — and past it, the age
            // skip in `Bursts` declines it.
            if (!_bursts[key].CutShort)
            {
                _spent.Add(key);
            }

            _bursts.Remove(key);
        }
    }

    /// <summary>Whether a played frame will do nothing more with this burst: finished, or retired as spent (finding 86).</summary>
    /// <param name="key">The burst's key.</param>
    /// <returns>True when offering it again on a played frame would only be skipped; a seek can still rebuild it.</returns>
    /// <remarks>`ProcessPSystem` sets the remove flag once `IsFinished` (particlemgr.cpp:1581-1584), and the engine spends
    /// nothing more on the effect; the caller asks this before building an offer.</remarks>
    public bool IsGone(long key) =>
        _spent.Contains(key) || (_bursts.TryGetValue(key, out RunningBurst running) && running.Effect.Finished);

    /// <summary>
    /// The bursts retired finished since the last seek: playing never builds one again, as the engine never re-creates an effect
    /// it removed (finding 84). A seek forgets them, since it rebuilds from the offer.
    /// </summary>
    private readonly HashSet<long> _spent = [];

    /// <summary>Each definition's <see cref="ParticleEffect.MaximumLife"/>, asked once: 470 bursts are offered a frame on f12.</summary>
    private readonly Dictionary<ParticleSystem, double> _lives = new(ReferenceEqualityComparer.Instance);

    /// <summary>The children <see cref="_lives"/> were bounded with: they are an input, so another set asks again.</summary>
    private IReadOnlyDictionary<string, ParticleSystem>? _livesOthers;

    private double LifeOf(ParticleSystem system, IReadOnlyDictionary<string, ParticleSystem>? others)
    {
        if (!ReferenceEquals(others, _livesOthers))
        {
            _lives.Clear();
            _livesOthers = others;
        }

        if (!_lives.TryGetValue(system, out double life))
        {
            // The same sheets the effect itself reads: `Lifetime From Sequence` is bounded by them (rocketjump_flame).
            _lives[system] = life = ParticleEffect.MaximumLife(system, others, Sheets);
        }

        return life;
    }

    /// <summary>The keys offered on the last call, reused.</summary>
    private readonly HashSet<long> _offered = [];

    /// <summary>The keys being retired, reused.</summary>
    private readonly List<long> _finished = [];

    /// <summary>One running one-shot: how many steps it took, and to how many ticks after it fired, fraction included.</summary>
    private readonly record struct RunningBurst(
        ParticleEffect Effect,
        int Stepped,
        double SimulatedTo,
        bool Stopped,
        bool StillRead = false,
        int Tick = 0,
        string Name = "",
        bool Unoffered = false)
    {
        /// <summary>`m_flNextSleepTime`, in ticks after the burst fired.</summary>
        public double NextSleep { get; init; }

        /// <summary>Whether the last step taken was on a dormant tick.</summary>
        public bool Dormant { get; init; }

        /// <summary>Whether a wake's `StartEmission` has cleared the stop that landed while it was dormant.</summary>
        public bool StopCleared { get; init; }

        /// <summary>The control point it was last placed at.</summary>
        public ParticleControlPoint LastAt { get; init; }

        /// <summary>
        /// Whether the offer ended while it was still running on its own definition, so its fade is ours and not its end:
        /// retired, it may be offered again and built (Fable review, item 1).
        /// </summary>
        public bool CutShort { get; init; }
    }

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
    ///
    /// **Given the views, only what they collate is drawn, and only drawing keeps an effect awake** (finding 84, D1): each
    /// effect is filed by its box in a leaf system over the same tree and collated with the same leaf lists and frustums as
    /// the entities; a collated one is drawn and has its sleep time pushed on, as `CParticleCollection::Render` does; the
    /// rest sleep once past it. With no view (no device, a test) every effect is drawn, as before.
    /// </remarks>
    /// <param name="view">The views this frame collated by; null to draw every effect.</param>
    public IReadOnlyList<ParticleBatch> Build(
        Vector3 eye,
        Vector3 right,
        Vector3 up,
        IReadOnlyDictionary<string, ParticleMaterial> materials,
        CollationView? view = null)
    {
        ArgumentNullException.ThrowIfNull(materials);

        foreach (List<DetailSpriteVertex> corners in _byMaterial.Values)
        {
            corners.Clear();
        }

        // **The frame's, not the instance's** (lead's review): whether a view decided this frame's draw, which the next
        // frame's simulation reads, as the engine's simulate reads what the last frame's render pushed.
        _collating = view is not null;

        if (view is not null)
        {
            CollateEffects(view);
        }

        foreach ((int entity, ParticleEffect effect) in _running)
        {
            if (view is not null && !_drawnKeys.Contains(TrailKey(entity)))
            {
                continue;
            }

            if (view is not null)
            {
                // `Render`: the sleep time at least this frame's simulation time plus the no-draw time.
                _trailSleep[entity] = Math.Max(
                    _trailSleep.GetValueOrDefault(entity), _tick + (ParticleEffect.SleepAfter(effect.System) / _seconds));
            }

            Gathered += effect.Empty ? 0 : 1;
            Gather(effect, eye, right, up, materials);
        }

        _pushed.Clear();

        foreach ((long key, RunningBurst running) in _bursts)
        {
            if (view is not null && !_drawnKeys.Contains(key))
            {
                continue;
            }

            if (view is not null)
            {
                _pushed.Add(key);
            }

            Gathered += running.Effect.Empty ? 0 : 1;
            Gather(running.Effect, eye, right, up, materials);
        }

        foreach (long key in _pushed)
        {
            RunningBurst running = _bursts[key];
            double noDraw = ParticleEffect.SleepAfter(running.Effect.System) / _seconds;

            _bursts[key] = running with { NextSleep = Math.Max(running.NextSleep, (_burstNow - running.Tick) + noDraw) };
        }

        return Batches(materials);
    }

    /// <summary>
    /// Builds the quads of effects someone else owns — a model panel's `m_particleList`, rendered by
    /// `CBaseModelPanel::PostPaint3D` (basemodel_panel.cpp:904-912) — the way <see cref="Build(Vector3, Vector3, Vector3, IReadOnlyDictionary{string, ParticleMaterial}, CollationView)"/>
    /// builds this scene's.
    /// </summary>
    /// <param name="effects">The effects to draw.</param>
    /// <param name="eye">Where that camera is.</param>
    /// <param name="right">That camera's right vector.</param>
    /// <param name="up">That camera's up vector.</param>
    /// <param name="materials">Every particle material, by its normalised name.</param>
    /// <returns>One batch per material, reused between frames.</returns>
    public IReadOnlyList<ParticleBatch> Build(
        IEnumerable<ParticleEffect> effects,
        Vector3 eye,
        Vector3 right,
        Vector3 up,
        IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(materials);

        foreach (List<DetailSpriteVertex> corners in _byMaterial.Values)
        {
            corners.Clear();
        }

        foreach (ParticleEffect effect in effects)
        {
            Gather(effect, eye, right, up, materials);
        }

        return Batches(materials);
    }

    /// <summary>Files every live effect by its box and collates the views' leaves — what the scene's `Build` draws.</summary>
    /// <remarks>
    /// `CNewParticleEffect` is a renderable of the one leaf system (`DetectChanges` re-links it when its box moves,
    /// particles_new.cpp:370-404), collated as the entities are (clientleafsystem.cpp:1574-1716): a listed leaf, then the
    /// frustum. Ours files it by its box every frame and re-links only on a change, as the entities' does.
    /// </remarks>
    private void CollateEffects(CollationView view)
    {
        if (_effectLeaves is null || !ReferenceEquals(_effectTree, view.Tree))
        {
            _effectTree = view.Tree;
            _effectLeaves = view.Tree is { IsEmpty: false } tree
                ? new ClientLeafSystem(tree.LeafCount, tree.EnumerateLeavesInBox)
                : new ClientLeafSystem(1, static (_, _, into) => into.Add(0));
            _everyEffectLeaf = new int[view.Tree is { IsEmpty: false } listed ? listed.LeafCount : 1];

            for (int leaf = 0; leaf < _everyEffectLeaf.Length; leaf++)
            {
                _everyEffectLeaf[leaf] = leaf;
            }

            _handleOfEffect.Clear();
        }

        _filedKeys.Clear();

        foreach ((int entity, ParticleEffect effect) in _running)
        {
            FileEffect(TrailKey(entity), effect);
        }

        foreach ((long key, RunningBurst running) in _bursts)
        {
            if (!ReferenceEquals(running.Effect, Spent))
            {
                FileEffect(key, running.Effect);
            }
        }

        _staleKeys.Clear();

        foreach (long key in _handleOfEffect.Keys)
        {
            if (!_filedKeys.Contains(key))
            {
                _staleKeys.Add(key);
            }
        }

        foreach (long key in _staleKeys)
        {
            _effectLeaves.RemoveRenderable(_handleOfEffect[key]);
            _handleOfEffect.Remove(key);
        }

        _effectLeaves.PreRender();
        _effectLeaves.BuildRenderablesList(view.Views.MainLeaves ?? _everyEffectLeaf, view.Frustum, OpaqueEffect, _effectsMain);

        _drawnKeys.Clear();
        AddDrawn(_effectsMain);

        if (view.Views.SkyLeaves is { Count: > 0 } sky)
        {
            _effectLeaves.BuildRenderablesList(sky, view.Views.SkyFrustum, OpaqueEffect, _effectsSky, includeUnplaced: false);
            AddDrawn(_effectsSky);
        }
    }

    private void FileEffect(long key, ParticleEffect effect)
    {
        (Vector3 min, Vector3 max) = effect.Bounds();

        // **An empty effect's box is control point 0 alone, a point, and the engine culls it** (Fable review, item 4). This
        // leaf system holds a box with no volume as unplaced — never culled — so the point is padded to one that links.
        min = Vector3.Min(min, max - PointPad);
        max = Vector3.Max(max, min + (2f * PointPad));

        (float, float, float, float, float, float) box = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);

        if (_handleOfEffect.TryGetValue(key, out int handle))
        {
            _effectLeaves!.RenderableChanged(handle, box);
        }
        else
        {
            handle = _effectLeaves!.AddRenderable(box);
            _handleOfEffect[key] = handle;
        }

        // A freed handle is reused, so the key it answers for is written every time.
        _keyOfHandle[handle] = key;
        _filedKeys.Add(key);
    }

    private void AddDrawn(CollatedRenderables collated)
    {
        foreach ((int handle, _, _) in collated.Entries)
        {
            _drawnKeys.Add(_keyOfHandle[handle]);
        }
    }

    /// <summary>Half the least extent an effect's filed box has on each axis, in world units.</summary>
    private static readonly Vector3 PointPad = new(0.5f);

    /// <summary>A trail's key among the bursts': its entity in the top half, which no burst key reaches.</summary>
    private static long TrailKey(int entity) => (1L << 62) | (uint)entity;

    private static readonly Func<int, LeafRenderGroup> OpaqueEffect = static _ => LeafRenderGroup.Opaque;

    /// <summary>Whether the last frame's draw was decided by a view: then only that draw pushed an effect's sleep time.</summary>
    private bool _collating;

    /// <summary>The last frame's simulation time, in ticks, and a tick's length — what a draw pushes the sleep time from.</summary>
    private double _burstNow;

    private float _seconds = 1f / 66f;

    /// <summary>Each trail's sleep time, in ticks, as a burst's <see cref="RunningBurst.NextSleep"/> is.</summary>
    private readonly Dictionary<int, double> _trailSleep = [];

    /// <summary>The trails whose projectile is gone: a projectile that appears on one of these indices is a new one.</summary>
    private readonly HashSet<int> _orphans = [];

    private ClientLeafSystem? _effectLeaves;
    private BspLeafTree? _effectTree;
    private int[] _everyEffectLeaf = [0];
    private readonly Dictionary<long, int> _handleOfEffect = [];
    private readonly Dictionary<int, long> _keyOfHandle = [];
    private readonly HashSet<long> _filedKeys = [];
    private readonly List<long> _staleKeys = [];
    private readonly HashSet<long> _drawnKeys = [];
    private readonly List<long> _pushed = [];
    private readonly CollatedRenderables _effectsMain = new();
    private readonly CollatedRenderables _effectsSky = new();

    /// <summary>This frame's non-empty material groups as batches.</summary>
    private List<ParticleBatch> Batches(IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
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
                        renderer.Number("animation_fit_lifetime", 0d) != 0d,
                        eye,
                        material.Size,
                        material.TintIsGamma);
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
        _spent.Clear();
        _lives.Clear();
        _burstTick = 0;

        // And the clocks the creation-frame rule reads (B557 row 6): a new demo's first frame is an opening, not a played one.
        _tick = double.NaN;
        _burstShown = double.NaN;
        _playedFrom.Clear();

        // And the effects' sleep and filing (finding 84, D1): a new map's tree files them afresh.
        _trailSleep.Clear();
        _orphans.Clear();
        _handleOfEffect.Clear();
        _keyOfHandle.Clear();
        _effectLeaves = null;
        _effectTree = null;
        _collating = false;
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
/// <param name="End">
/// Control point 1, or null when the effect has none. A tracer's is where its bullet stopped: `ParticleEffectCallback`
/// sets control point 0 to the muzzle and 1 to the end before the effect first simulates. It is set again on every
/// call, so a point that follows an entity — a medigun beam's target — follows it.
/// </param>
/// <param name="StopTick">
/// The tick emission stops — `ParticleProp()->StopEmission` — or null for none: from then on the effect only fades.
/// </param>
/// <param name="History">
/// Where both points were on an earlier tick, for an effect that follows entities; null for one that stays put. Asked
/// only for the ticks a catch-up steps before the one being shown (B527).
/// </param>
public readonly record struct ParticleBurst(
    long Key,
    ParticleSystem Definition,
    ParticleControlPoint At,
    int Tick,
    ParticleControlPoint? End = null,
    int? StopTick = null,
    Func<int, (ParticleControlPoint At, ParticleControlPoint? End)>? History = null)
{
    /// <summary>
    /// The posed hitboxes of the entity control point 0 follows, with where that point stood when they were posed; null
    /// for an effect on no model, and asked only when the effect is about to be stepped (B538).
    /// </summary>
    /// <remarks>
    /// `UpdateHitBoxInfo` reads the entity's bones before a simulation. A step to another place — an earlier tick of a
    /// catch-up — takes the same boxes there, carried rigidly: *interpolated*, as <see cref="FollowedPoint"/> is, since
    /// posing a player for every tick of a replay costs a skeleton each.
    /// </remarks>
    public Func<(IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)?>? Model { get; init; }

    /// <summary>
    /// Whether the owner was dormant at a tick, or null for an effect with no owner. `CParticleProperty::OwnerSetDormantTo`
    /// sets `SetDormant` on every effect it owns (particle_property.cpp:465-475), and in the particles library
    /// `CParticleCollection::SetDormant(true)` is `StopEmission(false, false, false)` and `SetDormant(false)` is
    /// `StartEmission(false)`; `Simulate` never reads the flag (particles.obj, Ghidra, 2026-10-18). So a dormant tick is
    /// simulated and drawn with no births, and the wake emits again. Asked for every tick a replay steps.
    /// </summary>
    public Func<int, bool>? DormantAt { get; init; }

    /// <summary>
    /// Whether nothing draws the effect this frame — its owner is not posed, so it cannot be placed. An undrawn frame
    /// does not push `m_flNextSleepTime` on (`CParticleCollection::Render` does, particles.obj, Ghidra), so after the
    /// definition's `time to sleep when not drawn` the effect sleeps while it has live particles
    /// (`BuildParticleSimList`, particlemgr.cpp:1775-1776). Until then it simulates on the points it last had. One
    /// never built is not built undrawn: there is no point to replay it on.
    /// </summary>
    public bool Undrawn { get; init; }
}
