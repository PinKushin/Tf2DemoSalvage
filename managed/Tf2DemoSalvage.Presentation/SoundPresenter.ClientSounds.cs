using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's client-made sounds are decided against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Game">The opened install, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Eye">The camera, for the footstep log's distance.</param>
/// <param name="Source">Where the shown moment's players come from, and the recorder's prediction.</param>
/// <param name="ShownTick">The tick the moment was sampled at.</param>
/// <param name="Playing">Whether the demo is playing.</param>
/// <param name="Silent">Whether this pass is one nobody hears, a replay for the decals.</param>
/// <param name="DemoFrameSeconds">The demo time this frame covered: zero while paused.</param>
public readonly record struct ClientSoundFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    GameContent? Game,
    int Tick,
    Vector3 Eye,
    IMomentSource? Source,
    double ShownTick,
    bool Playing,
    bool Silent,
    double DemoFrameSeconds);

/// <summary>What the map, the install and the corpse pass hold that these sounds read, as data (D214).</summary>
/// <param name="Level">The map's geometry, or null with no map.</param>
/// <param name="ImpactDecals">The map's surfaces by trace, or null when its content would not load.</param>
/// <param name="Doors">The map's solid brush entities, which are ground underfoot.</param>
/// <param name="GameSurfaces">The install's surface properties, or null with no install.</param>
/// <param name="CorpseSurfaces">The surface properties the corpse pass simulates with.</param>
/// <param name="CorpseRoots">Where each simulated corpse's root body is.</param>
internal sealed record ClientSoundWorld(
    MapLevel? Level,
    ImpactDecals? ImpactDecals,
    SolidBrushEntities Doors,
    VphysicsSurfaceProps? GameSurfaces,
    VphysicsSurfaceProps CorpseSurfaces,
    IReadOnlyDictionary<int, Vector3> CorpseRoots);

/// <summary>
/// The sounds the client makes each frame and no demo carries (B172): an animation event's sound and TF2's footstep, the
/// recorder's predicted landing, a corpse's impacts and scrapes.
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 4), where it was `StepAnimationSounds`, `StepLandingSounds`
/// and `StepCorpseSounds` with the eight methods they called and their state: the same arithmetic, the same order, the
/// same log lines in the same category. The view's side is one <see cref="StepClientSounds"/> a frame, the two
/// callbacks the corpse pass is given, and <see cref="ForgetCorpseSounds"/> with a new record.
///
/// **In the sound presenter rather than beside it**: each of these ends in <see cref="Emit"/>, and a second presenter
/// calling this one would be the sideways reach D92 forbids.
/// </remarks>
public sealed partial class SoundPresenter
{
    /// <summary>Where the footstep, animation event and corpse lines are logged: the render log, as before the move.</summary>
    public ILogger ClientSoundLog { get; set; } = NullLogger.Instance;

    /// <summary>The sounds emitted and not yet started, for a test of what a frame emitted.</summary>
    internal IReadOnlyList<SceneSound> Emitted => _emitted;

    /// <summary>The frame being stepped, for the methods <see cref="StepClientSounds"/> calls.</summary>
    private ClientSoundFrame _frame;

    /// <summary>What that frame's map, install and corpses hold.</summary>
    private ClientSoundWorld _world = new(null, null, SolidBrushEntities.None, null, new VphysicsSurfaceProps([]), new Dictionary<int, Vector3>());

    /// <summary>The posed entities the frame's events fired on.</summary>
    private IPosedEntities? _models;

    /// <summary>The map, install and corpse surfaces <see cref="_world"/> was read out of.</summary>
    private (LoadedMap? Loaded, GameContent? Game, VphysicsSurfaceProps? Corpses) _worldOf;

    /// <summary>This frame's animation event sounds, predicted landings and corpse sounds, in that order.</summary>
    /// <param name="frame">What this frame is decided against.</param>
    /// <param name="models">The view's model set: the events its pose fired, and its corpses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
    public void StepClientSounds(in ClientSoundFrame frame, EntityModelSet models)
    {
        ArgumentNullException.ThrowIfNull(models);

        if (!ReferenceEquals(frame.Loaded, _worldOf.Loaded) || !ReferenceEquals(frame.Game, _worldOf.Game) ||
            !ReferenceEquals(models.Corpses.Surfaces, _worldOf.Corpses))
        {
            _worldOf = (frame.Loaded, frame.Game, models.Corpses.Surfaces);
            _world = new ClientSoundWorld(
                frame.Loaded?.Level,
                frame.Loaded?.ImpactDecals,
                frame.Loaded?.Doors ?? SolidBrushEntities.None,
                frame.Game?.Surfaces,
                models.Corpses.Surfaces,
                models.Corpses.Roots);
        }

        StepClientSoundsOver(frame, _world, models);
    }

    /// <summary>The same, over what the map, the install and the corpses hold as data and any posed entities (D214).</summary>
    /// <param name="frame">What this frame is decided against; its map and install are not read here.</param>
    /// <param name="world">What they hold.</param>
    /// <param name="models">The posed entities.</param>
    internal void StepClientSoundsOver(in ClientSoundFrame frame, ClientSoundWorld world, IPosedEntities models)
    {
        _frame = frame;
        _world = world;
        _models = models;

        StepAnimationSounds(frame.Tick, frame.Eye);
        StepLandingSounds(frame.Tick);
        StepCorpseSounds(frame.Tick);
    }

    /// <summary>What the corpse pass heard collide, for the frame that reaches its tick.</summary>
    /// <param name="tick">When.</param>
    /// <param name="sound">The impact.</param>
    public void CorpseImpactHeard(int tick, Tf2DemoSalvage.Animation.Animating.PhysicsImpactSound sound)
    {
        if (!_heardCorpseSounds.TryGetValue(tick, out List<Tf2DemoSalvage.Animation.Animating.PhysicsImpactSound>? heard))
        {
            _heardCorpseSounds[tick] = heard = [];
        }

        heard.Add(sound);
    }

    /// <summary>What the corpse pass reported scraping, for the frame that reaches its tick.</summary>
    /// <param name="tick">When.</param>
    /// <param name="friction">The report.</param>
    public void CorpseFrictionHeard(int tick, CorpseFriction friction) => _heardCorpseFrictions.Add((tick, friction));

    /// <summary>Forgets what the corpse pass has said and nobody has played: a new record is a new death list.</summary>
    public void ForgetCorpseSounds()
    {
        _heardCorpseSounds.Clear();
        _heardCorpseFrictions.Clear();
    }

    /// <summary>`AE_CL_PLAYSOUND`, the old-system client event that names a sound script.</summary>
    public const int PlaySoundEvent = 5004;

    /// <summary>TF2's footstep event, answered by `C_TFPlayer::FireEvent`.</summary>
    public const int FootstepEvent = 7001;

    private readonly Footsteps _footsteps = new();

    /// <summary>The last ground trace, for the footstep log.</summary>
    private BspTrace _lastGround;

    /// <summary>Players at the shown moment, for the events that name one; reused.</summary>
    private readonly List<ScenePlayer> _eventPlayers = [];

    /// <summary>The animation events taken this frame; reused.</summary>
    private readonly List<FiredAnimationEvent> _firedEvents = [];

    /// <summary>One footstep event, gated by the flag list the loaded demo was written with (B501).</summary>
    /// <param name="footsteps">The viewer's footstep state.</param>
    /// <param name="tick">When.</param>
    /// <param name="stepper">The player.</param>
    /// <param name="ground">What is under them.</param>
    /// <param name="named">A surface by name.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <param name="timeline">The loaded demo, whose `FlagLayout` decides which bits are frozen and at controls.</param>
    /// <returns>The step, or null.</returns>
    internal static SceneSound? Footstep(
        Footsteps footsteps,
        int tick,
        ScenePlayer stepper,
        StepSurface? ground,
        Func<string, StepSurface?> named,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts,
        DemoTimeline timeline) =>
        footsteps.Step(tick, stepper, ground, named, scripts, timeline.FlagLayout);

    /// <summary>The sounds this frame's animation events make: `C_BaseAnimating::FireEvent` and TF2's footstep (B172).</summary>
    /// <remarks>
    /// `AE_CL_PLAYSOUND` (`c_baseanimating.cpp:3988`) emits its options from attachment 1, or the origin when the model
    /// has none. 7001 is `C_TFPlayer::FireEvent` (`c_tf_player.cpp:9066`): the surface a hull trace 64 units down finds
    /// (`C_BasePlayer::GetGroundSurface`), then <see cref="Footsteps"/>. The cube starts 24 up so its bottom is the hull's
    /// and its footprint is the hull's, which is all a trace straight down can touch. *Interpolated:* `level.Trace`'s own
    /// mask stands in for `MASK_PLAYERSOLID_BRUSHONLY`.
    /// </remarks>
    private void StepAnimationSounds(int tick, Vector3 eye)
    {
        // Taken before any early return: an event is crossed once, so one this frame does not sound is dropped, never
        // sounded later — and this runs on every camera upload, paused or not.
        _firedEvents.Clear();
        _models?.TakeFiredEvents(_firedEvents);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Silent || Scripts is not { } scripts || _firedEvents.Count == 0)
        {
            return;
        }

        // Filled on the first event that names a player, once per frame.
        _eventPlayers.Clear();

        foreach (FiredAnimationEvent fired in _firedEvents)
        {
            if (ClientSoundLog.IsEnabled(LogLevel.Debug))
            {
                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                ClientSoundLog.LogDebug(
                    "{Message}",
                    string.Create(CultureInfo.InvariantCulture, $"animation event tick {tick} entity {fired.EntityIndex}: {fired.Event.Id} {fired.Event.Options} in sequence {fired.Sequence} at cycle {fired.Cycle:0.000}"));
            }

            switch (fired.Event.Id)
            {
                case PlaySoundEvent:
                    (float X, float Y, float Z) at = _models?.AttachmentPoint(fired.EntityIndex, 1) is { } point
                        ? (point.At.X, point.At.Y, point.At.Z)
                        : fired.Origin;

                    if (EntitySounds.Emit(tick, fired.EntityIndex, fired.Event.Options, at, scripts.Entries, emitted: true, ClientSoundPhase.Simulate) is { } sound)
                    {
                        Emit(sound);
                    }

                    break;

                case FootstepEvent:
                    ScenePlayer? stepper = Stepper(fired.EntityIndex);
                    StepSurface? ground = stepper is { } p ? GroundSurface(p) : null;
                    SceneSound? step = stepper is { } q && _frame.Timeline is { } shown ? Footstep(_footsteps, tick, q, ground, NamedSurface, scripts.Entries, shown) : null;

                    if (step is { } played)
                    {
                        Emit(played);
                    }

                    if (ClientSoundLog.IsEnabled(LogLevel.Debug))
                    {
                        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                        string who = stepper is { } s
                            ? string.Create(
                                CultureInfo.InvariantCulture,
                                $"speed {s.Speed:0} of max {s.MaxSpeed}, flags {s.Flags}, water {s.WaterLevel}, ground {ground?.Material} {ground?.Right}, {Vector3.Distance(eye, new Vector3(s.X, s.Y, s.Z)):0} from the camera, trace {_lastGround.Fraction:0.000} texinfo {_lastGround.Texinfo} terrain {_lastGround.DisplacementTexdata}")
                            : "no player at the shown moment";

                        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                        ClientSoundLog.LogDebug(
                            "{Message}",
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"footstep tick {tick} entity {fired.EntityIndex}: {who}; {(step is { } h ? h.Name : "silent")}"));
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Impacts the corpse pass raised this frame, by tick — from the record or the live replay, whichever posed them.</summary>
    private readonly Dictionary<int, List<Tf2DemoSalvage.Animation.Animating.PhysicsImpactSound>> _heardCorpseSounds = [];

    /// <summary>`CPhysicsSystem::m_impactSounds`: this frame's merged list; reused.</summary>
    private readonly List<Tf2DemoSalvage.Animation.Animating.PhysicsImpactSound> _corpseFrameSounds = [];

    /// <summary>`Friction` reports the corpse pass raised this frame, in order.</summary>
    private readonly List<(int Tick, CorpseFriction Friction)> _heardCorpseFrictions = [];

    /// <summary>The corpses' scrape loops (`friction_t`).</summary>
    private readonly PhysicsFrictionSounds _frictionSounds = new();

    /// <summary>`gpGlobals->curtime` for the scrape loops: the demo time the frames have covered.</summary>
    private double _frictionClock;

    /// <summary>The tick the scrape loops last stepped at; a step back is a seek, which forgets them.</summary>
    private int _frictionTick = int.MinValue;

    /// <summary>A corpse's physics impacts — `CCollisionEvent::PostCollision` and `PlayImpactSounds`, per tick (B172).</summary>
    /// <remarks>
    /// **One source: what <see cref="CorpsePhysics.ImpactHeard"/> raised**, from whichever path posed the corpse. Reading the
    /// record here as well lost every impact of f12's 26990 death after a seek, where playback crosses from the live replay to the
    /// record mid-death (2026-09-23). Only the last <see cref="CorpsePhysics.HearingCatchUp"/> ticks play — a rebuild replays
    /// from the death, and those are older than any frame.
    /// </remarks>
    private void StepCorpseSounds(int tick)
    {
        if (!_frame.Silent && Scripts is { } scripts)
        {
            // **One list for the frame**, as `CPhysicsSystem::PhysicsSimulate` keeps `m_impactSounds` across every tick its
            // `Simulate( frametime )` crosses and plays it once — merged per tick instead, f12 played 29 impacts to TF2's 12.
            _corpseFrameSounds.Clear();

            foreach (int at in _heardCorpseSounds.Keys.Order())
            {
                if (at <= tick && at > tick - CorpsePhysics.HearingCatchUp)
                {
                    foreach (Tf2DemoSalvage.Animation.Animating.PhysicsImpactSound impact in _heardCorpseSounds[at])
                    {
                        Tf2DemoSalvage.Animation.Animating.PhysicsImpactSoundList.Add(_corpseFrameSounds, impact);
                    }
                }
            }

            // `PlayImpactSounds` walks the list from the back.
            for (int index = _corpseFrameSounds.Count - 1; index >= 0; index--)
            {
                if (PhysicsImpactSounds.For(tick, _corpseFrameSounds[index], _world.CorpseSurfaces, scripts.Entries) is { } sound)
                {
                    Emit(sound);
                }
            }
        }

        // Consumed; a seek backwards leaves nothing ahead of it to play.
        _heardCorpseSounds.Clear();
        StepCorpseScrapes(tick);
    }

    /// <summary>The corpses' scrape loops for this frame — `Friction` reports in, stale loops out (<see cref="PhysicsFrictionSounds"/>).</summary>
    private void StepCorpseScrapes(int tick)
    {
        if (tick < _frictionTick)
        {
            _frictionSounds.Clear();
        }

        _frictionTick = tick;
        _frictionClock += _frame.DemoFrameSeconds;

        List<CorpseFriction> frictions = [];

        foreach ((int at, CorpseFriction friction) in _heardCorpseFrictions)
        {
            if (at <= tick && at > tick - CorpsePhysics.HearingCatchUp)
            {
                frictions.Add(friction);
            }
        }

        _heardCorpseFrictions.Clear();

        if (frictions.Count > 0 && ClientSoundLog.IsEnabled(LogLevel.Debug))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            ClientSoundLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"corpse friction at {tick} (clock {_frictionClock:0.###}): {string.Join(", ", frictions.Select(static friction => $"{friction.Entity} {friction.Energy:0.#} {friction.SurfaceProps}/{friction.SurfacePropsHit}"))}"));
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Silent || Scripts is not { } scripts || _world.CorpseSurfaces is not { } surfaces)
        {
            return;
        }

        foreach (SceneSound sound in _frictionSounds.Step(tick, _frictionClock, frictions, CorpseAt, surfaces, scripts.Entries))
        {
            if (ClientSoundLog.IsEnabled(LogLevel.Debug))
            {
                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                ClientSoundLog.LogDebug(
                    "{Message}",
                    string.Create(CultureInfo.InvariantCulture, $"corpse scrape {(sound.IsStop ? "stop" : "start")} at {tick}: {sound.EntityIndex} {sound.Name} volume {sound.Volume:0.###}"));
            }

            Emit(sound);
        }
    }

    /// <summary>Where a corpse's root body is, for a sound it starts; the origin when it is not simulated.</summary>
    private (float X, float Y, float Z) CorpseAt(int entity, int tick) =>
        _world.CorpseRoots.TryGetValue(entity, out Vector3 root) ? (root.X, root.Y, root.Z) : default;

    /// <summary>The player an event fired on, as drawn at the shown moment.</summary>
    private ScenePlayer? Stepper(int entity)
    {
        if (_eventPlayers.Count == 0 && _frame.Source is { } source)
        {
            source.PlayersAt(_frame.ShownTick, _eventPlayers, _frame.Playing);
        }

        foreach (ScenePlayer player in _eventPlayers)
        {
            if (player.EntityIndex == entity)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>`C_BasePlayer::GetGroundSurface`: the surface under the player's hull, or null for none within 64.</summary>
    private StepSurface? GroundSurface(ScenePlayer player)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_world is not { Level: { } level, ImpactDecals: { } decals } loaded)
        {
            return null;
        }

        // `MASK_PLAYERSOLID_BRUSHONLY` keeps brush entities — a door underfoot is ground.
        BspTrace trace = level.GroundTrace((player.X, player.Y, player.Z), loaded.Doors.At(_frame.Tick));

        _lastGround = trace;

        return trace.Fraction >= 1f ? null : SurfaceAt(decals.SurfacePropOfTrace(trace));
    }

    /// <summary>The landings prediction ran for the first time this frame (B172).</summary>
    private readonly List<PredictedLanding> _landings = [];

    /// <summary>
    /// The POV recorder's landing sounds: prediction's <c>CheckFalling</c> → <c>PlayStepSound</c> (B172). Only the recorder —
    /// no client runs movement for anyone else, and the server sends their landings only beyond the PVS
    /// (<c>baseplayer_shared.cpp:717-722</c>), which the demo already carries.
    /// </summary>
    private void StepLandingSounds(int tick)
    {
        _landings.Clear();

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Source is not TimelineMoments { Prediction: { } prediction })
        {
            return;
        }

        prediction.TakeLandings(_landings);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Silent || Scripts is not { } scripts || _frame.Timeline?.RecorderEntityIndex is not { } recorder)
        {
            return;
        }

        foreach (PredictedLanding landing in _landings)
        {
            StepSurface? surface = landing.Surface is { } s ? new StepSurface(s.GameMaterial, s.Sounds.StepLeft, s.Sounds.StepRight) : null;

            if (_footsteps.Land(tick, recorder, (landing.Origin.X, landing.Origin.Y, landing.Origin.Z), surface, landing.Volume, scripts.Entries) is { } sound)
            {
                Emit(sound);
            }
        }
    }

    private StepSurface? NamedSurface(string name) =>
        _world.GameSurfaces is { } surfaces && surfaces.GetSurfaceIndex(name) is >= 0 and var index ? SurfaceAt(index) : null;

    private StepSurface? SurfaceAt(int index) =>
        _world.GameSurfaces?.GetSurfaceData(index) is { } surface
            ? new StepSurface(surface.GameMaterial, surface.Sounds.StepLeft, surface.Sounds.StepRight)
            : null;
}
