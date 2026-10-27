using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's one-shot particle effects are stepped against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Game">The game's content, or null without an install.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Viewing">The frame's camera.</param>
/// <param name="Players">The players as the frame drew them — interpolated while playing, last received while paused.</param>
/// <param name="Source">Where the frame's players came from, for a bullet's hull test.</param>
/// <param name="ShownTick">The moment the frame showed, fraction included.</param>
/// <param name="Playing">Whether playback is running.</param>
/// <param name="FirstPerson">Whether the view is in a player's eyes.</param>
/// <param name="Followed">The entity the camera follows, or null.</param>
/// <param name="DrawViewmodel">`r_drawviewmodel`.</param>
/// <param name="HealTargetMarker">`hud_medichealtargetmarker`.</param>
/// <param name="Scripts">The soundscripts the game loaded, or null.</param>
/// <param name="Silent">Whether this pass is a seek's replay, which nobody hears.</param>
/// <param name="Seeked">Whether this frame landed by a seek, the frame's one skip decision, a transport jump (`FrameSkip`) — what rebuilds a burst (D221).</param>
public readonly record struct BurstFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    GameContent? Game,
    int Tick,
    FreeCamera Viewing,
    IReadOnlyList<ScenePlayer> Players,
    IMomentSource? Source,
    double ShownTick,
    bool Playing,
    bool FirstPerson,
    int? Followed,
    bool DrawViewmodel,
    bool HealTargetMarker,
    SoundScriptCatalog? Scripts,
    bool Silent,
    bool Seeked = false);

/// <summary>What a frame's map and game hold that the bursts read, as data (D214): a test supplies it by hand.</summary>
/// <param name="Systems">The map's particle systems by name.</param>
/// <param name="Tracers">Every tracer the map's shots draw.</param>
/// <param name="Impacts">Every bullet the map stops.</param>
/// <param name="Level">The map's collision.</param>
/// <param name="Read">Opens a path out of the game's content; null without a game.</param>
/// <param name="Items">The item schema.</param>
/// <param name="Scripts">The soundscripts the game loaded.</param>
internal readonly record struct BurstWorld(
    IReadOnlyDictionary<string, ParticleSystem>? Systems,
    IReadOnlyList<ShotTracer>? Tracers,
    IReadOnlyList<ShotImpact>? Impacts,
    MapLevel? Level,
    Func<string, byte[]?>? Read,
    ItemSchema? Items,
    IReadOnlyDictionary<string, SoundScriptEntry>? Scripts);

/// <summary>
/// Every one-shot particle effect a demo starts — explosions, tracers, blood, splashes, muzzle flashes, dispatched
/// effects, medigun beams and overheal pluses — offered to <see cref="ParticleEffects.Bursts"/> each frame (B415).
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 1), where it was `StepExplosions` and the fourteen methods
/// it called: the same arithmetic, the same order, the same seeds. The view's side is the constructor, one
/// <see cref="Step"/> a frame, and <see cref="ClearMap"/>.
///
/// **A presenter (D90): it decides WHICH effects are shown and where their control points are.** Stepping them is
/// <see cref="ParticleEffects"/>, which is Scene's; turning them into quads is the same class's `Build`.
///
/// **It emits no sound itself** (D92: presenters do not reach sideways). A tracer's near-miss whiz is returned from
/// <see cref="Step"/> for the caller to hand to the sound presenter, in the order it was decided.
/// </remarks>
public sealed class BurstEffects
{
    /// <summary>How long a blast's effect is given before the window stops offering it.</summary>
    /// <remarks>
    /// **A bound on the replay, not a lifetime.** A burst runs until its own system says it is finished
    /// (`ParticleEffect.Finished`), and this only decides how far back the viewer looks when it arrives at a tick
    /// — which is what a seek costs. Three seconds at 66 ticks; `ExplosionCore_MidAir`'s own emitters run well
    /// inside that, so nothing is cut short. The same shape as the corpse fade capping a seek's replay
    /// (`docs/memory/a-parity-rule-may-bound-the-cost.md`).
    /// </remarks>
    private const int ExplosionWindowTicks = 200;

    /// <summary>How long a tracer is offered: its longest trip, 8192 units at 5000 a second, is 1.64 s.</summary>
    private const int TracerWindowTicks = 120;

    /// <summary>Tracer keys sit above every explosion's, which are their indices in the feed.</summary>
    private const long TracerKeys = 1L << 32;

    /// <summary>Blood keys sit above the tracers': two bursts per event, its impact and its spray.</summary>
    private const long BloodKeys = 2L << 32;

    /// <summary>Splash keys sit above the heal beams': one per bullet into water.</summary>
    private const long SplashKeys = 8L << 32;

    /// <summary>How long a blood event is offered — a second, well past `blood_spray_red_01`'s own life.</summary>
    private const int BloodWindowTicks = 66;

    /// <summary>Muzzle flash keys sit above the blood's.</summary>
    private const long MuzzleFlashKeys = 3L << 32;

    /// <summary>How long a muzzle flash is offered — a second, far past `muzzle_sentry`'s own life.</summary>
    private const int MuzzleFlashWindowTicks = 66;

    /// <summary>Medigun beam keys sit above the TF particle effects'.</summary>
    private const long HealBeamKeys = 7L << 32;

    /// <summary>How long a stopped beam is still offered, fading — two seconds, past its particles' lives.</summary>
    private const int HealBeamFadeTicks = 132;

    /// <summary>Overheal keys sit above the splashes'.</summary>
    private const long OverhealKeys = 9L << 32;

    /// <summary>Weapon muzzle flash keys sit above the dispatched particles'; two a flash, the muzzle and the backblast.</summary>
    private const long WeaponFlashKeys = 5L << 32;

    /// <summary>How long a weapon's flash is offered and followed — a second.</summary>
    private const int WeaponFlashWindowTicks = 66;

    /// <summary>Dispatched particle keys sit above the muzzle flashes'.</summary>
    private const long ParticleDispatchKeys = 4L << 32;

    /// <summary>How long a dispatched particle effect is offered and followed — ten seconds.</summary>
    /// <remarks>Nothing on the client stops `rocketjump_smoke`; it ends when its own definition does.</remarks>
    private const int ParticleDispatchWindowTicks = 660;

    /// <summary>`PARTICLE_DISPATCH_FROM_ENTITY`.</summary>
    private const int DispatchFromEntity = 1 << 0;

    /// <summary>`PATTACH_CUSTOMORIGIN`, `PATTACH_POINT` and `PATTACH_POINT_FOLLOW`.</summary>
    private const int AttachCustomOrigin = 2, AttachPoint = 3, AttachPointFollow = 4;

    /// <summary>`CTETFParticleEffect` keys sit above the weapon flashes'.</summary>
    private const long TfParticleKeys = 6L << 32;

    private readonly ParticleEffects _particles;

    private readonly IPosedEntities _models;

    private readonly BulletStrikes _strikes;

    private readonly ILogger _renderLog;

    /// <summary>The frame being stepped, for the methods <see cref="Step"/> calls.</summary>
    private BurstFrame _frame;

    /// <summary>What that frame's map and game hold.</summary>
    private BurstWorld _world;

    /// <summary>The game <see cref="_read"/> was made for.</summary>
    private GameContent? _readOf;

    /// <summary>That game's file reader, or null without one.</summary>
    private Func<string, byte[]?>? _read;

    /// <summary>Every blast whose effect should be running this tick, reused to avoid a per-frame allocation.</summary>
    private readonly List<(int Index, SceneExplosion Blast)> _blastsNow = [];

    /// <summary>The same, as bursts.</summary>
    private readonly List<ParticleBurst> _burstsNow = [];

    /// <summary>The effect chooser, built once the game's content is known.</summary>
    private ExplosionEffects? _explosions;

    /// <summary>Every tracer in the window this tick, reused.</summary>
    private readonly List<(int Index, ShotTracer Tracer)> _tracersNow = [];

    /// <summary>Where each tracer was drawn from and to, by its index in the map's list, fixed at first sight.</summary>
    private readonly Dictionary<int, ((float X, float Y, float Z) From, (float X, float Y, float Z) To)> _tracerStarts = [];

    /// <summary>`g_BulletWhiz.m_nextWhizTime`, in demo seconds.</summary>
    private double _nextWhizSeconds;

    /// <summary>The demo time a whiz was last asked about, so a seek back can reset the wait.</summary>
    private double _lastWhizAsked;

    /// <summary>The whizzes this frame's tracers made, handed back by <see cref="Step"/>; reused.</summary>
    private readonly List<SceneSound> _whizzes = [];

    /// <summary>The impacts in the splashes' window this tick, reused.</summary>
    private readonly List<(int Index, ShotImpact Impact)> _splashesNow = [];

    /// <summary>Every blood event in the window this tick, reused.</summary>
    private readonly List<(int Index, SceneBlood Blood)> _bloodNow = [];

    /// <summary>Each blood event's two bursts, turned against the view it was first seen under.</summary>
    private readonly Dictionary<int, (BloodBurst Impact, BloodBurst Spray)> _bloodBursts = [];

    /// <summary>Every effect dispatch in the window this tick, reused.</summary>
    private readonly List<(int Index, SceneEffectDispatch Dispatch)> _dispatchesNow = [];

    /// <summary>The tick each beam was first stepped from, fixed at first sight so a seek does not replay minutes of it.</summary>
    private readonly Dictionary<int, int> _healBeamFrom = [];

    /// <summary>The tick <see cref="AddHealBeams(DemoTimeline, int, IReadOnlyDictionary{string, ParticleSystem})"/> last ran at, so a backward seek can forget <see cref="_healBeamFrom"/>.</summary>
    private int _healBeamTick = int.MinValue;

    /// <summary>Every player's position this tick by entity, as the frame drew him.</summary>
    private readonly Dictionary<int, Vector3> _playersNow = [];

    /// <summary>Every player's yaw this tick by entity, filled with <see cref="_playersNow"/>.</summary>
    private readonly Dictionary<int, float> _yawsNow = [];

    /// <summary><see cref="PlayedAt"/>'s samples for the tick being shown, forgotten when it changes.</summary>
    private readonly Dictionary<int, Dictionary<int, ScenePlayer>> _playedAt = [];

    /// <summary>The tick <see cref="_playedAt"/> was filled for.</summary>
    private int _playedTick = int.MinValue;

    /// <summary>One sample being read, reused.</summary>
    private readonly List<ScenePlayer> _playedScratch = [];

    /// <summary>Which players show overheal pluses, and since when.</summary>
    private readonly OverhealEffects _overheals = new();

    /// <summary>Every weapon flash in the window this tick, reused.</summary>
    private readonly List<(int Index, SceneMuzzleFlash Flash)> _weaponFlashesNow = [];

    /// <summary>Resolves a weapon's flash from its item and script, built on first use.</summary>
    private WeaponMuzzleFlashes? _weaponMuzzles;

    /// <summary>The first-person flashes already logged, by their place in the demo's list.</summary>
    private readonly HashSet<int> _viewmodelFlashesLogged = [];

    /// <summary>Every effect dispatch in the particle window this tick, reused.</summary>
    private readonly List<(int Index, SceneEffectDispatch Dispatch)> _particleDispatchesNow = [];

    /// <summary>The dispatches already logged by key, so each is reported once unplaced and once placed.</summary>
    private readonly HashSet<long> _loggedDispatches = [];

    /// <summary>Where each non-following dispatched effect was created by key, fixed at first sight.</summary>
    private readonly Dictionary<long, ParticleControlPoint> _particleDispatchPoints = [];

    /// <summary>Every `CTETFParticleEffect` in the particle window this tick, reused.</summary>
    private readonly List<(int Index, SceneEffectDispatch Dispatch)> _tfParticlesNow = [];

    /// <summary>The timeline <see cref="_stops"/> was computed for.</summary>
    private DemoTimeline? _stopsOf;

    /// <summary>Each `"ParticleEffect"` dispatch's stop tick (<see cref="DispatchStops"/>), by its index.</summary>
    private int?[] _stops = [];

    /// <summary>Each owned dispatch's dormancy question by its index, made on first offer for <see cref="_stopsOf"/>.</summary>
    private Func<int, bool>?[] _dormantAt = [];

    /// <summary>The `"ParticleEffect"` dispatches made from an entity, by index — each runs until Valve ends it.</summary>
    private readonly List<int> _owned = [];

    /// <summary>The tick the bursts were last offered at, so a clock gone back offers the finished ones again.</summary>
    private int _offeredTick = int.MinValue;

    /// <summary>Whether this frame was played rather than landed: set once per <see cref="StepOver"/>, before any offer.</summary>
    private bool _played;

    /// <summary>
    /// Offers a let-go one-shot (a muzzle flash, an entity-less or TF dispatch) every played frame, as before B577's
    /// remainder: the reference a state-equivalence test compares with.
    /// </summary>
    internal bool OfferGoneBursts { get; set; }

    /// <summary>Whether a played frame may leave this key out: <see cref="ParticleEffects.IsGone"/> and nothing to rebuild.</summary>
    /// <remarks>
    /// The engine creates a dispatched or muzzle effect once, on arrival (`C_TEEffectDispatch::PostDataUpdate`,
    /// c_te_effect_dispatch.cpp:146 → `DispatchEffectToCallback`, :71), and removes it once `IsFinished`
    /// (particlemgr.cpp:1581-1584); nothing reads it again. Offered after that, ours was found finished and skipped.
    /// </remarks>
    private bool LetGo(long key) => _played && !OfferGoneBursts && _particles.IsGone(key);

    /// <summary>The last burst offered under each one-shot key, for <see cref="Reoffer"/>; for <see cref="_lastOffersOf"/>.</summary>
    /// <remarks>ponytail: grows by one entry per one-shot the demo fires and is cleared per timeline; prune by window if a
    /// long demo makes it matter.</remarks>
    private readonly Dictionary<long, ParticleBurst> _lastOffers = [];

    /// <summary>The timeline <see cref="_lastOffers"/> holds keys of.</summary>
    private DemoTimeline? _lastOffersOf;

    /// <summary>Whether a key is let go and has a last offer to repeat.</summary>
    private bool Gone(long key) => LetGo(key) && _lastOffers.ContainsKey(key);

    /// <summary>Offers a let-go key's last burst again instead of building it; false when it is not let go.</summary>
    /// <remarks>
    /// **Offered, not left out, so nothing but the work changes**: `ParticleEffects.Bursts` keeps a finished burst that
    /// is still offered and does not step it, never reading its point, so the census and the order of its dictionary
    /// stay exactly what offering a freshly built burst gave. Left out, it was retired sooner and the slot it freed
    /// moved later effects in the draw order. The caller decides that a fresh build would have offered it at all.
    /// </remarks>
    private bool Reoffer(long key)
    {
        if (!LetGo(key) || !_lastOffers.TryGetValue(key, out ParticleBurst last))
        {
            return false;
        }

        _burstsNow.Add(last);

        return true;
    }

    /// <summary>Offers a one-shot's burst and remembers it for <see cref="Reoffer"/>.</summary>
    private void Offer(in ParticleBurst burst)
    {
        _burstsNow.Add(burst);
        _lastOffers[burst.Key] = burst;
    }

    /// <summary>
    /// The owned dispatches fired and not yet let go, in fire order (B577): what the walk over <see cref="_owned"/> would
    /// still offer. A played frame only appends what fired and drops what is gone or past its fade, both for good.
    /// </summary>
    private readonly List<int> _liveOwned = [];

    /// <summary>The next index into <see cref="_owned"/> not yet fired into <see cref="_liveOwned"/>.</summary>
    private int _ownedFired;

    /// <summary>Walks every owned dispatch from the first each frame, as before B577: the reference a test compares with.</summary>
    internal bool WalkEveryOwnedDispatch { get; set; }

    /// <summary>The view's own: the model set it draws with.</summary>
    /// <param name="particles">The effects these bursts are stepped in.</param>
    /// <param name="models">The posed entities, for attachments and hitboxes.</param>
    /// <param name="strikes">Where a tracer's bullet stops.</param>
    /// <param name="render">The render log.</param>
    public BurstEffects(ParticleEffects particles, EntityModelSet models, BulletStrikes strikes, ILogger render)
        : this(particles, (IPosedEntities)models, strikes, render)
    {
    }

    /// <summary>Over any posed entities.</summary>
    /// <param name="particles">The effects these bursts are stepped in.</param>
    /// <param name="models">The posed entities, for attachments and hitboxes.</param>
    /// <param name="strikes">Where a tracer's bullet stops.</param>
    /// <param name="render">The render log.</param>
    public BurstEffects(ParticleEffects particles, IPosedEntities models, BulletStrikes strikes, ILogger render)
    {
        _particles = particles;
        _models = models;
        _strikes = strikes;
        _renderLog = render;
    }

    /// <summary>The bursts the last <see cref="Step"/> offered, for an instrument that names them.</summary>
    public IReadOnlyList<ParticleBurst> Bursts => _burstsNow;

    /// <summary>Forgets what was fixed against the map that is going: tracer paths, blood, dispatch points, beam starts.</summary>
    public void ClearMap()
    {
        // Indexed by the map's own tracer list, which goes with it.
        _tracerStarts.Clear();
        _bloodBursts.Clear();
        _particleDispatchPoints.Clear();
        _healBeamFrom.Clear();
        _lastOffers.Clear();
    }

    /// <summary>Starts and steps a one-shot effect for every explosion in the window (B415), and every other burst.</summary>
    /// <param name="frame">What this frame is stepped against.</param>
    /// <returns>The near-miss whizzes this frame's tracers made, to be emitted in this order; valid until the next call.</returns>
    /// <remarks>
    /// **`CTETFExplosion` is a temp entity, so there is no entity to follow** — the blast happens at a point and
    /// its effect runs on its own. `ParticleEffects.Bursts` steps each one by the frame's demo time and rebuilds it on a
    /// seek, so it is the same after any seek to a moment (D221).
    ///
    /// **A system this map did not upload a texture for draws nothing rather than the chequer**, which is what
    /// `Build` already does for an unresolved particle material: a missing effect is a missing effect, not a
    /// missing surface.
    /// </remarks>
    public IReadOnlyList<SceneSound> Step(in BurstFrame frame)
    {
        // The game's reader as one delegate, made when the game changes and not every frame.
        if (!ReferenceEquals(frame.Game, _readOf))
        {
            _readOf = frame.Game;
            _read = frame.Game is { } content ? content.Archives.Read : null;

            // The game's own, its scripts read before play by `LevelSystems.OpenGame` (B595 part 6).
            _weaponMuzzles = frame.Game?.MuzzleFlashes;
        }

        return StepOver(
            frame,
            new BurstWorld(
                frame.Loaded?.Assets?.ParticleSystemsByName,
                frame.Loaded?.Tracers,
                frame.Loaded?.Impacts,
                frame.Loaded?.Level,
                _read,
                frame.Game?.Weapons.Items,
                frame.Scripts?.Entries));
    }

    /// <summary>The rate line's parts under `--measure`, else null.</summary>
    internal FrameParts? Parts { get; set; }

    private long Part(string name, long since) => FrameParts.Lap(Parts, name, since);

    /// <summary>Frames that searched the timeline for what to offer, ever (finding 82).</summary>
    internal long Searches { get; private set; }

    /// <summary>Time spent searching the timeline for this frame's bursts, in stopwatch ticks (finding 82).</summary>
    internal long SearchTicks { get; private set; }

    /// <summary>Time spent starting and stepping the bursts found, in stopwatch ticks (finding 82).</summary>
    internal long BurstTicks { get; private set; }

    /// <summary>The weapon-script precache's duration and its end, in stopwatch ticks, carried from the precache itself; zero until one ran (B595 part 6).</summary>
    internal long PrecacheTicks => _weaponMuzzles?.PrecacheTicks ?? 0;

    /// <inheritdoc cref="PrecacheTicks"/>
    internal long PrecachedAt => _weaponMuzzles?.PrecachedAt ?? 0;

    /// <summary>The same, over what the frame's map and game hold as data, which a test can supply (D214).</summary>
    /// <param name="frame">What this frame is stepped against; its map, game and scripts are not read here.</param>
    /// <param name="world">What they hold.</param>
    /// <returns>The near-miss whizzes, as <see cref="Step"/> returns them.</returns>
    internal IReadOnlyList<SceneSound> StepOver(in BurstFrame frame, in BurstWorld world)
    {
        _frame = frame;
        _world = world;
        _whizzes.Clear();

        DemoTimeline? timeline = frame.Timeline;
        Func<string, byte[]?>? read = world.Read;
        IReadOnlyDictionary<string, ParticleSystem>? systems = world.Systems;

        if (timeline is null || read is null || systems is null || systems.Count <= 0)
        {
            return _whizzes;
        }

        _explosions ??= new ExplosionEffects(read);

        int tick = frame.Tick;
        FreeCamera viewing = frame.Viewing;

        // **Timed, both halves, where they run** (finding 82, B243): finding what the timeline offers this frame, and
        // stepping the bursts it found.
        long searchingAt = System.Diagnostics.Stopwatch.GetTimestamp();

        timeline.Explosions.Between(tick - ExplosionWindowTicks, tick, _blastsNow);

        _burstsNow.Clear();

        foreach ((int index, SceneExplosion blast) in _blastsNow)
        {
            // `bIsPlayer` was resolved against the entity table when the blast arrived, which is when the client asks.
            string name = _explosions.NameFor(blast, blast.StruckPlayer);

            if (!systems.TryGetValue(name, out ParticleSystem? definition))
            {
                continue;
            }

            (float pitch, float yaw, float roll) = ExplosionEffects.AnglesFor(blast);

            (float fx, float fy, float fz) = AngleVectors.Forward(pitch, yaw);
            (float px, float py, float pz) = AngleVectors.Right(pitch, yaw, roll);
            (float qx, float qy, float qz) = AngleVectors.Up(pitch, yaw, roll);

            _burstsNow.Add(new ParticleBurst(
                index,
                definition,
                new ParticleControlPoint(
                    new Vector3(blast.X, blast.Y, blast.Z),
                    new Vector3(fx, fy, fz),
                    new Vector3(px, py, pz),
                    new Vector3(qx, qy, qz)),
                blast.Tick));
        }

        // Each offerer timed where it runs, under `camera particles/bursts`; a `/` in a part's name nests it inside the
        // part before it, so nested parts are not added together (finding 86, B243).
        // A played frame, not a landed one: a seek, or a clock gone back, rebuilds from the offer (D221), so only a played
        // frame may leave out what `ParticleEffects` already let go (finding 86).
        _played = !frame.Seeked && tick >= _offeredTick;
        _offeredTick = tick;

        if (!ReferenceEquals(timeline, _lastOffersOf))
        {
            _lastOffersOf = timeline;
            _lastOffers.Clear();
        }

        long partAt = Part("camera particles/bursts/explosions", searchingAt);
        AddTracers(tick, systems, new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z));
        partAt = Part("camera particles/bursts/tracers", partAt);
        AddBlood(timeline, tick, systems, viewing);
        partAt = Part("camera particles/bursts/blood", partAt);
        AddSplashes(tick, systems);
        partAt = Part("camera particles/bursts/splashes", partAt);
        AddSentryMuzzleFlashes(timeline, tick, systems);
        partAt = Part("camera particles/bursts/sentries", partAt);
        AddParticleDispatches(timeline, tick, systems);
        partAt = Part("camera particles/bursts/dispatches", partAt);
        AddWeaponMuzzleFlashes(timeline, tick, systems);
        partAt = Part("camera particles/bursts/muzzles", partAt);

        DrawnPlayers(frame.Players, tick);
        partAt = Part("camera particles/bursts/players", partAt);

        AddHealBeams(timeline, tick, systems);
        partAt = Part("camera particles/bursts/beams", partAt);
        AddOverheals(timeline, tick, systems);
        _ = Part("camera particles/bursts/overheals", partAt);

        long steppingAt = System.Diagnostics.Stopwatch.GetTimestamp();
        SearchTicks += steppingAt - searchingAt;
        Searches++;

        _particles.Bursts(
            _burstsNow,
            timeline.IntervalPerTick,
            systems,
            tick,
            frame.ShownTick,
            frame.Seeked);

        long steppedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        BurstTicks += steppedAt - steppingAt;
        Parts?.Add("camera particles/bursts/step", steppedAt - steppingAt);

        return _whizzes;
    }

    /// <summary>
    /// **Where the frame DREW each player** (B523), for the effects that follow one; and the playing samples of
    /// another tick are forgotten.
    /// </summary>
    internal void DrawnPlayers(IReadOnlyList<ScenePlayer> players, int tick)
    {
        _playersNow.Clear();
        _yawsNow.Clear();

        foreach (ScenePlayer player in players)
        {
            _playersNow[player.EntityIndex] = new Vector3(player.X, player.Y, player.Z);
            _yawsNow[player.EntityIndex] = player.Yaw;
        }

        if (tick != _playedTick)
        {
            _playedAt.Clear();
            _playedTick = tick;
        }
    }

    /// <summary>A tracer's near-miss whiz, when it passes the listener and none has played in the last 0.1 s.</summary>
    private void Whiz(int tick, (float X, float Y, float Z) from, (float X, float Y, float Z) to, Vector3 eye, ClientSoundOrder order)
    {
        IReadOnlyDictionary<string, SoundScriptEntry>? scripts = _world.Scripts;

        if (_frame.Silent || scripts is null)
        {
            return;
        }

        double now = tick * (_frame.Timeline?.IntervalPerTick ?? (1f / 66f));

        if (now < _lastWhizAsked)
        {
            // A seek back: the timer belongs to a future this playback has not reached.
            _nextWhizSeconds = 0d;
        }

        _lastWhizAsked = now;

        Vector3 start = new(from.X, from.Y, from.Z);

        if (now < _nextWhizSeconds || !TracerWhiz.Hears(start, new Vector3(to.X, to.Y, to.Z), eye))
        {
            return;
        }

        if (TracerWhiz.SoundAt(tick, from, scripts, order) is { } whiz)
        {
            _whizzes.Add(whiz);
        }

        _nextWhizSeconds = now + TracerWhiz.CooldownSeconds;
    }

    /// <summary>Adds a burst for every tracer in the window — `ParticleTracerCallback` (B415).</summary>
    /// <remarks>
    /// **Control point 0 is the start and 1 is the end, with 0 facing the end**: `ParticleTracerCallback` takes
    /// `VectorAngles` of the normalised start-to-end and `ParticleEffectCallback` sets both points and that
    /// orientation before the effect first simulates.
    ///
    /// **The start is the active weapon's `muzzle`** when that weapon is posed here — `FireBullet`'s
    /// `pWeapon->GetAttachment( iAttachment, vecStart )` on the non-dormant branch — and the bullet's own origin
    /// otherwise, which is what the engine keeps when the lookup fails. It is read ONCE, the first time the tracer is
    /// offered, as the engine reads it once when the shot arrives: re-read every frame, a tracer would follow the gun.
    /// A seek that lands mid-flight reads the gun where it is then, which is the nearest pose there is.
    /// </remarks>
    private void AddTracers(int tick, IReadOnlyDictionary<string, ParticleSystem> systems, Vector3 eye)
    {
        IReadOnlyList<ShotTracer>? tracers = _world.Tracers;

        if (tracers is null || tracers.Count <= 0)
        {
            return;
        }

        TickWindow.Between(tracers, static tracer => tracer.Tick, tick - TracerWindowTicks, tick, _tracersNow);

        foreach ((int index, ShotTracer tracer) in _tracersNow)
        {
            if (!systems.TryGetValue(tracer.Effect, out ParticleSystem? definition))
            {
                continue;
            }

            if (!_tracerStarts.TryGetValue(index, out ((float X, float Y, float Z) From, (float X, float Y, float Z) To) path))
            {
                (float X, float Y, float Z)? muzzle = _models.AttachmentPosition(tracer.Weapon, "muzzle");

                (float X, float Y, float Z) start = muzzle ?? tracer.Start;

                // **Players are clipped only for a tracer met on its own tick**, where this frame's bones are the ones
                // the engine traced against when the shot arrived. Met later — a seek into the window — the players
                // have moved and this frame's bones would strike whoever stands there now; the world's end is kept.
                ((float X, float Y, float Z) end, bool judged, _) = tick - tracer.Tick <= 1
                    ? _strikes.End(
                        new ShotImpact(
                            tracer.Shot, tracer.Bullet, tracer.Tick, tracer.Shooter, 0, tracer.Start, tracer.End, tracer.Reach, -1),
                        new BulletScene(_frame.Timeline, _world.Level, _frame.Source, _frame.ShownTick, _frame.Playing))
                    : (tracer.End, true, null);

                path = (start, end);

                // Asked again next frame until a model pass has answered: the first frame after a load or a seek offers
                // tracers before any entity is posed. A muzzle, or any player posed for the hitbox pass, is an answer;
                // a weapon that is never drawn — the spectated player's own, in first person — is not waited for.
                if (muzzle is not null || _models.IsPosed(tracer.Weapon) || judged)
                {
                    _tracerStarts[index] = path;

                    // `ParticleTracerCallback` → `FX_TracerSound`, once, as the tracer is made; a tracer met by a seek
                    // was made in a stretch nobody heard.
                    if (tick - tracer.Tick <= 1)
                    {
                        Whiz(tracer.Tick, path.From, path.To, eye, BulletStrikes.ShotOrder(_frame.Timeline, tracer.Shot, tracer.Bullet));
                    }
                }

                if (_renderLog.IsEnabled(LogLevel.Debug))
                {
                    float away = Vector3.Distance(
                        new Vector3(start.X, start.Y, start.Z), new Vector3(tracer.Start.X, tracer.Start.Y, tracer.Start.Z));
                    float shortened = Vector3.Distance(
                        new Vector3(path.To.X, path.To.Y, path.To.Z), new Vector3(tracer.End.X, tracer.End.Y, tracer.End.Z));

                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    _renderLog.LogDebug(
                        "{Message}",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"tracer {index} (shot {tracer.Shot}, weapon entity {tracer.Weapon}) starts at " +
                            $"{(muzzle is null ? "the bullet origin, no muzzle" : "the muzzle")}, {away:0.0} units from the origin" +
                            $"{(_models.IsPosed(tracer.Weapon) ? string.Empty : "; weapon not posed yet")}; " +
                            $"ends {shortened:0.0} units short of the world"));
                }
            }

            (float X, float Y, float Z) from = path.From;
            (float X, float Y, float Z) to = path.To;

            (float pitch, float yaw, float roll) = AngleVectors.Angles(to.X - from.X, to.Y - from.Y, to.Z - from.Z);

            (float fx, float fy, float fz) = AngleVectors.Forward(pitch, yaw);
            (float px, float py, float pz) = AngleVectors.Right(pitch, yaw, roll);
            (float qx, float qy, float qz) = AngleVectors.Up(pitch, yaw, roll);

            _burstsNow.Add(new ParticleBurst(
                TracerKeys + index,
                definition,
                new ParticleControlPoint(
                    new Vector3(from.X, from.Y, from.Z),
                    new Vector3(fx, fy, fz),
                    new Vector3(px, py, pz),
                    new Vector3(qx, qy, qz)),
                tracer.Tick,
                ParticleControlPoint.Unoriented(new Vector3(to.X, to.Y, to.Z))));
        }
    }

    /// <summary>
    /// Starts a splash where each bullet in the window entered water — `ImpactWaterTrace`'s `tf_gunshotsplash`, which
    /// `TFSplashCallbackHelper` draws as its particle system at that point with the world's axes (`tf_fx_impacts.cpp:141`).
    /// </summary>
    private void AddSplashes(int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        IReadOnlyList<ShotImpact>? impacts = _world.Impacts;

        if (impacts is null)
        {
            return;
        }

        TickWindow.Between(impacts, static impact => impact.Tick, tick - BloodWindowTicks, tick, _splashesNow);

        foreach ((int index, ShotImpact impact) in _splashesNow)
        {
            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (impact.WaterEntry is { } entry && impact.Splash is { } name && systems.TryGetValue(name, out ParticleSystem? definition))
            {
                Vector3 at = new(entry.X, entry.Y, entry.Z);

                _burstsNow.Add(new ParticleBurst(SplashKeys + index, definition, ParticleControlPoint.Unoriented(at), impact.Tick));
            }
        }
    }

    /// <summary>Adds the impact puff and spray of every blood event in the window — `TFBloodSprayCallback` (B415).</summary>
    /// <remarks>
    /// **The spray is turned against the view when the blood ARRIVES**, as `MainViewOrigin` and `MainViewAngles` are
    /// read inside the callback: fixed the first time an event is offered, so the camera moving afterwards does not
    /// swing it. A seek into the window reads the view where it is then. An effect `DispatchParticleEffect` would
    /// attach to a dormant player is not created; the feed keeps no dormancy, so every event is taken as live.
    /// </remarks>
    private void AddBlood(
        DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems, FreeCamera viewing)
    {
        TickWindow.Between(timeline.Blood.All, static blood => blood.Tick, tick - BloodWindowTicks, tick, _bloodNow);

        foreach ((int index, SceneBlood blood) in _bloodNow)
        {
            if (!_bloodBursts.TryGetValue(index, out (BloodBurst Impact, BloodBurst Spray) pair))
            {
                pair = BloodEffects.For(
                    blood,
                    new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
                    (viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll),
                    SeededDraw.For(SeededDraw.Of(index, 0, salt: 2)));

                _bloodBursts[index] = pair;

                if (_renderLog.IsEnabled(LogLevel.Debug))
                {
                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    _renderLog.LogDebug(
                        "{Message}",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"blood {index} at tick {blood.Tick} on entity {blood.Entity}: {pair.Impact.System} " +
                            $"({(systems.ContainsKey(pair.Impact.System) ? "loaded" : "NO DEFINITION")}), {pair.Spray.System} " +
                            $"({(systems.ContainsKey(pair.Spray.System) ? "loaded" : "NO DEFINITION")})"));
                }
            }

            AddBloodBurst(BloodKeys + (index * 2L), pair.Impact, blood.Tick, systems);
            AddBloodBurst(BloodKeys + (index * 2L) + 1, pair.Spray, blood.Tick, systems);
        }
    }

    private void AddBloodBurst(long key, BloodBurst burst, int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        if (systems.TryGetValue(burst.System, out ParticleSystem? definition))
        {
            // `DispatchParticleEffect( name, origin, angles, … )` passes the origin as `vecStart` too: control point 1.
            _burstsNow.Add(new ParticleBurst(
                key, definition, burst.Point, tick, ParticleControlPoint.Unoriented(burst.Point.At)));
        }
    }

    /// <summary>Adds every sentry muzzle flash in the window at its barrel — `TF_3rdPersonMuzzleFlashCallback_SentryGun` (B415).</summary>
    /// <remarks>
    /// **`PATTACH_POINT_FOLLOW`, so the barrel is read every frame**, not once: the flash rides the barrel as the sentry
    /// turns. A sentry not posed here has no barrel to follow and its flash is not started, which stands in for the
    /// callback's `IsDormant` refusal — a sentry outside the recorder's view is not in the table, and one culled
    /// from this view would flash where nobody is looking.
    /// </remarks>
    private void AddSentryMuzzleFlashes(DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        TickWindow.Between(
            timeline.Dispatches.All, static dispatch => dispatch.Tick, tick - MuzzleFlashWindowTicks, tick, _dispatchesNow);

        foreach ((int index, SceneEffectDispatch dispatch) in _dispatchesNow)
        {
            // Finished, it is offered as it last was, without the barrel query (B577) — while posed, as a fresh build needs.
            if (_models.IsPosed(dispatch.Entity) && Reoffer(MuzzleFlashKeys + index))
            {
                continue;
            }

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (!string.Equals(
                    timeline.Dispatches.Names.Name(dispatch.Name), SentryMuzzleFlash.EffectName, StringComparison.Ordinal) ||
                !systems.TryGetValue(SentryMuzzleFlash.System(dispatch.Flags), out ParticleSystem? definition) ||
                _models.AttachmentPoint(dispatch.Entity, dispatch.Attachment) is not { } barrel)
            {
                continue;
            }

            Offer(new ParticleBurst(MuzzleFlashKeys + index, definition, barrel, dispatch.Tick));
        }
    }

    /// <summary>Draws every medigun beam at this tick — `CWeaponMedigun::UpdateEffects` (B396).</summary>
    /// <remarks>
    /// Control point 0 follows the medigun's `muzzle`; control point 1 the target's origin plus 50 up
    /// (`PATTACH_ABSORIGIN_FOLLOW`, offset `( 0, 0, 50 )`), both read every frame. A stopped beam fades.
    /// **A seek into a beam replays at most two seconds of it**, from where it was first met: the beam's own particles
    /// live well under that, so the picture is the same as replaying from its start. *Not built:* the revive marker's
    /// `healbeam` (MvM). In the medic's own eyes control point 0 is the hands' muzzle (<see cref="MedigunMuzzle"/>, B539).
    /// </remarks>
    private void AddHealBeams(DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        ItemSchema? items = _world.Items;

        AddHealBeams(
            timeline.HealBeams.All,
            timeline.RecorderEntityIndex,
            then => PlayedAt(timeline, then),
            beam => beam.Item is { } item ? items?.CustomParticle(item, beam.Team) : null,
            !_frame.Playing,
            _frame.HealTargetMarker,
            tick,
            systems,
            _frame.FirstPerson ? _frame.Followed : null,
            beam => beam.Owner is { } owner &&
                    timeline.ViewmodelAt(tick, owner) is { } held &&
                    string.Equals(held.WeaponClassName, HealBeamFeed.MedigunClassName, StringComparison.Ordinal) &&
                    held.WeaponItem == beam.Item);
    }

    /// <summary>The same over the demo's beams as data, which a test can supply (D214).</summary>
    /// <param name="beams">Every beam of the demo.</param>
    /// <param name="recorder">The recording player's entity, or null.</param>
    /// <param name="playedAt">Every player as a playing frame at a tick draws him, by entity.</param>
    /// <param name="customParticle">A beam's item's `custom_particlesystem`, or null.</param>
    /// <param name="paused">Whether playback is paused.</param>
    /// <param name="healTargetMarker">`hud_medichealtargetmarker`.</param>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="systems">The loaded particle systems by name.</param>
    /// <param name="inEyesOf">The player whose eyes the view is in, or null for any other camera.</param>
    /// <param name="handsHold">Whether the beam's medic holds THIS medigun in his hands; null for never.</param>
    internal void AddHealBeams(
        IReadOnlyList<SceneHealBeam> beams,
        int? recorder,
        Func<int, Dictionary<int, ScenePlayer>> playedAt,
        Func<SceneHealBeam, string?> customParticle,
        bool paused,
        bool healTargetMarker,
        int tick,
        IReadOnlyDictionary<string, ParticleSystem> systems,
        int? inEyesOf = null,
        Func<SceneHealBeam, bool>? handsHold = null)
    {
        if (tick < _healBeamTick)
        {
            _healBeamFrom.Clear();
        }

        _healBeamTick = tick;

        // **A paused frame keeps the effect where playing left it** (B538): the client simulates no particle while
        // paused (`ParticleMgr()->Simulate( gpGlobals->frametime )`, cdll_client_int.cpp:2234, on a frame time of 0),
        // so a beam stays between the INTERPOLATED positions the last playing frame drew, while every entity snaps
        // to its last received one (B399). A seek that lands paused is caught up the same way and left there.
        // ponytail: a scan over every beam, 1,211 on f12; an interval index if a demo ever carries far more.
        for (int index = 0; index < beams.Count; index++)
        {
            SceneHealBeam beam = beams[index];

            if (beam.Start > tick || (beam.End is { } end && tick >= end + HealBeamFadeTicks))
            {
                continue;
            }

            // `bHealTargetMarker && pFiringPlayer == pLocalPlayer`: only the recording medic's own beam, and only when
            // the config asks. A SourceTV recording's local player is never a medic.
            bool targeted = healTargetMarker && beam.Owner is { } medic && medic == recorder;
            string name = HealBeamFeed.EffectName(beam.Team, beam.ChargeRelease, targeted);

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (!systems.TryGetValue(name, out ParticleSystem? definition) ||
                MedigunMuzzle(beam, inEyesOf, handsHold) is not { } muzzle)
            {
                continue;
            }

            // **Where the frame DREW them** (B523): `UpdateControlPoint` reads the patient's `GetAbsOrigin()`
            // (particle_property.cpp:637), interpolated while playing. This read the newest recorded frame, which leads
            // the drawn player by the interpolation delay — the beam ended ahead of anyone running.
            if (!_playersNow.TryGetValue(beam.Target, out Vector3 target))
            {
                continue;
            }

            if (!_healBeamFrom.TryGetValue(index, out int from))
            {
                from = Math.Max(beam.Start, tick - HealBeamFadeTicks);
                _healBeamFrom[index] = from;
            }

            // The patient's own axes — `pPoint->hEntity->GetVectors` (particle_property.cpp:638) — which the trail's
            // `bias in local system` lays its sphere in (B529). A player's abs angles are his yaw alone.
            ParticleControlPoint patient = ParticleControlPoint.Facing(
                target + new Vector3(0f, 0f, 50f), _yawsNow.GetValueOrDefault(beam.Target));

            // **A beam met by a seek is caught up along the path its two ends were DRAWN on while playing** (B527,
            // B538), not between where they stand now: asked only for the ticks a catch-up steps and for a paused frame.
            Vector3? medicDrawn = beam.Owner is { } owner && _playersNow.TryGetValue(owner, out Vector3 drawn) ? drawn : null;
            float medicYaw = beam.Owner is { } healer ? _yawsNow.GetValueOrDefault(healer) : 0f;
            Func<int, (ParticleControlPoint At, ParticleControlPoint? End)> history =
                HealBeamHistory(playedAt, beam, muzzle, medicDrawn, medicYaw, patient);

            (ParticleControlPoint at, ParticleControlPoint? far) = paused ? history(tick) : (muzzle, patient);

            _burstsNow.Add(new ParticleBurst(HealBeamKeys + (2L * index), definition, at, from, far, beam.End, history));

            // The item's `custom_particlesystem`, created beside the beam on the same two points (`:2469-2479`).
            if (customParticle(beam) is { } custom &&
                systems.TryGetValue(custom, out ParticleSystem? added))
            {
                _burstsNow.Add(new ParticleBurst(HealBeamKeys + (2L * index) + 1, added, at, from, far, beam.End, history));
            }
        }
    }

    /// <summary>A live beam's <see cref="HealBeamThen"/> as a burst's history.</summary>
    /// <remarks>
    /// Its own method because a lambda in the loop body captured the loop's locals, so the compiler allocated the
    /// closure at the top of EVERY iteration over all the demo's beams, live or not (B574).
    /// </remarks>
    private static Func<int, (ParticleControlPoint At, ParticleControlPoint? End)> HealBeamHistory(
        Func<int, Dictionary<int, ScenePlayer>> playedAt,
        SceneHealBeam beam,
        ParticleControlPoint muzzle,
        Vector3? medicDrawn,
        float medicYaw,
        ParticleControlPoint patient) =>
        then => HealBeamThen(playedAt(then), beam, muzzle, medicDrawn, medicYaw, patient);

    /// <summary>Control point 0 of a beam — the medigun's `muzzle` as `CEconEntity::GetAttachment` answers it (B539).</summary>
    /// <remarks>
    /// <code>
    /// // econ_entity.cpp:1135-1178 — a c_model whose owner is not drawn (the view in his eyes) gets a
    /// // C_ViewmodelAttachmentModel on his viewmodel; :1978-1984 — GetAttachment answers from it when it exists.
    /// // particle_property.cpp:608-611 — a failed GetAttachment falls back to the weapon's own C_BaseAnimating one.
    /// </code>
    /// So in the medic's own eyes the beam starts at the hands' muzzle, on the hands' axes, which the path's bulge
    /// (bulge control 1, the start point's forward) follows: TF2's beam swings out along the first-person gun.
    ///
    /// The hands' model is the one `SetOuter( this )` tied to THIS medigun (`:1157`), so it is used only while the hands
    /// hold it; holding anything else, the hands model here is that weapon's and the world muzzle stands.
    /// `r_drawviewmodel` is not consulted: `UpdateAttachmentModels` creates the model on `!ShouldDrawThisPlayer()` alone,
    /// and `GetAttachment` poses it whether or not it is drawn. `FlashPoint` differs on purpose:
    /// `CreateMuzzleFlashEffects` itself returns under `r_drawviewmodel 0`. A medigun that IS its own viewmodel (not
    /// attached to hands) gets no attachment model (`:1135` tests `ShouldAttachToHands`), so `GetAttachment` is the
    /// world weapon's — where `FlashPoint` takes the arms' muzzle because `GetAppropriateWorldOrViewModel` returns
    /// the viewmodel itself. The two differ because the engine's two calls do.
    /// </remarks>
    private ParticleControlPoint? MedigunMuzzle(SceneHealBeam beam, int? inEyesOf, Func<SceneHealBeam, bool>? handsHold) =>
        (inEyesOf is { } eyes && beam.Owner == eyes && handsHold is not null && handsHold(beam)
            ? _models.AttachmentPoint(ViewmodelScene.WeaponEntityIndex, "muzzle")
            : null) ??
        _models.AttachmentPoint(beam.Medigun, "muzzle");

    /// <summary>
    /// Where a beam's two ends were drawn by a playing frame at a tick — <see cref="FollowedPoint"/>, the muzzle carried
    /// with its medic as this frame posed him.
    /// </summary>
    internal static (ParticleControlPoint At, ParticleControlPoint? End) HealBeamThen(
        Dictionary<int, ScenePlayer> played,
        SceneHealBeam beam,
        ParticleControlPoint muzzle,
        Vector3? medicDrawn,
        float medicYaw,
        ParticleControlPoint patient)
    {
        ParticleControlPoint at = muzzle;
        ParticleControlPoint end = patient;

        if (medicDrawn is { } drawn && beam.Owner is { } owner && played.TryGetValue(owner, out ScenePlayer medic))
        {
            at = FollowedPoint.Then(muzzle, drawn, drawn, medicYaw, new Vector3(medic.X, medic.Y, medic.Z), medic.Yaw);
        }

        // `PATTACH_ABSORIGIN_FOLLOW` with an offset: the patient's end has no lever arm to turn, only his movement.
        if (played.TryGetValue(beam.Target, out ScenePlayer target))
        {
            end = ParticleControlPoint.Facing(new Vector3(target.X, target.Y, target.Z + 50f), target.Yaw);
        }

        return (at, end);
    }

    /// <summary>Every player as a PLAYING frame at a whole tick draws him — interpolated — by entity (B538).</summary>
    /// <remarks>
    /// What a followed control point read on that tick: `UpdateControlPoint` takes the entity's `GetAbsOrigin()`, which
    /// is interpolated while playing. Kept for the tick being shown, so a paused frame asks once and a catch-up pays
    /// once per tick it steps.
    /// </remarks>
    private Dictionary<int, ScenePlayer> PlayedAt(DemoTimeline timeline, int tick)
    {
        if (!_playedAt.TryGetValue(tick, out Dictionary<int, ScenePlayer>? players))
        {
            players = [];
            timeline.PlayersAt((double)tick, _playedScratch);

            foreach (ScenePlayer player in _playedScratch)
            {
                players[player.EntityIndex] = player;
            }

            _playedAt[tick] = players;
        }

        return players;
    }

    /// <summary>Draws the pluses on every overhealed player — `C_TFPlayer::UpdateOverhealEffect` (B538).</summary>
    /// <remarks>
    /// `overhealedplayer_blue_pluses` or its red twin on the player's origin (`PATTACH_ABSORIGIN_FOLLOW`), born on his
    /// hitboxes and riding them. The definition's "control point to disable rendering if it is the camera" is 0, so the
    /// player whose eyes the view is in shows none (`CNewParticleEffect::DrawModel`, particles_new.cpp:541-571).
    /// </remarks>
    private void AddOverheals(DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems) =>
        AddOverheals(
            timeline.PlayersAt,
            timeline.RecorderEntityIndex,
            then => PlayedAt(timeline, then),
            !_frame.Playing,
            _frame.FirstPerson ? _frame.Followed : null,
            tick,
            systems);

    /// <summary>The same over the demo's players as data, which a test can supply (D214).</summary>
    /// <param name="recordedAt">The recorded players on a tick.</param>
    /// <param name="recorder">The recording player's entity, or null.</param>
    /// <param name="playedAt">Every player as a playing frame at a tick draws him, by entity.</param>
    /// <param name="paused">Whether playback is paused.</param>
    /// <param name="inEyes">The player whose eyes the view is in, or null.</param>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="systems">The loaded particle systems by name.</param>
    internal void AddOverheals(
        Func<int, IReadOnlyList<ScenePlayer>> recordedAt,
        int? recorder,
        Func<int, Dictionary<int, ScenePlayer>> playedAt,
        bool paused,
        int? inEyes,
        int tick,
        IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        foreach (OverhealRun run in _overheals.At(tick, recordedAt, recorder))
        {
            int entity = run.Entity;

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (entity == inEyes ||
                OverhealEffects.EffectName(run.Team) is not { } name ||
                !systems.TryGetValue(name, out ParticleSystem? definition) ||
                !_playersNow.TryGetValue(entity, out Vector3 drawn) ||
                _models.PosedHitboxes(entity) is not { } posed)
            {
                // Not offered until his model is posed here: an effect caught up without its boxes is born on the
                // control point, a heap of pluses at his feet.
                continue;
            }

            ParticleControlPoint now = ParticleControlPoint.Facing(drawn, _yawsNow.GetValueOrDefault(entity));
            Func<int, (ParticleControlPoint At, ParticleControlPoint? End)> history = then =>
                (playedAt(then).TryGetValue(entity, out ScenePlayer was)
                    ? ParticleControlPoint.Facing(new Vector3(was.X, was.Y, was.Z), was.Yaw)
                    : now,
                 null);

            _burstsNow.Add(
                new ParticleBurst(
                    OverhealKeys + ((long)run.From << 7) + entity,
                    definition,
                    paused ? history(tick).At : now,
                    run.From,
                    null,
                    run.Stop,
                    history)
                {
                    Model = () => posed,
                });
        }
    }

    /// <summary>Starts every weapon's muzzle flash in the window — `CTFWeaponBase::CreateMuzzleFlashEffects` (B415).</summary>
    /// <remarks>
    /// **Only for a weapon posed here**, which stands in for `DoAnimationEvents`' own gate: it returns before looking at
    /// the parity for an entity that is not visible, and a first-person weapon is the viewmodel's business. Nothing
    /// starts unless the model has a `muzzle` attachment; the particle then follows it, as the backblast follows the
    /// weapon's own `backblast`. **No muzzle flash MODEL, as in TF2:** only the medigun's script names one, and nothing
    /// raises a medigun's parity (B415). The first-person flash arrives on the viewmodel's own counter and starts on the
    /// viewmodel — <see cref="FlashPoint"/>.
    /// </remarks>
    private void AddWeaponMuzzleFlashes(DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        Func<string, byte[]?>? read = _world.Read;

        if (read is null)
        {
            return;
        }

        _weaponMuzzles ??= new WeaponMuzzleFlashes(read, _world.Items);

        TickWindow.Between(
            timeline.MuzzleFlashes.All, static flash => flash.Tick, tick - WeaponFlashWindowTicks, tick, _weaponFlashesNow);

        foreach ((int index, SceneMuzzleFlash flash) in _weaponFlashesNow)
        {
            long muzzleKey = WeaponFlashKeys + (index * 2L);
            WeaponMuzzleFlash what = _weaponMuzzles.For(flash.Item, flash.Team);

            // "Don't do backblast effects in first person" — the test is `pOwner->IsLocalPlayer()`, so the recorder's own launcher
            // never has one, in any view (`tf_weapon_rocketlauncher.cpp:383`).
            bool backblasts = what.Backblast && flash.Viewmodel is null && flash.Owner != timeline.RecorderEntityIndex;

            // **Nothing left to build on a played frame, no attachment query** (B577). A flash with nothing under it — no
            // muzzle particle and no backblast — offers nothing, as a fresh build did. A world flash whose every key is let go
            // offers each as it last was, while its weapon is posed, which is when a fresh build would offer it at all.
            // The first-person log line still gets its frame: it is written only while debug is on.
            if (_played && !OfferGoneBursts && (flash.Viewmodel is null || !_renderLog.IsEnabled(LogLevel.Debug)))
            {
                if (what.Particle is null && !backblasts)
                {
                    continue;
                }

                if (flash.Viewmodel is null && _models.IsPosed(flash.Weapon) &&
                    (what.Particle is null || Gone(muzzleKey)) && (!backblasts || Gone(muzzleKey + 1)))
                {
                    Reoffer(muzzleKey);
                    Reoffer(muzzleKey + 1);
                    continue;
                }
            }

            ParticleControlPoint? point = FlashPoint(flash);

            // One line per first-person flash, the first frame it is offered: what started, or why nothing did. Debug, and
            // guarded so the string is never built otherwise: at Information it was flushed per shot on the main thread
            // (B591, the B191 shape).
            if (flash.Viewmodel is not null && _renderLog.IsEnabled(LogLevel.Debug) && _viewmodelFlashesLogged.Add(index))
            {
                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                string where = point is { } p
                    ? string.Create(CultureInfo.InvariantCulture, $"at ({p.At.X:0} {p.At.Y:0} {p.At.Z:0})")
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"no muzzle (first person {_frame.FirstPerson}, followed {_frame.Followed}, owner {flash.Owner})");

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                _renderLog.LogDebug(
                    "{Message}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"viewmodel muzzle flash tick {flash.Tick} weapon {flash.Weapon} item {flash.Item}: particle {what.Particle ?? "none"}, {where}"));
            }

            if (point is null)
            {
                continue;
            }

            ParticleControlPoint muzzle = point.Value;

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (what.Particle is { } particle && systems.TryGetValue(particle, out ParticleSystem? definition))
            {
                Offer(new ParticleBurst(muzzleKey, definition, muzzle, flash.Tick));
            }

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (backblasts &&
                systems.TryGetValue(WeaponMuzzleFlashes.BackblastSystem, out ParticleSystem? backblast) &&
                _models.AttachmentPoint(flash.Weapon, WeaponMuzzleFlashes.BackblastAttachment) is { } behind)
            {
                Offer(new ParticleBurst(muzzleKey + 1, backblast, behind, flash.Tick));
            }
        }
    }

    /// <summary>Where a flash starts — `GetAppropriateWorldOrViewModel`'s `muzzle` — or null where the engine starts none.</summary>
    /// <remarks>
    /// A viewmodel flash (`CTFViewModel::ProcessMuzzleFlashEvent`) shows only in its owner's first-person view, and
    /// `CreateMuzzleFlashEffects` returns under `r_drawviewmodel 0`. It starts on the weapon attached to the hands when there is
    /// one, else on the viewmodel itself. A world weapon's flash starts on the weapon, when it is in the scene.
    /// </remarks>
    private ParticleControlPoint? FlashPoint(SceneMuzzleFlash flash)
    {
        if (flash.Viewmodel is null)
        {
            return _models.AttachmentPoint(flash.Weapon, "muzzle");
        }

        if (!_frame.FirstPerson || !_frame.DrawViewmodel || flash.Owner is null || _frame.Followed != flash.Owner)
        {
            return null;
        }

        return _models.AttachmentPoint(ViewmodelScene.WeaponEntityIndex, "muzzle") ??
               _models.AttachmentPoint(ViewmodelScene.ArmsEntityIndex, "muzzle");
    }

    /// <summary>Adds every `"ParticleEffect"` dispatch in the window — `ParticleEffectCallback` (B415).</summary>
    /// <remarks>
    /// <code>
    /// // c_particle_system.cpp:181
    /// name = GetParticleSystemNameFromIndex( m_nHitBox )
    /// if FROM_ENTITY:  entity non-dormant → ParticleProp()->Create( name, (attach) m_nDamageType, m_nAttachmentIndex, offset )
    ///                  CUSTOMORIGIN: control point 0 = m_vOrigin, 1 = m_vStart, 0 oriented by m_vAngles
    /// else:            Create( name ): control point 0 = m_vOrigin, 1 = m_vStart, 0 oriented by m_vAngles
    /// m_bControlPoint1 → control point 1 = m_ControlPoint1.m_vecOffset
    /// </code>
    /// **A following effect is offered its attachment's pose every frame**, which is `PATTACH_POINT_FOLLOW`. An entity
    /// not posed here starts nothing, standing in for the dormancy refusal. **Not built**, and absent from f12's 1,309:
    /// the origin and root-bone attach types, a following effect's `m_vStart` offset, `PARTICLE_DISPATCH_RESET_PARTICLES`,
    /// custom colours, and the Pyrovision translation.
    /// </remarks>
    private void AddParticleDispatches(DemoTimeline timeline, int tick, IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        TickWindow.Between(
            timeline.Dispatches.All, static dispatch => dispatch.Tick, tick - ParticleDispatchWindowTicks, tick, _particleDispatchesNow);

        // **An entity's effect runs until Valve's own stop reaches it** — `ParticleEffectStop`, or a later dispatch on the
        // entity with RESET (`DispatchStops`) — so one stopped later than the window is still offered until it is.
        if (!ReferenceEquals(_stopsOf, timeline))
        {
            _stopsOf = timeline;
            _stops = DispatchStops.Compute(timeline.Dispatches.All, timeline.Dispatches.Names.Name, timeline.Dormancy.DeletedAfter);
            _dormantAt = new Func<int, bool>?[_stops.Length];
            _owned.Clear();
            _liveOwned.Clear();
            _ownedFired = 0;

            for (int index = 0; index < _stops.Length; index++)
            {
                SceneEffectDispatch dispatch = timeline.Dispatches.All[index];

                if ((dispatch.Flags & DispatchFromEntity) != 0 &&
                    string.Equals(timeline.Dispatches.Names.Name(dispatch.Name), "ParticleEffect", StringComparison.Ordinal))
                {
                    // The walk below stops at the first dispatch not yet fired, which is only right in fire order.
                    if (_owned.Count > 0 && dispatch.Tick < timeline.Dispatches.All[_owned[^1]].Tick)
                    {
                        throw new InvalidOperationException(
                            string.Create(CultureInfo.InvariantCulture, $"dispatch {index} at tick {dispatch.Tick} is out of fire order"));
                    }

                    _owned.Add(index);
                }
            }
        }

        // **An effect from no entity ends by itself** (`CNewParticleEffect::Create( NULL, … )`): offered over the window,
        // which is only how far back a seek replays it.
        foreach ((int index, SceneEffectDispatch dispatch) in _particleDispatchesNow)
        {
            // Finished, it is offered as it last was (B577): a point fixed at first sight, so a fresh build offers the same.
            if ((dispatch.Flags & DispatchFromEntity) == 0 &&
                !Reoffer(ParticleDispatchKeys + index) &&
                string.Equals(timeline.Dispatches.Names.Name(dispatch.Name), "ParticleEffect", StringComparison.Ordinal))
            {
                AddParticleEffect(ParticleDispatchKeys + index, dispatch, timeline, systems, remember: true);
            }
        }

        // **An entity's effect runs until Valve ends it** — a `ParticleEffectStop`, a later RESET dispatch on it, or the
        // owner's deletion (`DispatchStops`) — and is offered until its fade is past, so a seek into the fade builds it fading
        // exactly as playing through does. **Dormancy stops its emission, and does not end it** (`OwnerSetDormantTo`,
        // particle_property.cpp:465; `CParticleCollection::SetDormant`, particles.obj).
        // In fire order, so the first not yet fired ends the walk.
        //
        // **A finished one is not offered while playing** (finding 86): `ProcessPSystem` flags an effect for removal once
        // `IsFinished` (particlemgr.cpp:1581-1584) and nothing reads its owner again. Offered, it cost an attachment query
        // and a burst every frame for the rest of the demo, to be skipped. A seek, or a clock gone back, offers it again,
        // since that frame rebuilds from the offer (D221).
        bool played = _played;

        // **Each frame visits only what is live, not every owned dispatch since the first** (B577): the engine creates an
        // effect once, when its dispatch arrives (`C_TEEffectDispatch::PostDataUpdate`, c_te_effect_dispatch.cpp:146-154),
        // and updates only the effects it holds (`CParticleProperty::UpdateParticleEffect`, particle_property.cpp:507-519).
        // Both drops are for good while playing: gone stays gone until a landed frame clears `_spent`, and a tick past the
        // fade only grows. A landed frame — or a new timeline, which cleared `_owned` — starts again from the first.
        if (!played || WalkEveryOwnedDispatch)
        {
            _liveOwned.Clear();
            _ownedFired = 0;
        }

        while (_ownedFired < _owned.Count && timeline.Dispatches.All[_owned[_ownedFired]].Tick <= tick)
        {
            _liveOwned.Add(_owned[_ownedFired++]);
        }

        int kept = 0;

        for (int live = 0; live < _liveOwned.Count; live++)
        {
            int index = _liveOwned[live];

            if (played && _particles.IsGone(ParticleDispatchKeys + index))
            {
                continue;
            }

            if (_stops[index] is { } stop && tick >= stop + ParticleDispatchWindowTicks)
            {
                continue;
            }

            _liveOwned[kept++] = index;

            SceneEffectDispatch dispatch = timeline.Dispatches.All[index];

            AddParticleEffect(
                ParticleDispatchKeys + index, dispatch, timeline, systems, _stops[index],
                _dormantAt[index] ??= DormantAt(timeline.Dormancy, dispatch.Entity));
        }

        _liveOwned.RemoveRange(kept, _liveOwned.Count - kept);

        // `C_TETFParticleEffect::PostDataUpdate` is the same dispatch, built on the client.
        TickWindow.Between(
            timeline.TfParticleEffects.All, static dispatch => dispatch.Tick, tick - ParticleDispatchWindowTicks, tick, _tfParticlesNow);

        foreach ((int index, SceneEffectDispatch dispatch) in _tfParticlesNow)
        {
            // Finished, offered as it last was (B577); one from an entity only while that entity is posed, as a fresh build
            // needs it to be.
            if (((dispatch.Flags & DispatchFromEntity) == 0 || _models.IsPosed(dispatch.Entity)) &&
                Reoffer(TfParticleKeys + index))
            {
                continue;
            }

            AddParticleEffect(TfParticleKeys + index, dispatch, timeline, systems, remember: true);
        }
    }

    /// <summary>An owned dispatch's dormancy question, made once per dispatch and timeline rather than once a frame (B577).</summary>
    private static Func<int, bool> DormantAt(EntityDormancy dormancy, int owner) => at => dormancy.IsDormant(owner, at);

    /// <summary>One `"ParticleEffect"` dispatch's burst, under a key unique across both sources.</summary>
    private void AddParticleEffect(
        long key,
        SceneEffectDispatch dispatch,
        DemoTimeline timeline,
        IReadOnlyDictionary<string, ParticleSystem> systems,
        int? stop = null,
        Func<int, bool>? dormantAt = null,
        bool remember = false)
    {
        string? name = timeline.Dispatches.ParticleNames.Name(dispatch.HitBox);
        ParticleSystem? definition = name is null ? null : systems.GetValueOrDefault(name);
        ParticleControlPoint? point = definition is null ? null : DispatchedPoint(key, dispatch);

        // Once unplaced and once placed: the first frame after a load or seek comes before any pose pass.
        if (_renderLog.IsEnabled(LogLevel.Debug) && _loggedDispatches.Add((key * 2) + (point is null ? 0 : 1)))
        {
            string posed = _models.IsPosed(dispatch.Entity) ? " (posed)" : " (not posed)";

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"particle dispatch {key:x} at tick {dispatch.Tick}: {name ?? "(unnamed " + dispatch.HitBox + ")"} " +
                    $"{(definition is null ? "NO DEFINITION" : "loaded")}, entity {dispatch.Entity} attach " +
                    $"{dispatch.DamageType} point {dispatch.Attachment}: " +
                    $"{(point is null ? "NOT PLACED" + posed : "placed")}"));
        }

        // **An owned effect whose owner is not posed is offered undrawn** rather than not at all: unoffered, it would fade,
        // be retired, and be replayed from its dispatch tick when the owner is posed again — tens of thousands of steps for
        // a long-lived effect. Its point is a placeholder that an undrawn effect never reads.
        bool undrawn = definition is not null && point is null && dormantAt is not null;

        if (undrawn)
        {
            point = ParticleControlPoint.Unoriented(Vector3.Zero);
        }

        if (definition is not null && point is { } at)
        {
            ParticleBurst burst = new(key, definition, at, dispatch.Tick, DispatchedEnd(dispatch), stop)
            {
                DormantAt = dormantAt,
                Undrawn = undrawn,
            };

            if (remember)
            {
                Offer(burst);
            }
            else
            {
                _burstsNow.Add(burst);
            }
        }
    }

    /// <summary>Control point 1 of a dispatched effect: `m_ControlPoint1` when sent, else `m_vStart` where the callback sets it.</summary>
    private static ParticleControlPoint? DispatchedEnd(SceneEffectDispatch dispatch)
    {
        if (dispatch.HasControlPoint1)
        {
            return ParticleControlPoint.Unoriented(
                new Vector3(dispatch.ControlPoint1.X, dispatch.ControlPoint1.Y, dispatch.ControlPoint1.Z));
        }

        return (dispatch.Flags & DispatchFromEntity) == 0 || dispatch.DamageType == AttachCustomOrigin
            ? ParticleControlPoint.Unoriented(new Vector3(dispatch.Start.X, dispatch.Start.Y, dispatch.Start.Z))
            : null;
    }

    /// <summary>Control point 0 of a dispatched effect, or null when the callback would create nothing here.</summary>
    private ParticleControlPoint? DispatchedPoint(long index, SceneEffectDispatch dispatch)
    {
        bool fromEntity = (dispatch.Flags & DispatchFromEntity) != 0;

        if (fromEntity && dispatch.DamageType == AttachPointFollow)
        {
            return _models.AttachmentPoint(dispatch.Entity, dispatch.Attachment);
        }

        if (_particleDispatchPoints.TryGetValue(index, out ParticleControlPoint fixedPoint))
        {
            return fixedPoint;
        }

        ParticleControlPoint? point;

        if (!fromEntity || dispatch.DamageType == AttachCustomOrigin)
        {
            if (fromEntity && !_models.IsPosed(dispatch.Entity))
            {
                return null;
            }

            (float fx, float fy, float fz) = AngleVectors.Forward(dispatch.Angles.X, dispatch.Angles.Y);
            (float rx, float ry, float rz) = AngleVectors.Right(dispatch.Angles.X, dispatch.Angles.Y, dispatch.Angles.Z);
            (float ux, float uy, float uz) = AngleVectors.Up(dispatch.Angles.X, dispatch.Angles.Y, dispatch.Angles.Z);

            point = new ParticleControlPoint(
                new Vector3(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z),
                new Vector3(fx, fy, fz),
                new Vector3(rx, ry, rz),
                new Vector3(ux, uy, uz));
        }
        else
        {
            point = dispatch.DamageType == AttachPoint ? _models.AttachmentPoint(dispatch.Entity, dispatch.Attachment) : null;
        }

        if (point is { } found)
        {
            _particleDispatchPoints[index] = found;
        }

        return point;
    }
}
