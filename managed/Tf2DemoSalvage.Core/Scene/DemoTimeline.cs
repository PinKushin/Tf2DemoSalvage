using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Diagnostics;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One player, at one moment.</summary>
/// <param name="EntityIndex">The entity's slot, stable for as long as it lives.</param>
/// <param name="X">World position across.</param>
/// <param name="Y">World position along.</param>
/// <param name="Z">World height.</param>
/// <param name="Team">Which team, when the demo has said; 2 is RED and 3 is BLU.</param>
/// <param name="Health">Current health, when known.</param>
/// <param name="PlayerClass">Which of the nine classes, when known; 1 is Scout through 9 Engineer.</param>
/// <param name="Yaw">Which way the body faces, in degrees, interpolated with the position.</param>
/// <param name="Speed">How fast the player is moving horizontally, in units a second.</param>
/// <param name="LifeState">0 alive, 1 dying, 2 dead; absent means alive.</param>
/// <param name="MoveX">The <c>move_x</c> pose parameter: how much of the motion is forward.</param>
/// <param name="MoveY">The <c>move_y</c> pose parameter: how much of it is sideways.</param>
/// <param name="Flags">
/// The player's <c>m_fFlags</c>, carrying the crouch and ground bits, or <c>null</c> when the
/// recording did not say. Declared in <c>DT_LocalPlayerExclusive</c>, so a POV demo carries it for
/// the recorder alone while a SourceTV recording carries it for every player.
/// </param>
/// <param name="AirborneSeconds">
/// How long since this player left the ground, or <c>null</c> when they are on it or the recording
/// did not say. Splits a jump into its push-off and its float, which the engine does from
/// <c>m_flJumpStartTime</c> — a moment no demo records, so this is derived from when
/// <c>FL_ONGROUND</c> cleared.
/// </param>
/// <param name="Airwalking">
/// Whether this player has risen fast enough to air-walk since leaving the ground. The engine's
/// test is <c>vecVelocity.z &gt; 300.0f || m_bInAirWalk</c>, so it latches until they land — this
/// is that latch. It says nothing about whether the CLASS air-walks, which is the class script's
/// answer and the caller's to apply.
/// </param>
/// <param name="EyePitch">
/// How far up or down the player is looking, in degrees, or <c>null</c> when the recording did not
/// send eye angles. Drives the <c>body_pitch</c> pose parameter, which aims the torso —
/// <c>ComputePoseParam_AimPitch</c> sets it to the NEGATED eye pitch.
/// </param>
/// <param name="EyeYaw">
/// Where the player is LOOKING, which is not where their body is drawn once they turn on the spot.
/// <paramref name="Yaw"/> is the feet.
/// </param>
/// <param name="AimYaw">
/// The <c>body_yaw</c> pose parameter — how far the torso is twisted from the feet, already negated
/// as the engine negates it.
/// </param>
/// <param name="WaterLevel">
/// How deep in water they are — 0 dry, 1 feet, 2 waist, 3 eyes (<c>player.cpp:1961</c>). Waist deep
/// is where the animation changes: both <c>HandleJumping</c> and <c>HandleSwimming</c> test
/// <c>&gt;= WL_Waist</c>, so a player who leaps into water swims rather than falling with their
/// legs tucked.
/// </param>
/// <param name="ActiveWeapon">
/// Which entity is the weapon in hand, or <c>null</c> when none is held. Decoded from
/// <c>m_hActiveWeapon</c> on <c>DT_BaseCombatCharacter</c>, so it arrives for every player in the
/// PVS rather than for the recorder alone.
/// </param>
/// <param name="WeaponClass">
/// That weapon's server class, such as <c>CTFRevolver</c>. It is what decides the suffix on every
/// body activity — <c>CTFWeaponBase::ActivityList</c> picks an <c>acttable_t</c> from the weapon's
/// role, so a medic's medigun drives <c>ACT_MP_RUN_SECONDARY</c> where a scattergun drives
/// <c>ACT_MP_RUN_PRIMARY</c>.
/// </param>
/// <param name="WeaponItem">
/// Which item in TF2's schema that weapon is, or <c>null</c> when the demo predates the item system
/// or nothing is held. It is what turns "a scattergun" into a model path: the weapon a player sees
/// in their own hands is a client-side entity a recording cannot carry, so the index into
/// <c>items_game.txt</c> is the only route to it. See <c>EntityState.ItemDefinitionIndex</c>.
/// </param>
/// <param name="Drawn">
/// Whether the engine would draw this player's model, which is <c>EF_NODRAW</c> rather than
/// anything about life state. TF2 turns the player off on death — <c>AddEffects( EF_NODRAW |
/// EF_NOSHADOW )</c> at the end of <c>CreateRagdollEntity</c>, <c>tf_player.cpp:15637</c> — and
/// spawns a separate <c>CTFRagdoll</c> to be the corpse. A dead player stays in this list as data
/// for the scoreboard and the kill feed with this false.
/// </param>
/// <param name="ObserverMode">
/// What the player is watching through — <c>m_iObserverMode</c>, <c>shareddefs.h:492</c> — or
/// <c>null</c> when the recording never said, which means <see cref="ObserverModes.None"/>. It is
/// the engine's own answer to "is this a first-person view", and the reason it matters here is that
/// a player who goes to spectator is still ALIVE: liveness cannot distinguish them, and this can.
/// See <see cref="ScenePlayer.InFirstPersonView"/>.
/// </param>
/// <remarks>
/// **Not everything here is playing.** A spectator and a SourceTV camera are <c>CTFPlayer</c>
/// entities with real positions that fly around the map, and drawing them puts dots where nobody
/// is standing. The team number separates them, and it is the engine's own:
/// <c>TEAM_UNASSIGNED</c> is 0, <c>TEAM_SPECTATOR</c> is 1, and TF2's own
/// <c>TF_TEAM_RED = LAST_SHARED_TEAM + 1</c> makes RED 2 and BLU 3.
/// </remarks>
public readonly record struct ScenePlayer(
    int EntityIndex,
    float X,
    float Y,
    float Z,
    int? Team,
    int? Health,
    int? PlayerClass,
    float Yaw = 0f,
    float Speed = 0f,
    int? LifeState = null,
    float MoveX = 0f,
    float MoveY = 0f,
    int? Flags = null,
    bool Drawn = true,
    float? AirborneSeconds = null,
    bool Airwalking = false,
    float? EyePitch = null,
    float? EyeYaw = null,
    float? AimYaw = null,
    int? WaterLevel = null,
    int? ActiveWeapon = null,
    string? WeaponClass = null,
    int? WeaponItem = null,
    int? ObserverMode = null)
{
    /// <summary>Whether the player is crouched, when the recording says.</summary>
    /// <remarks>
    /// <c>FL_DUCKING</c>. Null flags mean the recording never said, which is every player but the
    /// recorder in a POV demo — a SourceTV recording carries them for all of them. So this answers
    /// false for "not crouched" and for "not known" alike, and a caller that needs to tell them
    /// apart must look at <see cref="Flags"/> itself.
    ///
    /// **Written as a null check rather than as a masked comparison, because the obvious form is
    /// wrong.** <c>(Flags &amp; Ducking) != 0</c> lifts to a nullable comparison, and in C# a null
    /// compared with <c>!=</c> to zero is TRUE — so every player whose flags never arrived read as
    /// permanently crouched. Caught by the completeness test that checks each property differs from
    /// its default, which is the one place that shape shows up as a value rather than as a crash.
    /// </remarks>
    public bool IsCrouched =>
        Flags is { } ducking && (ducking & PlayerActivityState.Ducking) != 0;

    /// <summary>Whether the player is off the ground, when the recording says.</summary>
    /// <remarks>
    /// <c>FL_ONGROUND</c> absent. Null flags answer false rather than true, deliberately: an
    /// unknown state should draw a player standing on the floor rather than permanently falling.
    /// </remarks>
    public bool IsAirborne => Flags is { } flags && (flags & PlayerActivityState.OnGround) == 0;

    /// <summary>Whether this is someone actually playing, rather than watching.</summary>
    /// <remarks>
    /// **The distinction a map view has to make.** Team 0 is unassigned and team 1 is spectator;
    /// only 2 and 3 are playing. A viewer that draws everything shows the SourceTV camera as a
    /// player, and it moves convincingly - it follows the action, because that is its job.
    /// </remarks>
    public bool IsPlaying => Team is SceneTeams.Red or SceneTeams.Blu;

    /// <summary>Whether this player is alive and standing in the world.</summary>
    /// <remarks>
    /// **A dead player is still on a team and still has a position — the position of whoever they
    /// are spectating.** So a corpse drawn as a player appears standing inside the living player
    /// it is watching, and several of them stack into one heap. That is what "two soldiers in a
    /// ball" was.
    ///
    /// **Absent means alive**, because <c>LIFE_ALIVE</c> is zero and a delta-compressed format
    /// only sends what changed. Reading absence as unknown would hide everyone who has not died.
    /// </remarks>
    public bool IsAlive => LifeState is null or 0;

    /// <summary>Whether the engine would consider this player's view first person.</summary>
    /// <remarks>
    /// **<c>C_BasePlayer::LocalPlayerInFirstPersonView</c>, <c>c_baseplayer.cpp:1919</c>:**
    ///
    /// <code>
    ///   int ObserverMode = pLocalPlayer->GetObserverMode();
    ///   if ( ( ObserverMode == OBS_MODE_NONE ) || ( ObserverMode == OBS_MODE_IN_EYE ) )
    ///   {
    ///       return !input->CAM_IsThirdPerson() &amp;&amp; ...;
    ///   }
    ///
    ///   // Not looking at the local player, e.g. in a replay in third person mode or freelook.
    ///   return false;
    /// </code>
    ///
    /// An allowlist of two, and the SDK's own comment warns against treating the enum as ordered:
    /// <c>OBS_MODE_POI</c> was inserted at 6 *"due to tons of hard-coded '&lt;ROAMING' enum
    /// compares"*. So this compares against the two values rather than against a threshold.
    ///
    /// **Absent means <see cref="ObserverModes.None"/>**, because zero is the default and a
    /// delta-compressed format sends only what changed — the same rule as
    /// <see cref="IsAlive"/>. A recording that never mentions the field is a recording of someone
    /// who never observed, not an unknown.
    /// </remarks>
    public bool InFirstPersonView =>
        ObserverMode is null or ObserverModes.None or ObserverModes.InEye;
}

/// <summary>The engine's observer modes, <c>shareddefs.h:492</c>.</summary>
/// <remarks>
/// Sent as three bits unsigned (<c>player.cpp:8184</c>), which is exactly enough for 0..7 — every
/// value the enum defines fits and none is unrepresentable.
///
/// **In <c>DT_BasePlayer</c> proper rather than <c>DT_LocalPlayerExclusive</c>**, so it arrives for
/// every player in any recording, not only for the one holding the camera.
/// </remarks>
public static class ObserverModes
{
    /// <summary>Not in spectator mode — playing.</summary>
    public const int None = 0;

    /// <summary>The death cam animation.</summary>
    public const int DeathCam = 1;

    /// <summary>Zooms to a target and freeze-frames on them.</summary>
    public const int FreezeCam = 2;

    /// <summary>A fixed camera position.</summary>
    public const int Fixed = 3;

    /// <summary>Following a player in first person.</summary>
    public const int InEye = 4;

    /// <summary>Following a player in third person.</summary>
    public const int Chase = 5;

    /// <summary>A PASSTIME point of interest.</summary>
    /// <remarks>
    /// **Inserted in the MIDDLE of the enum**, and the SDK says why: *"added in the middle of the
    /// enum due to tons of hard-coded '&lt;ROAMING' enum compares"*. Anything here comparing modes
    /// by ordering rather than by value would be wrong for a reason Valve documented in advance.
    /// </remarks>
    public const int PointOfInterest = 6;

    /// <summary>Free roaming — where TF2 puts a player who goes to spectator.</summary>
    public const int Roaming = 7;
}

/// <summary>The engine's team numbers.</summary>
/// <remarks>
/// From <c>shareddefs.h</c> and <c>tf_shareddefs.h</c>: the first two are shared by every Source
/// game, and TF2 numbers its own from <c>LAST_SHARED_TEAM + 1</c>.
/// </remarks>
public static class SceneTeams
{
    /// <summary>Connected but not yet on a team.</summary>
    public const int Unassigned = 0;

    /// <summary>Watching rather than playing; includes a SourceTV camera.</summary>
    public const int Spectator = 1;

    /// <summary>RED.</summary>
    public const int Red = 2;

    /// <summary>BLU.</summary>
    public const int Blu = 3;
}

/// <summary>Where everyone was at one tick.</summary>
/// <param name="Tick">The demo tick this was recorded at.</param>
/// <param name="Players">Every player with a known position.</param>
public readonly record struct TimelineFrame(int Tick, IReadOnlyList<ScenePlayer> Players);

/// <summary>
/// A demo turned into player positions over time.
/// </summary>
/// <remarks>
/// **Built once and kept, because a demo cannot be seeked.** It is a forward-only stream of deltas
/// against the previous state, so there is no way to know where anyone stood at tick 4,000 without
/// replaying every tick before it. Scrubbing backwards would mean re-reading from the start each
/// time, which is why the whole thing is walked once and the answers stored.
///
/// The cost is bounded and small next to the map: a few thousand frames of at most a couple of
/// dozen players, against a 24 MB BSP and its textures.
///
/// **A frame per packet, not per tick.** Positions arrive with <c>svc_PacketEntities</c> and the
/// server does not send one every tick, so the frames are irregular by nature and
/// <see cref="PlayersAt(int)"/> answers with the most recent one rather than requiring an exact
/// match.
/// </remarks>
public sealed class DemoTimeline
{
    /// <summary>The entity that carries every player's team and class.</summary>
    /// <remarks>
    /// **Team and class do not travel on the player entity, and on modern demos they are not there
    /// at all.** A positioned modern player carries only its health among the three; era demos do
    /// send <c>DT_BaseEntity.m_iTeamNum</c> on the player, which is what made this look like it
    /// worked before it was measured.
    ///
    /// Both live on a single <c>CTFPlayerResource</c> entity as arrays indexed by entity index —
    /// <c>m_iTeam.003</c>, <c>m_iPlayerClass.003</c> — which is one entity for the whole server
    /// rather than a copy per player.
    /// </remarks>
    private const string ResourceClass = "CTFPlayerResource";

    /// <summary>The class every player entity has, spectators and the SourceTV camera included.</summary>
    private const string PlayerClass = "CTFPlayer";

    /// <summary>The entity that carries the atmosphere.</summary>
    private const string FogControllerClass = "CFogController";


    private static readonly string[] TeamProperties =
    [
        "DT_BaseEntity.m_iTeamNum",
        "DT_BaseCombatCharacter.m_iTeamNum",
    ];

    private static readonly string[] HealthProperties =
    [
        "DT_BasePlayer.m_iHealth",
        "DT_TFPlayerScoringDataExclusive.m_iHealth",
    ];

    private readonly List<TimelineFrame> _frames;

    private readonly List<ScenePropTrack> _props;

    private readonly Dictionary<int, ScenePropTrack> _trackByEntity = [];

    private readonly List<ScenePropTrack> _playerTracks;

    /// <summary>Each packet's recorded camera and the tick it was stated at, in order.</summary>
    /// <remarks>
    /// A sorted list rather than a dictionary because the question asked is "what was the camera at
    /// or before this tick" — the viewer draws between packets, so an exact-match lookup answers
    /// nothing on most frames. Binary search over the ticks is what makes that cheap enough to do
    /// per frame.
    /// </remarks>
    private readonly List<(int Tick, RecordedView View)> _recordedViews = [];

    /// <summary>Every <c>hltv_chase</c> the director sent, in tick order.</summary>
    private readonly List<(int Tick, DirectorShot Shot)> _director = [];

    /// <summary>Whether the recording carries a director at all.</summary>
    /// <remarks>
    /// **False for a point-of-view demo, and that is the normal case rather than a gap.** Only a
    /// SourceTV recording has a director choosing shots; a POV demo carries the player's own camera
    /// and never sends <c>hltv_chase</c>.
    /// </remarks>
    public bool HasDirector => _director.Count > 0;

    /// <summary>What the director last asked for at or before a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The shot, or null when the recording has no director.</returns>
    /// <remarks>
    /// **The LAST shot at or before the tick, because a shot persists until the next one.** The
    /// director sends an event when it changes its mind, not every tick, so anything that sampled
    /// only the current tick would find a shot on one frame in a hundred and nothing on the rest.
    /// </remarks>
    public DirectorShot? DirectorAt(int tick)
    {
        if (_director.Count == 0 || tick < _director[0].Tick)
        {
            return null;
        }

        int low = 0;
        int high = _director.Count - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;

            if (_director[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _director[low].Shot;
    }

    /// <summary>Each tick a viewmodel was described, and what it said.</summary>
    /// <remarks>
    /// Sampled per tick rather than stored per frame, for the same reason the recorded views are:
    /// the viewer draws between packets and wants the most recent answer, not an exact match.
    /// </remarks>
    private readonly List<(int Tick, SceneViewmodel Weapon)> _viewmodels = [];

    /// <summary>Every change to the atmosphere, in tick order.</summary>
    /// <remarks>
    /// Recorded on change rather than per tick: a fog controller sends its state on entry and then
    /// rarely, so a per-tick list would be tens of thousands of identical entries for a handful of
    /// distinct values.
    /// </remarks>
    private readonly List<(int Tick, SceneFog Fog)> _fog = [];

    private readonly List<SceneSound> _sounds = [];

    private readonly List<(int Tick, SceneSoundscape Soundscape)> _soundscapes = [];

    /// <summary>Whether any viewmodel in this demo names an owner.</summary>
    /// <remarks>
    /// **This is what separates a point-of-view recording from a SourceTV one**, and it is a
    /// property of the whole demo rather than of one entity. A client receives only its own
    /// viewmodel and the server never says whose it is, so a POV demo names nobody; a SourceTV
    /// recording carries one per player and names each. Asked once here rather than per lookup so
    /// the answer cannot vary with the tick being drawn.
    /// </remarks>
    private readonly bool _viewmodelsNameOwners;

    private DemoTimeline(
        List<TimelineFrame> frames,
        List<ScenePropTrack>? props = null,
        List<ScenePropTrack>? playerTracks = null,
        List<(int Tick, RecordedView View)>? recordedViews = null,
        List<(int Tick, SceneViewmodel Weapon)>? viewmodels = null,
        List<(int Tick, SceneFog Fog)>? fog = null,
        List<SceneSound>? sounds = null,
        List<(int Tick, SceneSoundscape Soundscape)>? soundscapes = null,
        List<(int Tick, DirectorShot Shot)>? director = null)
    {
        _director = director ?? [];
        _soundscapes = soundscapes ?? [];
        _recordedViews = recordedViews ?? [];
        _viewmodels = viewmodels ?? [];
        _fog = fog ?? [];
        _sounds = sounds ?? [];

        _viewmodelsNameOwners =
            _viewmodels.Exists(recorded => recorded.Weapon.OwnerEntityIndex is not null);

        _frames = frames;
        _props = props ?? [];
        _playerTracks = playerTracks ?? [];

        // Last track wins where a slot was reused: the later occupant is the one still alive at
        // any tick a caller can ask about after it started, and At answers null before that.
        foreach (ScenePropTrack track in _props)
        {
            _trackByEntity[track.EntityIndex] = track;
        }

        foreach (ScenePropTrack track in _playerTracks)
        {
            _trackByEntity[track.EntityIndex] = track;
        }
    }

    /// <summary>Every model-bearing entity the demo carried, with its pose over time.</summary>
    public IReadOnlyList<ScenePropTrack> Props => _props;

    /// <summary>Every sound the recording plays, in tick order.</summary>
    /// <remarks>
    /// **A flat list rather than tracks, because a sound is an instant and not a state.** There is
    /// no "what is entity 12 sounding like at tick 4000" to answer — it made a noise at a tick and
    /// the player owns it from there. Kept in tick order so playback walks a cursor forward and a
    /// seek is a binary search, rather than a scan per frame.
    /// </remarks>
    public IReadOnlyList<SceneSound> Sounds => _sounds;

    /// <summary>Every player, with the pose the interpolator works from.</summary>
    /// <remarks>
    /// **Separate from <see cref="Props"/> because these carry no model.** A player's model is
    /// resolved from the installed game rather than from the demo — see
    /// <c>PlayerClassModels</c> — so a consumer walking <see cref="Props"/> to draw models would
    /// find entries it could only report as missing assets.
    /// </remarks>
    public IReadOnlyList<ScenePropTrack> PlayerTracks => _playerTracks;

    /// <summary>Every distinct model this recording will ever ask for.</summary>
    /// <returns>Model paths, without duplicates and in no particular order.</returns>
    /// <remarks>
    /// **For precaching, which is when the engine loads models too.**
    /// <c>CBaseEntity::PrecacheModel</c> sits behind <c>IsPrecacheAllowed()</c> and warns on an
    /// out-of-order precache, because Source loads models at level load rather than on sight.
    /// Packing on sight cost 385 ms in one frame when a crowd of props came into view (B163, D86).
    ///
    /// **Answered here rather than assembled by the caller**, because the three collections it has
    /// to reach into are not all public and one of them never was: <see cref="Props"/> and
    /// <see cref="PlayerTracks"/> are, and the viewmodels are private. A caller that knew about two
    /// of the three would precache most models and leave weapon switches to hitch — which is
    /// precisely the case a first-person viewer meets most often.
    ///
    /// **Player tracks are included even though they carry no model of their own.** Their path is
    /// empty or a placeholder, and an empty entry costs a packer nothing to reject; the class models
    /// a player actually wears come from the installed game and are the caller's to add.
    /// </remarks>
    public IEnumerable<string> ModelPaths()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ScenePropTrack track in _props)
        {
            if (track.ModelPath is { Length: > 0 } && seen.Add(track.ModelPath))
            {
                yield return track.ModelPath;
            }
        }

        foreach (ScenePropTrack track in _playerTracks)
        {
            if (track.ModelPath is { Length: > 0 } && seen.Add(track.ModelPath))
            {
                yield return track.ModelPath;
            }
        }

        // The weapons a first-person view puts in the player's hands. A weapon switch changes this
        // model, which is why leaving them out would leave a hitch every few seconds in exactly the
        // view the owner reported it from.
        foreach ((int _, SceneViewmodel weapon) in _viewmodels)
        {
            if (weapon.ModelPath is { Length: > 0 } && seen.Add(weapon.ModelPath))
            {
                yield return weapon.ModelPath;
            }
        }
    }

    /// <summary>Every distinct sound this recording will ever play.</summary>
    /// <returns>Sound names, without duplicates and in no particular order.</returns>
    /// <remarks>
    /// **The sibling of <see cref="ModelPaths"/>, and for the same reason** (D87, B163). Valve does
    /// not merely prefer to load audio at level load — <c>CBaseEntity::PrecacheSound</c> refuses to
    /// do it later: <c>SoundEmitterSystem.cpp:1497</c> is
    /// <c>if ( !CBaseEntity::IsPrecacheAllowed() )</c> followed by
    /// <c>Assert( !"CBaseEntity::PrecacheSound:  too late" )</c>. Decoding on first play is the same
    /// departure D86 caught for models, sitting behind the same guard.
    ///
    /// **Measured 2026-08-25, and it is what the owner had been hearing.** Of eleven slow frames in
    /// one run, six were dominated by the sound step at 27-91 ms while posing and drawing sat at
    /// 1.7-2.6 ms. Only ONE decode logged a stall, because the per-decode threshold is 30 ms and a
    /// frame that starts three sounds pays three decodes that each fall under it — which is why an
    /// instrument watching single decodes reported almost nothing while the frames were visibly
    /// freezing.
    ///
    /// **Names, not paths, because the name is what the schedule replays.** A name may be a script
    /// rather than a file — see <c>SoundScript</c> — and resolving it is the caller's job, exactly
    /// as it is when the sound is played.
    ///
    /// A stop carries a name but plays nothing, so decoding it would read a file to throw it away.
    /// </remarks>
    /// <remarks>
    /// Not <c>SoundNames</c>, which is the <c>soundprecache</c> string table READER
    /// (<c>Core/Net/SoundNames.cs</c>) and is used by name inside this very class.
    /// </remarks>
    public IEnumerable<string> SoundsToPrecache()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (SceneSound sound in _sounds)
        {
            if (!sound.IsStop && sound.Name is { Length: > 0 } && seen.Add(sound.Name))
            {
                yield return sound.Name;
            }
        }
    }

    /// <summary>Every recorded moment, in tick order.</summary>
    public IReadOnlyList<TimelineFrame> Frames => _frames;

    /// <summary>Every recorded change to the atmosphere, in tick order.</summary>
    /// <remarks>
    /// Exposed alongside <see cref="FogAt"/> because the two answer different questions and a test
    /// that can only ask the second cannot tell "no fog was recorded" from "fog was recorded after
    /// the tick I asked about". That distinction cost a diagnosis here.
    /// </remarks>
    public IReadOnlyList<(int Tick, SceneFog Fog)> FogSamples => _fog;

    /// <summary>Every soundscape change the recording carries, in tick order.</summary>
    /// <remarks>
    /// **Empty for a SourceTV recording, and that is the format rather than a gap.** `m_audio` is
    /// sent only to the player who owns the entity, so a SourceTV demo — which owns no player —
    /// carries nobody's. See <see cref="SceneSoundscape"/>.
    /// </remarks>
    public IReadOnlyList<(int Tick, SceneSoundscape Soundscape)> Soundscapes => _soundscapes;

    /// <summary>The soundscape in force at a tick, or <c>null</c> before the first sample.</summary>
    /// <param name="tick">The tick to ask about.</param>
    /// <returns>The soundscape, or <c>null</c>.</returns>
    /// <remarks>
    /// Walks forward keeping the last sample at or before the tick, the same way fog and viewmodels
    /// are read: a soundscape persists until the player enters another one, so the absence of a
    /// sample means "unchanged", never "none".
    /// </remarks>
    public SceneSoundscape? SoundscapeAt(int tick)
    {
        SceneSoundscape? found = null;

        foreach ((int at, SceneSoundscape soundscape) in _soundscapes)
        {
            if (at > tick)
            {
                break;
            }

            found = soundscape;
        }

        return found;
    }

    /// <summary>How many times a fog controller was seen in the entity table, across all packets.</summary>
    /// <remarks>
    /// **A diagnostic, and it exists because "no fog" has two causes that look identical.** Either
    /// the demo has no controller, or it has one and something between the entity table and
    /// <c>SceneFog</c> is dropping it. A count separates them in one number; without it the only
    /// way to tell was to decode the demo twice by different routes and compare.
    /// </remarks>
    public int FogControllersSeen { get; private init; }

    /// <summary>The most properties any fog controller carried, which B132 says is zero.</summary>
    public int FogControllerProperties { get; private init; }

    /// <summary>The atmosphere at a tick, or null when the demo records none.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The fog in force, or null.</returns>
    /// <remarks>
    /// **Walks forward keeping the last sample at or before the tick**, which is how every other
    /// lookup here works and is what a networked value means: it holds until the server changes it.
    ///
    /// Null before the first sample as well as when there is none at all, because fog that begins
    /// partway through a demo did not exist before then. Drawing the eventual value from tick zero
    /// would be inventing atmosphere the recording does not have.
    /// </remarks>
    public SceneFog? FogAt(int tick)
    {
        SceneFog? found = null;

        foreach ((int at, SceneFog fog) in _fog)
        {
            if (at > tick)
            {
                break;
            }

            found = fog;
        }

        return found;
    }

    /// <summary>Which entity the recording was made from, or <c>null</c> for SourceTV.</summary>
    /// <remarks>
    /// <c>svc_ServerInfo</c>'s player slot, plus one: entity indices are one-based and slot zero is
    /// the first player. Named by the demo rather than worked out — a first-person camera needs the
    /// recorder's class to know their eye height, and identifying them by "whichever player moves
    /// like the camera" would be an instrument that agrees with its own hypothesis.
    ///
    /// A SourceTV recording has no local player. Its <c>PlayerSlot</c> is not meaningful and
    /// <see cref="HasRecordedView"/> is false, so the viewer spectates a chosen player instead.
    /// </remarks>
    public int? RecorderEntityIndex { get; private init; }

    /// <summary>The weapon a player is holding, as they would see it.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="playerEntityIndex">The player whose view is being shown.</param>
    /// <returns>Their viewmodel, or <c>null</c> when the demo describes none for them.</returns>
    /// <remarks>
    /// **Two cases, both measured rather than assumed.** A point-of-view recording carries exactly
    /// one viewmodel and never names an owner — you only ever receive your own — so an unowned one
    /// belongs to whoever is being followed. A modern SourceTV recording carries one per player and
    /// names each, so it is matched by owner. Requiring an owner would find nothing on eight of the
    /// nine corpus demos, and the weapon would simply never appear.
    ///
    /// At or before the tick, like every other per-tick lookup here: the demo speaks at packet
    /// ticks and the viewer draws between them.
    ///
    /// **The main hand, because a player has two viewmodels and only one is the weapon.**
    /// <c>MAX_VIEWMODELS</c> is 2 and slot 1 is the off hand, which TF2 gives to the spy's watch
    /// and to grenades. Ignoring the slot answers with whichever entity was described last, and on
    /// the corpus's 2009 badlands recording that is the watch — so the weapon on screen stayed
    /// <c>v_watch_spy</c> across a change of class from soldier to scout.
    ///
    /// **The off hand is drawn as well as the main hand, not instead of it** — the owner, who has
    /// played the class: "main viewmodel doesnt get hidden when a spy goes invis, the watch just
    /// comes up and everything goes transparent". So this answers with one weapon short of what a
    /// spy actually sees, which is a smaller error than the wrong weapon and is its own piece of
    /// work. See <c>docs/findings/04-entities.md</c>.
    /// </remarks>
    public SceneViewmodel? ViewmodelAt(int tick, int playerEntityIndex) =>
        Viewmodel(tick, playerEntityIndex, mainHand: true);

    /// <summary>The model in a player's other hand, which for TF2 is the spy's watch.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="playerEntityIndex">The player whose view is being shown.</param>
    /// <returns>Their off-hand viewmodel, or <c>null</c> when they carry none.</returns>
    /// <remarks>
    /// **Drawn as well as the main hand, not instead of it.** The owner, who has played the class:
    /// "main viewmodel doesnt get hidden when a spy goes invis, the watch just comes up and
    /// everything goes transparent". A cloaking spy has both on screen, so a viewer answering only
    /// with <see cref="ViewmodelAt"/> is one model short of what that player saw.
    ///
    /// **The watch is the only thing that uses slot 1.** <c>tf_weaponbase_grenade.cpp:74</c> also
    /// calls <c>SetViewModelIndex( 1 )</c> and reads as a second case, but TF2's throwable grenades
    /// were cut before release: the class is still linked and no shipped item names it. So this
    /// answers null for every class but a spy, which is the ordinary case rather than a failure.
    /// </remarks>
    public SceneViewmodel? OffHandViewmodelAt(int tick, int playerEntityIndex) =>
        Viewmodel(tick, playerEntityIndex, mainHand: false);

    /// <summary>Whichever of a player's two viewmodels was asked for, at or before a tick.</summary>
    /// <remarks>
    /// One walk for both hands, because the rule about owners is the same for each and having it
    /// written twice is how the two would come to disagree.
    /// </remarks>
    private SceneViewmodel? Viewmodel(int tick, int playerEntityIndex, bool mainHand)
    {
        SceneViewmodel? found = null;

        foreach ((int at, SceneViewmodel weapon) in _viewmodels)
        {
            if (at > tick)
            {
                break;
            }

            // **An unowned viewmodel is the follower's only when the demo names no owners at all.**
            // That is the point-of-view shape: one viewmodel, no owner, because a client receives
            // only its own. A SourceTV recording carries one per player and names them — and when
            // one of thirty-seven fails to resolve an owner, treating "unowned" as "anybody's" hands
            // that one to every player who has none. Measured on z1800: following a sniper drew a
            // demoman's arms.
            if (weapon.IsMainHand == mainHand &&
                (weapon.OwnerEntityIndex == playerEntityIndex ||
                 (weapon.OwnerEntityIndex is null && !_viewmodelsNameOwners)))
            {
                found = weapon;
            }
        }

        // **Filtered at the END, on the latest state, never while walking.** Rejecting hidden
        // samples inside the loop would leave `found` holding an older visible one, which is the
        // stale-sample bug this whole flag exists to avoid: a spy who puts the watch away would
        // keep it in frame for the rest of the demo.
        return found is { IsOnScreen: true } ? found : null;
    }

    /// <summary>Every distinct viewmodel model the demo ever describes.</summary>
    /// <remarks>
    /// **A viewmodel is not a prop, and that is precisely why this is needed.** It carries no
    /// origin, so it is deliberately absent from <see cref="Props"/> — and a viewer that builds its
    /// load set by walking the prop tracks therefore never loads the arms, never packs them, and
    /// draws nothing while reporting that it resolved a model. That is what happened: the log said
    /// <c>viewmodel c_demo_arms.mdl ... 0 instances</c> for every frame, with the file sitting in
    /// the archive the whole time.
    ///
    /// Distinct because a match changes weapons constantly and the same few arms recur; the set is
    /// one entry per class in practice.
    /// </remarks>
    public IEnumerable<string> ViewmodelModels =>
        _viewmodels
            .Select(recorded => recorded.Weapon.ModelPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this demo carries a recorded camera at all.</summary>
    /// <remarks>
    /// **A SourceTV recording has no local player and leaves <c>democmdinfo_t</c> zeroed**, so the
    /// point-of-view camera has nothing to follow and the viewer has to offer something else —
    /// spectating a chosen player, as the engine does. Asked once, because a per-frame null check
    /// cannot tell "not yet" from "never".
    /// </remarks>
    public bool HasRecordedView => _recordedViews.Count > 0;

    /// <summary>The camera the recording was made through at a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The most recently stated view, or <c>null</c> before the first one.</returns>
    /// <remarks>
    /// **At or before, not exactly at.** The demo speaks at packet ticks and the viewer draws
    /// between them, so an exact-match lookup answers nothing on most frames and the view would
    /// flicker back to another camera. Before the first packet the answer really is nothing —
    /// inventing the first view would place the camera somewhere the recording never was.
    /// </remarks>
    public RecordedView? RecordedViewAt(int tick)
    {
        if (_recordedViews.Count == 0 || tick < _recordedViews[0].Tick)
        {
            return null;
        }

        int low = 0;
        int high = _recordedViews.Count - 1;

        while (low < high)
        {
            // Rounded up, so the search moves towards the later entry and cannot stall on low.
            int middle = low + ((high - low + 1) / 2);

            if (_recordedViews[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _recordedViews[low].View;
    }

    /// <summary>The first tick with positions, or zero when the demo has none.</summary>
    public int FirstTick => _frames.Count > 0 ? _frames[0].Tick : 0;

    /// <summary>The last tick with positions, or zero.</summary>
    public int LastTick => _frames.Count > 0 ? _frames[^1].Tick : 0;

    /// <summary>Seconds per tick, as the server that recorded this demo ran.</summary>
    /// <remarks>
    /// **Read from the demo, never assumed.** <c>svc_ServerInfo</c> states it, and it is not always
    /// TF2's usual 0.015 — the rate is a server setting, so it varies with how a given box was set
    /// up rather than with when the demo was recorded. A demo played back at the wrong rate looks
    /// like a slow or fast server rather than like a defect.
    ///
    /// Zero when no <c>svc_ServerInfo</c> arrived, which leaves the choice of a default to the
    /// caller rather than burying one here.
    /// </remarks>
    public float IntervalPerTick { get; private init; }

    /// <summary>What the recording server had its replicated ConVars set to.</summary>
    /// <remarks>
    /// **Never null, and a fresh one answers with Valve's declared defaults** — which is exactly
    /// what the engine does for a server that changed nothing, so a timeline built from tracks for
    /// a test is not a special case needing a guard at every reader.
    ///
    /// This is where a mod's altered movement arrives. A vanilla competitive server sends forty
    /// values without touching movement and every one of these stays at its default;
    /// <c>ServerConVars.Changed</c> is what distinguishes that from a jump or surf server.
    /// </remarks>
    public ServerConVars ServerConVars { get; private init; } = new();

    /// <summary>The checksum of the map this was recorded on, when the demo said.</summary>
    /// <remarks>
    /// **`svc_ServerInfo`'s `mapCRC`, which identifies the map's VERSION where its name does not.**
    /// `cp_badlands` in 2017 is not `cp_badlands` in 2026, and the viewer loads by name out of
    /// whatever TF2 install is present — so an old demo is drawn against geometry it was never
    /// recorded on, and every consequence looks like a rendering defect. Three were investigated as
    /// such before the owner identified the real cause (D113, finding 41).
    ///
    /// **Null when the demo carried no `svc_ServerInfo`**, which is a real case for a truncated or
    /// hand-authored file — and distinguishable from "the CRCs differ", which is the point of it
    /// being nullable rather than zero.
    /// </remarks>
    public uint? MapCrc { get; private init; }

    /// <summary>The map hash <c>svc_ServerInfo</c> carries beside the checksum, when it has one.</summary>
    /// <remarks>
    /// **The instrument for modern demos, because the CRC is dead in them.** Measured across gcor:
    /// 2007 through 2011 carry a real `mapCRC` — and the 2008 and 2011 POV/STV pairs each agree with
    /// themselves, so the field is genuine — while 2013 onward and `z1800` all carry `0xFFFFFFFF`,
    /// the CRC32 init value. Valve stopped computing it somewhere between 2011 and 2013.
    ///
    /// So a version check needs both: the CRC for the old era, this for the new one.
    /// </remarks>
    public IReadOnlyList<byte>? MapHash { get; private init; }

    /// <summary>Builds a timeline directly from tracks, for tests that need an exact motion.</summary>
    /// <param name="tracks">The entity tracks the timeline should answer from.</param>
    /// <returns>A timeline with no frames and these tracks.</returns>
    /// <remarks>
    /// **A seam, and a narrow one on purpose.** Some behaviour here is a function of an entity's
    /// motion over time — speed, and the movement pose parameters derived from it — and asserting
    /// on it from a real demo means hunting for a tick where a player happens to be running, which
    /// measures the corpus rather than the code. Two keyframes 200 units apart state the condition
    /// exactly.
    ///
    /// Internal rather than public: this is not a way to build a timeline, it is a way to ask one a
    /// question. Nothing outside the tests should be assembling tracks by hand.
    /// </remarks>
    internal static DemoTimeline ForTracks(List<ScenePropTrack> tracks) => new([], tracks);

    /// <summary>A timeline carrying nothing but these sounds, for testing the precache list.</summary>
    internal static DemoTimeline ForSounds(List<SceneSound> sounds) =>
        new([], props: null, playerTracks: null, recordedViews: null, viewmodels: null,
            fog: null, sounds: sounds);

    /// <summary>Walks a demo and records where everyone was.</summary>
    /// <param name="file">The whole demo file, header included.</param>
    /// <returns>The timeline, empty when the demo carries no schema or no entities.</returns>
    /// <exception cref="ArgumentException">The file is too short to hold a header.</exception>
    /// <remarks>
    /// **A demo with no <c>dem_datatables</c> yields an empty timeline rather than an error.** Some
    /// files genuinely have none, and a viewer that refused to open them would be refusing exactly
    /// the salvage cases this project exists for.
    /// </remarks>
    public static DemoTimeline Build(ReadOnlyMemory<byte> file)
    {
        DemoHeader header = DemoHeader.Parse(file.Span);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file[DemoHeader.SizeBytes..])];

        // **`DemoCommand` is a readonly record STRUCT, so FirstOrDefault yields default(T) rather
        // than null and a `is not { }` guard on it can never fire.** This block used to read
        // `DemoCommand? tables = commands.FirstOrDefault(...)`, which compiles, looks like the
        // familiar reference-type idiom and is dead: the implicit conversion wraps the default
        // struct in a non-null nullable, so a demo carrying no dem_datatables fell through to
        // SendTableParser with an empty payload and threw "the payload ends mid-table after 0
        // bytes" instead of returning an empty timeline.
        //
        // Found by a synthetic demo built without the command — every real demo has one, so the
        // corpus could not reach this path. The type check below is what the nullable pattern was
        // meant to express: DemoCommandType has no zero member on purpose, so a defaulted struct
        // cannot collide with a genuine command.
        DemoCommand dataTables = commands.FirstOrDefault(
            command => command.Type == DemoCommandType.DataTables);

        if (dataTables.Type != DemoCommandType.DataTables)
        {
            return new DemoTimeline([]);
        }

        DemoSchema schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Given the decoder, because an entering entity is a delta against its class baseline.**
        // Without it every entity whose whole state equals its baseline accumulates as an empty
        // one - see IEntityBaselines, and B132, which is what that cost.
        EntityStateTable entities = new(decoder);

        // **Class names come from dem_datatables, not from svc_ClassInfo.** TF2 sets the
        // "create on client" flag and sends no names, so a reader waiting for that message names
        // nothing and finds no players while decoding every entity correctly.
        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        List<TimelineFrame> frames = [];

        float interval = 0f;

        // **When each player last left the ground**, so a jump can be split into its push-off and
        // its float. The engine reads m_flJumpStartTime, set when the jump event arrives; a demo
        // carries no such event, so this watches FL_ONGROUND clear instead.
        Dictionary<int, int> leftGroundAt = [];

        // Where each player was last tick, so a vertical speed can be differenced. The client does
        // the same: GetOuterAbsVelocity calls EstimateAbsVelocity on the client, which estimates
        // from position history rather than reading a networked velocity.
        Dictionary<int, (int Tick, float X, float Y, float Z)> lastHeight = [];

        // **Where each player's feet point**, which is where their body is drawn. The engine runs
        // this per client frame; a demo gives ticks, so it runs per tick. Stateful by nature — the
        // feet lag the eyes and catch up over several of them.
        Dictionary<int, FeetYaw> feet = [];

        // **Sticky, because the engine's condition is `vz > 300 || m_bInAirWalk`.** Once an
        // air-walk starts it continues until the player lands, so a rocket jump does not flicker
        // back to the jump animation as the rise slows.
        HashSet<int> airwalkingSince = [];

        ModelPrecache precache = new();
        int protocol = header.NetworkProtocol;

        // **The sounds the recording plays, and the table that names them.** Both are needed
        // together: svc_Sounds carries a NUMBER, and the number is an index into this demo's own
        // soundprecache, so a decoder without the table produces sounds nobody can open (B168).
        SoundNames soundNames = new();
        List<SceneSound> sounds = [];

        // Sampled on change like fog, and present only in a point-of-view recording — see
        // SceneSoundscape for why a SourceTV demo carries nobody's (B173).
        List<(int Tick, SceneSoundscape Soundscape)> soundscapes = [];

        // Every shot the director called, so the chase camera can be framed as the recording asks
        // rather than always from C_HLTVCamera::Reset's defaults.
        List<(int Tick, DirectorShot Shot)> director = [];

        // Live tracks by slot, plus every track ever started. A slot is reused when its occupant
        // is destroyed, so the two are not the same list - keeping only the live ones would lose
        // every rocket the moment the next one took its index.
        Dictionary<int, ScenePropTrack> tracks = [];
        List<ScenePropTrack> props = [];
        List<ScenePropTrack> playerTracks = [];
        List<(int Tick, RecordedView View)> recordedViews = [];
        int? recorderSlot = null;
        uint? mapCrc = null;
        IReadOnlyList<byte>? mapHash = null;

        // **What the server had its replicated ConVars set to** (D106). Built here rather than by
        // a caller because the values arrive as messages in this stream and nowhere else, and the
        // engine applies them the same way: at signon, and again whenever one changes mid-match.
        ServerConVars serverConVars = new();
        List<(int Tick, SceneViewmodel Weapon)> viewmodels = [];
        List<(int Tick, SceneFog Fog)> fogSamples = [];
        int fogControllersSeen = 0;
        int fogProperties = 0;

        // What each viewmodel entity last said, so an unchanged one is not recorded again. Keyed
        // by entity because a player carries two and they interleave.
        Dictionary<int, SceneViewmodel> lastViewmodel = [];

        foreach (DemoCommand command in commands)
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            // **The recorder's own camera, kept while the file is already open.** A viewer asking
            // for it per frame cannot re-walk a 39 MB demo, and this loop is the only pass over
            // the commands there is.
            //
            // A zeroed structure is not a camera at the world origin, it is the absence of one:
            // SourceTV recordings have no local player and leave every one of these blank, so they
            // are skipped rather than recorded as a view at (0, 0, 0) that would put the camera in
            // the middle of the map.
            if (command.Prologue.Length >= RecordedView.SizeBytes)
            {
                RecordedView view = RecordedView.Parse(command.Prologue.Span);

                if (view.Origin != (0f, 0f, 0f))
                {
                    recordedViews.Add((command.Tick, view));
                }
            }

            bool moved = false;

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                // **Instance baselines, because an entering entity is a delta against its class.**
                // Applying them changed no count on any file in the corpus, era or modern - but
                // "it changed nothing measurable here" is not evidence that it never will, and the
                // format says an entity entering the visible set is sent against this rather than
                // in full.
                switch (message)
                {
                    // **The server's own tick rate, not a constant.** Early servers ran 33 tick,
                    // and a demo replayed at the wrong rate looks like a slow or fast server
                    // rather than like a defect.
                    case ServerInfoMessage server when server.IntervalPerTick > 0f:
                        interval = server.IntervalPerTick;

                        // **Which map, and WHICH VERSION of it.** The name alone does not identify
                        // a map — `cp_badlands` in 2017 is not `cp_badlands` in 2026 — and the CRC
                        // is what the engine uses to tell a client its `.bsp` is the wrong one.
                        // Decoded since the container work and compared against nothing until
                        // D113, which cost an evening of chasing rendering defects that were a
                        // mismatched map (finding 41).
                        mapCrc = server.MapCrc;
                        mapHash = server.MapHash;

                        // **Which entity the recording was made from**, named by the demo rather
                        // than inferred. A first-person camera needs the recorder's class for the
                        // eye height, and picking whichever player moves like the camera would be
                        // an instrument that agrees with its own hypothesis.
                        recorderSlot = server.PlayerSlot;
                        continue;

                    // **What the server changed, which for a mod is the whole of how it plays.**
                    // Decoded and round-tripped since the container work and consumed by nothing
                    // until D106 — so a server that raised `sv_maxspeed` sent the value, this
                    // project read it correctly, and every reader used a baked constant instead.
                    case SetConVarMessage convars:
                        serverConVars.Apply(convars);
                        continue;

                    // **The director telling the camera how to frame this shot** — `hltv_chase`,
                    // which `C_HLTVCamera::FireGameEvent` (`hltvcamera.cpp:776`) reads to set the
                    // mode, both targets, and every chase parameter. Without it the chase camera
                    // runs on `Reset`'s defaults for ever, which is what it did until now: a demo
                    // could ask for a wider shot or a different angle and be ignored.
                    case GameEventMessage { Name: DirectorShot.ChaseEvent } chase:
                        director.Add((command.Tick, DirectorShot.From(chase.Values, director.Count > 0 ? director[^1].Shot : null)));
                        continue;

                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        BaselineBuilder.Apply(create.Entries, decoder);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        BaselineBuilder.Apply(update.Entries, decoder);
                        continue;

                    // Which model each m_nModelIndex names. Without this every entity carrying a
                    // model resolves to nothing and the scene is players on an empty map.
                    case CreateStringTableMessage { Name: ModelPrecache.TableName } models:
                        precache.Apply(models.Entries);
                        continue;

                    // **The same arrangement for sounds, and it needs both messages.** A table is
                    // created once and then updated as the round goes on — a sound first played
                    // mid-match is added by an update, so handling only the create resolves the
                    // opening minute and nothing after it.
                    case CreateStringTableMessage { Name: SoundNames.TableName } soundTable:
                        soundNames.Add(soundTable);
                        continue;

                    case UpdateStringTableMessage soundUpdate
                        when state.StringTableName(soundUpdate.TableId) == SoundNames.TableName:
                        soundNames.Add(soundUpdate, SoundNames.TableName);
                        continue;

                    // **Every sound the server plays, placed at the tick it was sent on.** Decoded
                    // here rather than at playback because the body is a delta-compressed bit
                    // stream: each sound is written against the one before it, so it can only be
                    // read in order and only once.
                    case SoundsMessage { Count: > 0 } played:
                        foreach (DecodedSound sound in SoundDecoder.Decode(
                            played.Body.Span, played.Count, played.BodyBits, (ushort)protocol))
                        {
                            sounds.Add(new SceneSound(
                                command.Tick,

                                // Empty rather than dropped when the number names nothing: an
                                // unresolvable sound still happened, and discarding it would make a
                                // gap in the table look like silence in the recording.
                                soundNames.Resolve(sound.SoundNumber) ?? string.Empty,
                                sound.SoundNumber,
                                sound.EntityIndex,
                                sound.Channel,
                                sound.Volume,
                                sound.SoundLevel,
                                sound.Pitch,
                                sound.DelaySeconds,
                                sound.OriginX,
                                sound.OriginY,
                                sound.OriginZ,
                                sound.IsAmbient,
                                (sound.Flags & SoundDecoder.StopFlag) != 0,
                                command.Type == DemoCommandType.Signon));
                        }

                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.TableName:
                        precache.Apply(update.Entries);
                        continue;

                    // The second model table, which is where every cosmetic lives. A negative
                    // m_nModelIndex is a dynamic model, and the even ones are networked through
                    // here - see ModelPrecache.Path.
                    case CreateStringTableMessage { Name: ModelPrecache.DynamicTableName } dynamic:
                        precache.ApplyDynamic(dynamic.Entries);
                        continue;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == ModelPrecache.DynamicTableName:
                        precache.ApplyDynamic(update.Entries);
                        continue;

                    default:
                        break;
                }

                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    entities.Apply(entity);
                    RecordProp(
                        entity, entities, precache, tracks, props, playerTracks,
                        protocol, command.Tick);
                }

                moved = true;
            }

            // **After the packet's messages, not before.** Sampling first reads the table as it
            // stood at the PREVIOUS tick, so an entity that enters on this packet is missed
            // entirely — and on a demo whose viewmodel enters once and never changes, that means
            // it is never recorded at all.
            RecordViewmodels(
                entities, precache, protocol, command.Tick, lastViewmodel, viewmodels);

            // **Sampled here for the same reason and recorded only on CHANGE.** A fog controller
            // sends its whole state on entry and then rarely again, so a keyframe per tick would be
            // tens of thousands of identical entries; a keyframe per change is a handful, and the
            // lookup walks forward keeping the last one exactly as the viewmodels do.
            foreach (EntityState entity in entities.All)
            {
                if (entity.ClassName == FogControllerClass)
                {
                    fogControllersSeen++;

                    // **The controller's property count, which is the number that diagnosed B132.**
                    // Zero here while a trace of the same demo shows the entity entering with
                    // fifteen properties is the whole finding, and it is cheap enough to keep.
                    fogProperties = Math.Max(fogProperties, entity.Properties.Count);
                }

                // **The soundscape a player is standing in, sampled the same way and for the same
                // reason.** It is per-player private data — `m_audio` lives in `DT_Local`, sent
                // through `SendProxy_SendLocalDataTable`'s `SetOnly( objectID - 1 )` — so at most
                // one entity in a recording carries it, and only a point-of-view recording has one
                // at all. Whichever entity has it is the one whose ears the demo was recorded from.
                if (entity.SoundscapeIndex() is { } soundscape)
                {
                    SceneSoundscape sampled = new(
                        soundscape,
                        entity.SoundscapePositionBits() ?? 0,
                        Positions(entity),
                        entity.SoundscapeEntity() ?? -1);

                    if (soundscapes.Count == 0 || soundscapes[^1].Soundscape != sampled)
                    {
                        soundscapes.Add((command.Tick, sampled));
                    }
                }

                if (entity.Fog() is not { } fog)
                {
                    continue;
                }

                if (fogSamples.Count == 0 || fogSamples[^1].Fog != fog)
                {
                    fogSamples.Add((command.Tick, fog));
                }

                break;
            }

            if (!moved)
            {
                continue;
            }

            List<ScenePlayer> players = [];
            EntityState? resource = entities.OfClass(ResourceClass).FirstOrDefault();

            foreach (EntityState player in entities.OfClass(PlayerClass))
            {
                if (!player.IsVisible || player.Origin() is not { } origin)
                {
                    continue;
                }

                // The resource's arrays are keyed by entity index, zero padded to three digits.
                string slot = player.EntityIndex.ToString("D3", CultureInfo.InvariantCulture);

                // **The jump clock, kept here because only this loop sees the ticks in order.**
                // Recorded on the transition rather than every airborne tick, so the elapsed time
                // is measured from the moment the flag cleared.
                float? airborne = null;

                // **The rise, differenced from the last height this player was seen at.** Only the
                // upward component matters: the air-walk test is on velocity.z alone.
                float? rising = null;

                // How fast in all three dimensions, which is what the feet-yaw test uses:
                // `vecVelocity.Length() > 1.0f` rather than the horizontal speed the activity
                // choice uses.
                float travelling = 0f;

                if (lastHeight.TryGetValue(
                        player.EntityIndex, out (int Tick, float X, float Y, float Z) before) &&
                    command.Tick > before.Tick &&
                    interval > 0f)
                {
                    float elapsed = (command.Tick - before.Tick) * interval;

                    rising = (origin.Z - before.Z) / elapsed;

                    float acrossX = origin.X - before.X;
                    float acrossY = origin.Y - before.Y;
                    float upward = origin.Z - before.Z;

                    travelling = MathF.Sqrt(
                        (acrossX * acrossX) + (acrossY * acrossY) + (upward * upward)) / elapsed;
                }

                lastHeight[player.EntityIndex] = (command.Tick, origin.X, origin.Y, origin.Z);

                if (player.Flags() is { } stateFlags)
                {
                    if ((stateFlags & PlayerActivityState.OnGround) != 0)
                    {
                        leftGroundAt.Remove(player.EntityIndex);
                        airwalkingSince.Remove(player.EntityIndex);
                    }
                    else
                    {
                        if (!leftGroundAt.TryGetValue(player.EntityIndex, out int since))
                        {
                            since = command.Tick;
                            leftGroundAt[player.EntityIndex] = since;
                        }

                        // Null while the interval is unknown — the first frames arrive before
                        // net_tick states one, and a zero interval would make every jump read as
                        // its own first instant for ever.
                        airborne = interval > 0f ? (command.Tick - since) * interval : null;

                        // The engine's threshold, and it latches: once rising this fast the
                        // air-walk holds until the ground flag returns.
                        if (rising is { } climb && climb > PlayerActivityState.AirwalkRiseSpeed)
                        {
                            airwalkingSince.Add(player.EntityIndex);
                        }
                    }
                }

                // **A dead player's origin is not where they died — it is where they are
                // WATCHING.** The entity follows whoever they spectate, so drawing a corpse at its
                // current origin puts it standing inside a living player, and several of them
                // stack into one heap.
                //
                // So the last position held while alive is kept and used until they respawn, which
                // leaves a body roughly where it fell. TF2 leaves a ragdoll there; this is a
                // standing stand-in for one until ragdolls are simulated (B58).
                int? life = player.LifeState();
                bool alive = life is null or 0;

                // **The yaw has to be carried here too, and was not.** Every argument below is
                // positional and the list stopped at LifeState, so Yaw took the record's default of
                // zero — every player in a frame faced due east regardless of where they were
                // looking. The track path gained eye angles and this one did not, which is the
                // shape a default has whenever it is also a legitimate value: nothing reports a
                // missing yaw, because zero IS a yaw.
                //
                // Read from the same place the track reads it, so the two cannot disagree: the eye
                // angles when the demo sends them, and m_angRotation when it does not. Normalised
                // to (−180, 180] like every other angle here, because the wire carries this one as
                // 0..360 — without it the same direction is held as two numbers a full turn apart,
                // measured as 220.997 against −139.003, and anything comparing or interpolating
                // them is wrong by 360 at the wrap.
                float facing = Normalize(
                    player.EyeAngles() is { } eyes ? eyes.Yaw : player.Angles()?.Yaw ?? 0f);

                // **How far up or down they are looking, which aims the torso.**
                // ComputePoseParam_AimPitch is one line — `SetPoseParameter( m_iAimPitch,
                // -flAimPitch )` where flAimPitch is m_flEyePitch — and body_pitch is what it sets.
                // Kept separate from the pose's own Pitch, which stays zero: a player model stands
                // upright however far the eyes are pitched, and rolling the whole body by the view
                // would lay them on their side every time they looked up.
                float? lookingAt = player.EyeAngles() is { } view ? Normalize(view.Pitch) : null;

                // **The feet, advanced once per tick, and only once the eye yaw is known.** They
                // follow the eyes while moving and stay planted while the player turns on the spot,
                // which is the whole difference between this and using the eye yaw as the body yaw.
                FeetYaw standing = feet.TryGetValue(player.EntityIndex, out FeetYaw known)
                    ? known
                    : default;

                standing.Advance(facing, travelling, interval > 0f ? interval : 0f);
                feet[player.EntityIndex] = standing;

                // **The dead are reported where the entity actually is, which is wherever they are
                // spectating from.** This used to hold the last living position and yaw so a body
                // stayed roughly where it fell, standing in for a ragdoll nobody had built yet.
                // That stand-in is gone: the engine does not draw a dead player at all, so there
                // was never a corpse for it to approximate, and holding the position meant the
                // timeline reported a coordinate the demo does not contain. A viewer that wants a
                // body waits for B58 and draws the CTFRagdoll entity, which is where the engine
                // keeps one.
                players.Add(new ScenePlayer(
                    player.EntityIndex,
                    origin.X,
                    origin.Y,
                    origin.Z,
                    resource?.Integer($"m_iTeam.{slot}") ?? First(player, TeamProperties),
                    resource?.Integer($"m_iHealth.{slot}") ?? First(player, HealthProperties),
                    resource?.Integer($"m_iPlayerClass.{slot}"),
                    LifeState: life,
                    // **The FEET, which is what the engine renders the body at** —
                    // `m_angRender[YAW] = m_flCurrentFeetYaw`. Equal to the eye yaw whenever the
                    // player is moving, so this changes nothing except while turning on the spot,
                    // where the feet should stay planted and the torso twist.
                    Yaw: standing.Current,
                    EyeYaw: facing,
                    AimYaw: standing.AimYaw(facing),

                    // Null on a POV demo for everyone but the recorder, because the send prop is in
                    // DT_LocalPlayerExclusive; a SourceTV recording carries it for every player.
                    Flags: player.Flags(),

                    // **What the engine itself uses to decide whether a view is first person**
                    // (B225). Unlike `Flags` above, this one is in `DT_BasePlayer` proper, so it
                    // arrives for everybody in either kind of recording. Without it a player who
                    // goes to spectator is indistinguishable from one still playing — spectating is
                    // not dying, so `LifeState` says nothing about it — and the viewer drew their
                    // last weapon over a free-roaming camera.
                    ObserverMode: player.ObserverMode(),

                    // **EF_NODRAW, which is how the engine hides a corpse.** On death the server
                    // spawns a CTFRagdoll and then turns the player off with
                    // `AddEffects( EF_NODRAW | EF_NOSHADOW )` (tf_player.cpp:15637), so the body on
                    // screen is a different entity and the player itself is simply not drawn. TF2
                    // has no death animation to play instead: HandleDying is unreachable, because
                    // PLAYERANIMEVENT_DIE is raised nowhere in the game tree and its handler is an
                    // Assert(0).
                    //
                    // Read from the effects field rather than only from the life state, because
                    // that is what the engine tests. EF_NODRAW is also how a player is hidden while
                    // taunting into a cutscene or riding a teleporter, and life state cannot answer
                    // those.
                    //
                    // **The life state is ANDed in as well, and the reason is a gate in the
                    // engine rather than a shortcut here.** A dead player does not keep EF_NODRAW
                    // for the whole of their death: StateThinkDYING removes it again for the
                    // deathcam, commented `// still draw player body` (tf_player.cpp:13934). That
                    // whole branch is conditional on `m_hRagdoll` being non-null — the body is
                    // re-shown only once a corpse exists to justify it. We do not build ragdolls
                    // yet (B58), so that condition is false for every death we could render, and
                    // the engine's own answer for our situation is that the effect stays on.
                    //
                    // Measured on movement-test-stv-cp_process: 322 of 535 dead player-ticks
                    // carried EF_NODRAW on the wire and 213 did not, and the 213 are exactly the
                    // deathcam window above.
                    //
                    // When ragdolls land this becomes wrong and should follow EF_NODRAW alone.
                    Drawn: player.IsDrawn && alive,

                    // **The weapon, and its class resolved here while the table is in hand.** A
                    // handle is only an entity slot; what the animation needs is which weapon it
                    // is, and only this loop can see both. Resolved rather than carried as a bare
                    // index so no consumer has to keep the entity table alive to make sense of it.
                    AirborneSeconds: airborne,
                    Airwalking: airwalkingSince.Contains(player.EntityIndex),
                    EyePitch: lookingAt,
                    WaterLevel: player.WaterLevel(),
                    ActiveWeapon: player.ActiveWeapon(),
                    WeaponClass: player.ActiveWeapon() is { } held &&
                        entities.TryGet(held, out EntityState? weapon)
                            ? weapon.ClassName
                            : null,

                    // Read here rather than left to the caller, because this is the only pass over
                    // the entity table there is — a viewer asking later would have to re-walk the
                    // demo to find out which item the weapon was.
                    WeaponItem: player.ActiveWeapon() is { } carried &&
                        entities.TryGet(carried, out EntityState? item)
                            ? item.ItemDefinitionIndex()
                            : null));
            }

            // **Only when the tick advanced.** Several commands can share a tick, and recording a
            // frame for each would make the timeline's own ordering ambiguous — PlayersAt would
            // then depend on which of them it happened to find first.
            if (frames.Count > 0 && frames[^1].Tick >= command.Tick)
            {
                frames[^1] = new TimelineFrame(frames[^1].Tick, players);
                continue;
            }

            frames.Add(new TimelineFrame(command.Tick, players));
        }

        Backfill(frames);

        // **The signon's sounds are the ambience already playing, and its clock is not the
        // recording's.** Measured on movement-test-pov-cp_process: six )ambient/machine_hum.wav
        // arrive from the signon stamped tick 4654 while every packet sound runs from 30 upward, so
        // the signon carries the server's tick at the moment recording began. Left alone, the map's
        // hum starts seventy seconds in and sorting by the stated tick buries the opening minute
        // behind it.
        //
        // Moved to the first tick anything else happens on, because that is when a viewer starts
        // hearing the map — not to zero, since a demo's ticks do not start at zero
        // (docs/memory/demo-ticks-do-not-start-at-zero.md) and zero would sort before a recording
        // that opens at 30 and leave a gap nothing fills.
        if (sounds.Count > 0)
        {
            int firstPacketTick = int.MaxValue;

            foreach (SceneSound sound in sounds)
            {
                if (!sound.FromSignon && sound.Tick < firstPacketTick)
                {
                    firstPacketTick = sound.Tick;
                }
            }

            if (firstPacketTick < int.MaxValue)
            {
                for (int index = 0; index < sounds.Count; index++)
                {
                    if (sounds[index].FromSignon)
                    {
                        sounds[index] = sounds[index] with { Tick = firstPacketTick };
                    }
                }
            }

            // **A stable sort, so sounds sent in one message keep the order the server wrote them
            // in.** Within a tick that order is the engine's own, and several sounds on one channel
            // replace each other — reordering them would change which one survives.
            List<SceneSound> ordered = [.. sounds.OrderBy(sound => sound.Tick)];

            sounds.Clear();
            sounds.AddRange(ordered);
        }

        return new DemoTimeline(
            frames, props, playerTracks, recordedViews, viewmodels, fogSamples, sounds, soundscapes,
            director)
        {
            FogControllersSeen = fogControllersSeen,
            FogControllerProperties = fogProperties,
            IntervalPerTick = interval,
            RecorderEntityIndex = recorderSlot is { } recorded ? recorded + 1 : null,
            ServerConVars = serverConVars,
            MapCrc = mapCrc,
            MapHash = mapHash,
        };
    }

    /// <summary>Records where a model-bearing entity was, if this update said anything about it.</summary>
    /// <remarks>
    /// **Only entities the snapshot actually mentioned.** Walking the whole entity table every
    /// frame would ask several hundred entities to repeat themselves across a hundred thousand
    /// frames, and produce identical keyframes that the track then discards — the same answer for
    /// tens of millions of times the work. A demo states what changed; this records exactly that.
    /// </remarks>
    /// <summary>What an entity draws as, or <c>null</c> when nothing can say.</summary>
    /// <returns>
    /// A model path; the empty string for a player, whose model is not in the demo at all.
    /// </returns>
    /// <remarks>
    /// **A player's model is never sent.** <c>CTFPlayerClassShared::GetModelName</c> looks it up
    /// locally from <c>m_iClass</c> through the class data table, and only
    /// <c>m_iszCustomModel</c> travels. So a player is recognised by class and given a track with
    /// no model: the poses are what the interpolator needs, and the model is resolved from the
    /// installed game by whoever draws it.
    /// </remarks>
    /// <summary>Samples any viewmodel the entity table currently describes.</summary>
    /// <remarks>
    /// **Read from the entity table rather than from the snapshot's changed properties**, because
    /// a viewmodel is mostly silent: its model index arrives once and then only the sequence
    /// changes, so a reader looking at what a packet CHANGED would see a weapon with no model for
    /// almost every tick of the demo.
    ///
    /// Only recorded when something differs from the last sample. A viewmodel that has not changed
    /// costs nothing, which matters because z1800 carries 37 of them across 95,480 updates.
    /// </remarks>
    /// <summary>Records every viewmodel that changed on this tick.</summary>
    /// <remarks>
    /// **Deduplicated per entity, not against the tail of the list.** A player has two viewmodels
    /// and a demo describing both writes them alternately, so a check against the previous entry
    /// never matches and every tick records both — which is how the wrong one came to win.
    /// </remarks>
    private static void RecordViewmodels(
        EntityStateTable entities,
        ModelPrecache precache,
        int protocol,
        int tick,
        Dictionary<int, SceneViewmodel> last,
        List<(int Tick, SceneViewmodel Weapon)> into)
    {
        foreach (EntityState entity in entities.All)
        {
            if (entity.ViewmodelModelIndex() is not { } rawIndex)
            {
                continue;
            }

            // **Recorded even when it resolves to nothing, for the same reason a hidden one is.**
            // Model index 0 means "no model", and an unused off hand sends exactly that — all 22 of
            // z1800's do. Skipping those here would be right for a viewmodel that is always empty
            // and wrong for one that is emptied: the last sample would keep saying "watch", and the
            // lookup would answer with it for the rest of the demo. `IsOnScreen` decides instead,
            // in one place, on the latest state.
            string path = precache.Path(ModelPrecache.Unpack(rawIndex, protocol)) ?? string.Empty;

            // **Recorded whether or not it is drawn, and this is deliberate.** Skipping a hidden
            // viewmodel here would leave the last recorded sample for that entity saying "visible",
            // and the lookup walks forward keeping the last match — so a watch put away would carry
            // on being answered for the rest of the demo. The flag has to travel with the sample so
            // that the latest state is the one that wins.
            // **Which weapon the VIEWMODEL says it is holding** (B222). `DT_BaseViewModel` networks
            // `m_hWeapon` (`baseviewmodel_shared.cpp:567`), and that entity's own `m_nModelIndex` is
            // its VIEW model — the `c_` model — as this decoder already records elsewhere. So the
            // pair resolves the weapon the engine's own way, in one hop, from data the demo states
            // outright.
            //
            // What this replaces reconstructed it from the PLAYER instead: the player's
            // `m_hActiveWeapon`, that entity's item definition index, then a lookup in
            // `items_game.txt`. Three hops and a schema to reach something already on the wire, and
            // every hop able to fail on its own. Valve never asks the player; the viewmodel knows.
            //
            // Empty when the viewmodel names no weapon, which is an ordinary state — an off hand
            // holding nothing, or a viewmodel between weapons.
            // **The ITEM, not that entity's model index** — Valve builds the attachment as
            // `pItem->GetPlayerDisplayModel( iClass, team )` (`econ_entity.cpp:1167`), which is
            // `model_player` from `items_game.txt`. Taking the weapon entity's own `m_nModelIndex`
            // was tried on 2026-08-28 and drew no weapon at all: `m_hWeapon` says WHICH weapon and
            // the schema says what it looks like. Both hops are needed and they are different
            // questions.
            int? weaponItem = null;
            string? weaponClass = null;

            if (entity.ViewmodelWeapon() is { } weaponEntity &&
                entities.TryGet(weaponEntity, out EntityState? carried))
            {
                weaponItem = carried.ItemDefinitionIndex();
                weaponClass = carried.ClassName;
            }

            bool seen = last.TryGetValue(entity.EntityIndex, out SceneViewmodel before);

            // **The parity counter is how the engine says "play that again"** — see
            // `ViewmodelAnimation.RestartAt` for the citation. `m_nSequence` cannot express it,
            // because firing the same weapon twice sets the same number twice, and the record
            // equality below would then record nothing at all.
            //
            // An unsent parity means unchanged rather than zero, so the previous value is carried
            // forward: reading a missing property as 0 would fake a restart every time it wrapped
            // back to something else.
            int parity = entity.ViewmodelAnimationParity()
                ?? (seen ? before.AnimationParity : 0);

            int startedAt = ViewmodelAnimation.RestartAt(
                seen ? before.AnimationParity : null,
                parity,
                seen ? before.AnimationStartTick : 0,
                tick);

            SceneViewmodel weapon = new(
                path,
                entity.ViewmodelSequence() ?? 0,
                entity.ViewmodelPlaybackRate() ?? 1f,
                entity.ViewmodelOwner(),
                entity.ViewmodelSlot(),
                entity.IsDrawn,
                weaponItem,
                weaponClass,
                parity,
                startedAt);

            // Unchanged since this entity was last sampled, so there is nothing new to record.
            if (seen && before == weapon)
            {
                continue;
            }

            last[entity.EntityIndex] = weapon;
            into.Add((tick, weapon));
        }
    }

    private static string? ModelFor(EntityState state, ModelPrecache precache, int protocol)
    {
        if (PlayerClass.Equals(state.ClassName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // **A weapon in the world is its WORLD model, which is a different property.** A carried
        // weapon's m_nModelIndex holds the VIEW model, so reading it here drew the owner's
        // first-person arms as scenery — every weapon the player carried, all resolving to
        // c_soldier_arms.mdl, stacked at the hand on top of the real viewmodel (B160).
        //
        // m_iWorldModelIndex is sent by DT_BaseCombatWeapon (basecombatweapon_shared.cpp:2870) and
        // is what the client draws the world model from (tf_weaponbase.cpp:2144).
        //
        // Preferred rather than exclusive: an entity that is not a weapon does not declare the
        // table at all, and a weapon that somehow sent no world model still has its base index to
        // fall back on, which is no worse than what it had before.
        int? index = state.WorldModelIndex() ?? state.ModelIndex();

        // The engine's own compatibility shim: protocol 20 and below packed indices below -1.
        // See ModelPrecache.Unpack and docs/findings/19-model-indices.md.
        return index is { } rawIndex
            ? precache.Path(ModelPrecache.Unpack(rawIndex, protocol))
            : null;
    }

    /// <summary>The eight <c>localSound</c> slots an entity carries, in order.</summary>
    /// <remarks>
    /// All eight always, rather than only the ones <c>localBits</c> marks used: the bits and the
    /// vectors are separate properties and either can arrive without the other, so keeping the raw
    /// slots lets a reader see that rather than having it decided here.
    /// </remarks>
    private static (float X, float Y, float Z)?[] Positions(EntityState entity)
    {
        (float X, float Y, float Z)?[] slots = new (float X, float Y, float Z)?[8];

        for (int slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = entity.SoundscapePosition(slot);
        }

        return slots;
    }

    private static void RecordProp(
        DecodedEntity entity,
        EntityStateTable entities,
        ModelPrecache precache,
        Dictionary<int, ScenePropTrack> tracks,
        List<ScenePropTrack> props,
        List<ScenePropTrack> players,
        int protocol,
        int tick)
    {
        if (entity.UpdateType == EntityUpdateType.Delete)
        {
            if (tracks.Remove(entity.EntityIndex, out ScenePropTrack? finished))
            {
                finished.End(tick);
            }

            return;
        }

        // **Entity zero is the world and the client never draws it as an entity.**
        // C_BaseEntity::ShouldDraw ends `&& (index != 0)` at c_baseentity.cpp:1450, so the
        // exclusion is by index rather than by anything about the model: CWorld holds model
        // index 1, `maps/<name>.bsp`, which is an ordinary brush model naming submodel zero.
        //
        // It never reached here before instance baselines were applied (B132) — the world states
        // its model once, in its class baseline, and never again — so a track for the entire map
        // appeared the moment that was fixed. Drawn, it would be the world laid over the world.
        if (entity.EntityIndex == 0)
        {
            return;
        }

        if (!entities.TryGet(entity.EntityIndex, out EntityState? state))
        {
            return;
        }

        // **A viewmodel is never scenery, and the engine says so in the demo case specifically.**
        // C_BaseViewModel::ShouldDraw, c_baseviewmodel.cpp:277:
        //
        //     if ( engine->IsHLTV() )
        //     {
        //         return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &&
        //                  HLTVCamera()->GetPrimaryTarget() == GetOwner() );
        //     }
        //
        // In eye, and owned by the player being watched — otherwise not drawn at all. There is no
        // camera from which one of these is part of the world, so it does not belong in the prop
        // list at all; the viewmodel pass resolves its own models through ViewmodelAt.
        //
        // **Left in, it draws the weapon twice.** Measured on movement-test-pov-cp_process: three
        // tracks carrying c_soldier_arms.mdl, a model that exists only to sit in front of a
        // first-person camera. Those instances land at the player's eye alongside the ones the
        // viewmodel pass puts there, which is the "2 sticky launchers overlapping each other" the
        // owner reported — and it happens in SourceTV recordings as well, because the branch above
        // is the HLTV branch.
        //
        // **Keyed on the TABLE rather than on the model path**, which matters: a viewmodel declares
        // DT_BaseViewModel and nothing else does, whereas an arms model is merely the most obvious
        // symptom. Excluding by name would leave every weapon viewmodel behind — those carry the
        // same c_ model as the world weapon and are indistinguishable by path.
        // **A viewmodel is never scenery, and the engine says so in the demo case specifically.**
        // C_BaseViewModel::ShouldDraw, c_baseviewmodel.cpp:277:
        //
        //     if ( engine->IsHLTV() )
        //     {
        //         return ( HLTVCamera()->GetMode() == OBS_MODE_IN_EYE &&
        //                  HLTVCamera()->GetPrimaryTarget() == GetOwner() );
        //     }
        //
        // In eye, and owned by the player being watched — otherwise not drawn at all. There is no
        // camera from which one of these is part of the world, and the viewmodel pass resolves its
        // own models through ViewmodelAt.
        //
        // **Belt and braces rather than the load-bearing guard.** DT_BaseViewModel is
        // BEGIN_NETWORK_TABLE_NOBASE, so a viewmodel sends no origin and no parent and is already
        // dropped by the transform check below. This states the rule where it belongs anyway, so
        // that a later change giving these entities a position does not silently put them in the
        // world.
        if (state.ViewmodelModelIndex() is not null)
        {
            return;
        }

        // **No origin is an answer for an attached entity, not a gap.** A hat, a badge and a
        // carried weapon are attached with FollowEntity, which sets EF_BONEMERGE and then zeroes
        // local origin and angles (shared/baseentity_shared.cpp:2360) — the client matches the
        // child model's bones to the parent's BY NAME and takes the parent's matrices, so the
        // child never has a transform and the engine sends none.
        //
        // Requiring one therefore dropped every cosmetic in every demo: measured on cp_process,
        // all 37 live CTFWearable entities carry a model, an owner, a skin and a team, and no
        // position whatsoever. They are recorded at the origin because that is literally what
        // SetLocalOrigin( vec3_origin ) put there; the owner is what says where to draw them.
        // **Attachment is asked FIRST, and the order is the whole of B173's weapon half.** A
        // networked origin is a LOCAL origin — relative to the parent when there is one — so for an
        // attached entity it says nothing about where the thing is in the world. `FollowEntity`
        // ends with `SetLocalOrigin( vec3_origin )` (baseentity_shared.cpp:2371), so a carried
        // weapon sends an origin, and that origin is exactly (0,0,0).
        //
        // Tested the other way round, that zero satisfied the first branch and `Attachment` was
        // never called at all. Measured in the viewer: 17 weapons in the scene, 17 instanced, 14
        // owned, **0 attached, 14 at the world origin** — every carried weapon piled on the map's
        // origin while the three unowned ones, which send a real position, drew correctly.
        //
        // Cosmetics escaped it only by accident: a `CTFWearable` sends no origin whatsoever, so it
        // fell through to the attachment branch and worked. That is why hats were fixed and weapons
        // were not, and why the comment above — "the owner is what says where to draw them" —
        // described a rule the code did not follow.
        int? attachedTo = null;
        (float X, float Y, float Z) origin;

        // **Whether this entity rides a SKELETON or a transform, which is Valve's second branch and
        // was the missing distinction** (B231). `CalcAbsolutePosition` tests `EF_BONEMERGE` — not
        // "does it have a parent" — and everything that is not bone-merged concatenates its own
        // local transform onto its parent's. This project took the first branch for both, so a
        // `prop_dynamic` parented to a `func_door` looked for a skeleton the door does not have.
        bool boneMerged = state.IsBoneMerged;

        if (state.Attachment() is { } owner)
        {
            attachedTo = owner;

            // **A bone-merged entity keeps (0,0,0) and one that is merely parented does NOT.** The
            // bones carry the first outright, so its own origin is meaningless — but a parented
            // entity's `m_vecOrigin` IS its offset from the parent, which is precisely the value
            // `MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent )` needs. Zeroing it for
            // everything discarded that offset, and the grate props then had nothing left to place
            // them with even once a parent transform was available.
            origin = boneMerged ? (0f, 0f, 0f) : state.Origin() ?? (0f, 0f, 0f);
        }
        else if (state.Origin() is { } placed)
        {
            origin = placed;
        }
        else
        {
            return;
        }

        // **A player's model is not on the wire, so it cannot be resolved here.**
        // CTFPlayerClassShared::GetModelName looks it up locally from m_iClass through the class
        // data table, and only m_iszCustomModel travels. A player therefore sends no
        // m_nModelIndex and gets a track with no model rather than no track at all - the poses are
        // what the interpolator needs, and the model is the viewer's to resolve from the install.
        string? model = ModelFor(state, precache, protocol);

        if (model is null)
        {
            return;
        }

        // **A slot is reused, and the SERIAL NUMBER is what identifies the occupant** — the engine's
        // own rule, and the one EntityStateTable already applies to entity state. A rocket that
        // explodes frees its index for the next one, and appending that one's positions to the old
        // track would draw a rocket flying between two unrelated places.
        //
        // This compared MODEL PATHS until 2026-08-16 (B92), which was wrong in both directions. Two
        // consecutive rockets share a model, so the case described above — the one the check existed
        // for — was exactly the case it could not see. And an entity may change model while
        // remaining itself: team_control_point.cpp:569 calls SetModel on every capture, so a point
        // changing hands ended its track and split one object into several.
        if (tracks.TryGetValue(entity.EntityIndex, out ScenePropTrack? track) &&
            !track.Continues(state.SerialNumber))
        {
            track.End(tick);
            tracks.Remove(entity.EntityIndex);
            track = null;
        }

        if (track is null)
        {
            track = new ScenePropTrack(entity.EntityIndex, model, state.SerialNumber);
            tracks[entity.EntityIndex] = track;

            // Player tracks are kept apart from Props. They carry poses and no model, so a
            // consumer walking Props to draw models would find one it cannot draw and could only
            // report as a missing asset - which is exactly the false alarm this split avoids.
            (model.Length == 0 ? players : props).Add(track);
        }

        // Kept current rather than set once: a wearable can arrive before its owner handle does,
        // and a track stuck on the first answer would draw the hat on whoever wore it last.
        track.AttachedTo = attachedTo;

        // Kept current alongside the parent, because an entity can gain or lose EF_BONEMERGE on a
        // later delta and the branch it takes has to follow.
        track.BoneMerged = boneMerged;

        // Ownership regardless of attachment, because the first-person view hides a followed
        // player's weapon by OWNER and a carried weapon that sends an origin is parented to nobody.
        track.OwnedBy = state.Owner();

        // Kept current for the same reason as the others: a weapon is holstered and drawn again as
        // the player switches, so a state fixed at the first delta would freeze whichever weapon
        // happened to be out when the track began.
        track.WeaponState = state.WeaponState();

        // **Which point on the wearer, for the items that hang from one rather than merging.**
        // Kept current for the same reason the wearer is: it can arrive on a later delta than the
        // model, and a track fixed at the first answer would leave the item wherever it started.
        track.AttachmentPoint = state.ParentAttachment();

        (float pitch, float yaw, float roll) = state.Angles() ?? (0f, 0f, 0f);

        // **A player faces where its EYES point, not where m_angRotation says.** A player's
        // m_angRotation is not networked, so reading it gives zero for every player in every demo
        // - measured across the whole corpus as exactly one distinct yaw, twenty-four players
        // included. What TF2 sends is m_angEyeAngles, as two independent properties
        // (tf_player.cpp:731):
        //
        //   SendPropFloat( SENDINFO_VECTORELEM(m_angEyeAngles, 0), 8, SPROP_CHANGES_OFTEN, -90, 90 )
        //   SendPropAngle( SENDINFO_VECTORELEM(m_angEyeAngles, 1), 10, SPROP_CHANGES_OFTEN )
        //
        // And the eye yaw is what drives the body: the server feeds its animation state from it
        // directly, `m_PlayerAnimState->Update( m_angEyeAngles[YAW], m_angEyeAngles[PITCH] )`
        // (tf_player.cpp:2689). So this is the engine's own source for which way a player model
        // points, not a substitute for a value we could not find.
        //
        // Applied here rather than at the ScenePlayer, deliberately: this pose feeds the same
        // ScenePropTrack a rocket uses, so the eye angles are interpolated by the same spline and
        // the same LoopingLerp that knows 359 to 1 is two degrees. TF2 registers m_angEyeAngles as
        // an interpolated variable of its own (c_tf_player.cpp:3874), so interpolating it is
        // matching the client rather than embellishing it.
        if (state.EyeAngles() is { } eyes)
        {
            (pitch, yaw) = (eyes.Pitch, eyes.Yaw);
        }

        track.Add(
            tick,
            new ScenePose
            {
                X = origin.X,
                Y = origin.Y,
                Z = origin.Z,
                Pitch = pitch,
                Yaw = yaw,
                Roll = roll,

                // **Scale defaults to 1, sequence to 0, and both are the engine's own defaults
                // rather than sentinels of ours.** An absent scale is authored size; an absent
                // sequence is sequence 0, which m_nSequence is initialised to
                // (BaseAnimatingOverlay.cpp:104).
                //
                // Sequence was -1 until 2026-08-16, meaning "does not animate". Every drawing
                // consumer clamped it straight back to 0, and the one that compares rather than
                // clamps — InterpolateCycle, where a sequence change is a cut — saw a change that
                // never happened and froze the cycle at that boundary.
                Scale = state.ModelScale() ?? 1f,
                Sequence = state.AnimationSequence() ?? 0,

                // The third factor in Valve's advance, c_baseanimating.cpp:5493. Retained and
                // decoded since the whitelist was written, and read by nothing until now.
                PlaybackRate = state.PlaybackRate() ?? 1f,
                Body = state.Body() ?? 0,

                // **The render state, which is what lets anything fade** (B221). All three are on
                // `DT_BaseEntity`, so they arrive for props, brush entities and players alike —
                // measured on real matches at 410 of 1,973 entities not fully opaque, with 118 at
                // `kRenderNone`, which the engine does not draw at all.
                //
                // Defaults applied HERE rather than left null, because `ScenePose` carries values
                // rather than answers: absent alpha is opaque, absent effect is `kRenderFxNone` and
                // absent mode is `kRenderNormal`, and each of those is the ordinary case rather
                // than an unknown.
                RenderAlpha = state.RenderAlpha(),
                RenderFx = state.RenderFx() ?? 0,
                RenderMode = state.RenderMode() ?? 0,

                // **Skin defaults to 0 because 0 is a real skin**, the model's first family, and a
                // delta-compressed format only sends what changed from it.
                //
                // This line is the one that was missing. Everything downstream was already in
                // place — ScenePropTrack copies Skin through its clone, with a comment explaining
                // why losing it draws every entity in family zero, and the renderer reads
                // prop.Pose.Skin. The value simply never entered the pose, so it was structurally
                // zero and no assertion could tell that from a demo where zero was correct.
                Skin = state.Skin() ?? 0,
                Cycle = state.Cycle() ?? 0f,

                // EF_NODRAW, or gone from the visible set. A taken health pack is hidden rather
                // than deleted because it respawns, so this is a property of the moment.
                Hidden = !state.IsDrawn,
            });
    }

    /// <summary>Gives a player their earliest known team and class before it was first stated.</summary>
    /// <remarks>
    /// **The whole demo is in hand, so a fact learned late can be applied early.** A player is
    /// often sighted for a few frames before <c>CTFPlayerResource</c> says anything about them,
    /// which leaves them greyed at the start of a recording and then correct for the rest of it —
    /// about eight per cent of sightings on a modern demo.
    ///
    /// This is why reading entity state beats taking team from the <c>player_spawn</c> event, as a
    /// streaming parser must: a demo that begins mid-round carries no spawn event, so that route
    /// leaves the player on a default team until the next round. The resource states the answer
    /// continuously, and building offline means the earliest statement can be carried backwards.
    ///
    /// **Backwards only to a player's first sighting, and only from their first known value.** A
    /// team can genuinely change mid-match, so nothing here overwrites a value the demo stated —
    /// it fills the gap before the first one and stops.
    /// </remarks>
    private static void Backfill(List<TimelineFrame> frames)
    {
        Dictionary<int, (int? Team, int? PlayerClass)> earliest = [];

        foreach (TimelineFrame frame in frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (earliest.ContainsKey(player.EntityIndex))
                {
                    continue;
                }

                if (player.Team is not null || player.PlayerClass is not null)
                {
                    earliest[player.EntityIndex] = (player.Team, player.PlayerClass);
                }
            }
        }

        if (earliest.Count == 0)
        {
            return;
        }

        for (int index = 0; index < frames.Count; index++)
        {
            TimelineFrame frame = frames[index];
            List<ScenePlayer>? replaced = null;

            for (int at = 0; at < frame.Players.Count; at++)
            {
                ScenePlayer player = frame.Players[at];

                if (player.Team is not null && player.PlayerClass is not null)
                {
                    continue;
                }

                if (!earliest.TryGetValue(player.EntityIndex, out (int? Team, int? PlayerClass) known))
                {
                    continue;
                }

                replaced ??= [.. frame.Players];

                replaced[at] = player with
                {
                    Team = player.Team ?? known.Team,
                    PlayerClass = player.PlayerClass ?? known.PlayerClass,
                };
            }

            if (replaced is not null)
            {
                frames[index] = frame with { Players = replaced };
            }
        }
    }

    /// <summary>Every model that existed at a tick, with the pose it held then.</summary>
    /// <param name="tick">The moment being shown, which may fall between ticks.</param>
    /// <param name="into">Filled with the visible models; cleared first.</param>
    /// <remarks>
    /// **Fills a caller's collection rather than returning a new one.** A viewer asks this on
    /// every frame, and a match carries over a thousand tracks on a busy map — allocating a list
    /// per frame is garbage the renderer does not need to make. Typed as
    /// <see cref="ICollection{T}"/> rather than <c>List</c> so callers keep their own buffer
    /// without this API dictating which type it is (CA1002).
    ///
    /// Tracks are asked individually because each holds its own keyframes; a track that has not
    /// started or has already ended simply answers nothing.
    /// </remarks>
    public void PropsAt(double tick, ICollection<SceneProp> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (ScenePropTrack track in _props)
        {
            // A hidden entity is not drawn but is still tracked: it is coming back.
            if (track.At(tick) is { Hidden: false } pose)
            {
                into.Add(new SceneProp(
                    track.EntityIndex, track.ModelPath, track.Kind, Moving(track, tick, pose),
                    track.AttachedTo, track.AttachmentPoint, track.OwnedBy, track.WeaponState,
                    track.BoneMerged));
            }
        }
    }

    /// <summary>Fills in the movement pose parameters, which are derived rather than sent.</summary>
    /// <param name="track">The entity's track, which is where the motion is.</param>
    /// <param name="tick">The moment being drawn.</param>
    /// <param name="pose">The interpolated pose.</param>
    /// <returns>The pose with <c>move_x</c> and <c>move_y</c> filled in.</returns>
    /// <remarks>
    /// **These were computed onto one type and read off another, so they were always zero.**
    /// <c>PlayersAt</c> works them out and writes them to <see cref="ScenePlayer"/>; the renderer
    /// reads them from <see cref="SceneProp"/>'s pose, which nothing ever wrote them to. A movement
    /// blend at (0, 0) is the grid's standing corner, so a running player's legs stood still while
    /// the body slid along — and the numbers were right the whole time, in a record nobody asked.
    ///
    /// Filled here rather than at the keyframe, because they are a function of where the entity was
    /// a tenth of a second ago and that is a question about the TRACK rather than about one moment.
    /// Recording them per keyframe would also be wrong at any tick between two.
    ///
    /// Found by a reflection test asserting that no field of a pose comes back at its default —
    /// which is the same class as <c>Body</c> and <c>Skin</c> going missing from the same rebuild.
    /// </remarks>
    private static ScenePose Moving(ScenePropTrack track, double tick, ScenePose pose)
    {
        (float moveX, float moveY) = MoveParameters(track, tick, pose.EyeYaw ?? pose.Yaw);

        // **Speed decides WHICH animation plays, and it was missing the same way.** The viewer picks
        // a sequence from it — MainForm asks SequenceFor(model, speed) — and a null speed skips that
        // block entirely, so a running player kept whatever sequence the demo last stated while the
        // move parameters, had they arrived, would only have blended within it. Two layers of the
        // same defect, from one value computed onto ScenePlayer and read from SceneProp.
        return pose with { Speed = SpeedAt(track, tick), MoveX = moveX, MoveY = moveY };
    }

    /// <summary>Where everyone was at a tick, or the most recent moment before it.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <returns>The players, empty before the first recorded frame.</returns>
    /// <remarks>
    /// **The most recent frame rather than an exact match**, because positions arrive with packets
    /// and the server sends no packet on most ticks. Requiring an exact tick would blink the map
    /// empty between updates.
    /// </remarks>
    public IReadOnlyList<ScenePlayer> PlayersAt(int tick)
    {
        int low = 0;
        int high = _frames.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);

            if (_frames[middle].Tick <= tick)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 ? _frames[found].Players : [];
    }

    /// <summary>Where everyone was at a moment, with positions interpolated as the client does.</summary>
    /// <param name="tick">The moment being shown, which may fall between ticks.</param>
    /// <param name="into">Filled with the players; cleared first.</param>
    /// <remarks>
    /// **A player is interpolated by exactly the same machinery as a rocket, because in the engine
    /// it is the same code.** <c>m_vecOrigin</c> and <c>m_angRotation</c> are registered on
    /// <c>C_BaseEntity</c> — <c>AddVar(&amp;m_vecOrigin, &amp;m_iv_vecOrigin, LATCH_SIMULATION_VAR)</c>
    /// at <c>c_baseentity.cpp:905</c> — and a player is a <c>C_BaseEntity</c>. There is no separate
    /// player position path to reproduce. TF2 adds exactly one interpolated variable of its own,
    /// <c>m_angEyeAngles</c> (<c>c_tf_player.cpp:3874</c>).
    ///
    /// So the position here comes from the entity's own <see cref="ScenePropTrack"/> — the same
    /// hermite spline, the same time renormalisation — rather than from a second implementation
    /// that would drift from the first.
    ///
    /// **Team, class and health are not interpolated, and that is measured rather than assumed.**
    /// Neither appears in any <c>AddVar</c> call in the client. They are discrete facts: a player
    /// between 125 and 68 health was never on 96, and one changing team was never on a team
    /// between the two.
    /// </remarks>
    public void PlayersAt(double tick, ICollection<ScenePlayer> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        foreach (ScenePlayer player in PlayersAt((int)Math.Floor(tick)))
        {
            // **A dead player keeps the position recorded for them**, which is where they fell.
            // The entity's own track follows whoever they are spectating, so interpolating it
            // would drag the body across the map to stand inside a living player.
            if (!player.IsAlive)
            {
                into.Add(player);
                continue;
            }

            if (!_trackByEntity.TryGetValue(player.EntityIndex, out ScenePropTrack? track) ||
                track.At(tick) is not { } pose)
            {
                into.Add(player);
                continue;
            }

            (float moveX, float moveY) = MoveParameters(track, tick, pose.EyeYaw ?? pose.Yaw);

            // **Yaw travels with the position, from the same pose.** Taking one and discarding the
            // other is what left every player facing north the moment they stopped being a dot:
            // the number was decoded and interpolated already, and simply not carried the last few
            // lines.
            into.Add(player with
            {
                X = pose.X,
                Y = pose.Y,
                Z = pose.Z,
                Yaw = pose.Yaw,
                Speed = SpeedAt(track, tick),
                MoveX = moveX,
                MoveY = moveY,
            });
        }
    }

    /// <summary>The interpolation track for one entity, when it has one.</summary>
    /// <param name="entityIndex">The entity's slot.</param>
    /// <returns>Its track, or <c>null</c> when nothing about it was recorded.</returns>
    /// <remarks>
    /// **Exposed so a test can predict what <see cref="PlayersAt(double, ICollection{ScenePlayer})"/>
    /// should report.** Asserting a player's yaw against a literal would test the demo rather than
    /// the code; asserting it against the track this reads from tests the plumbing between them,
    /// which is where the number was being dropped.
    /// </remarks>
    public ScenePropTrack? TrackFor(int entityIndex) =>
        _trackByEntity.TryGetValue(entityIndex, out ScenePropTrack? track) ? track : null;

    /// <summary>How fast a track is moving horizontally at a moment.</summary>
    /// <remarks>
    /// **Differenced from the positions, because velocity is networked only to its owner.**
    /// <c>m_vecVelocity[0..2]</c> sit inside <c>DT_LocalPlayerExclusive</c>
    /// (<c>server/player.cpp:8117</c>), sent through <c>SendProxy_SendLocalDataTable</c> — so a
    /// SourceTV recording carries nobody's velocity at all, because SourceTV is not any of the
    /// players, and a point-of-view recording carries only the recorder's.
    ///
    /// That makes differencing the only thing that works generally rather than a workaround: it is
    /// the sole option for every player in an STV demo and for eleven of twelve in a POV one. The
    /// recorder's own velocity IS available in a POV demo and would be exact; using it is a refinement
    /// this does not make yet.
    ///
    /// An animation state needs speed to tell standing from running — <c>MOVING_MINIMUM_SPEED</c>
    /// is 0.5 units a second in <c>base_playeranimstate.h</c>.
    ///
    /// Sampled over a tenth of a second rather than one tick. A single tick is 15 milliseconds and
    /// the positions are interpolated, so differencing two adjacent samples measures the
    /// interpolator's noise as much as the player's motion; a tenth of a second is long enough to
    /// be a speed and short enough to still be this moment's.
    ///
    /// Vertical motion is left out, which is what <c>GetOuterXYSpeed</c> does — a falling player is
    /// not running.
    /// </remarks>
    private static float SpeedAt(ScenePropTrack track, double tick)
    {
        const double window = 0.1d;

        double ticks = window / Math.Max(0.001f, 0.015f);

        if (track.At(tick) is not { } now || track.At(Math.Max(0d, tick - ticks)) is not { } was)
        {
            return 0f;
        }

        float across = now.X - was.X;
        float along = now.Y - was.Y;

        return MathF.Sqrt((across * across) + (along * along)) / (float)window;
    }

    /// <summary>Which way a track is travelling, in degrees, or null when it is still.</summary>
    /// <remarks>
    /// Differenced over the same window as <see cref="SpeedAt"/> and for the same reason: velocity
    /// is inside <c>DT_LocalPlayerExclusive</c>, so a SourceTV demo carries nobody's.
    ///
    /// Null rather than zero when stationary, because zero degrees is due east and a player
    /// standing still is not facing east — it is a different question with no answer, and
    /// answering it anyway makes every idle player run on the spot toward the same corner of the
    /// map.
    /// </remarks>
    private static float? HeadingAt(ScenePropTrack track, double tick)
    {
        const double window = 0.1d;

        double ticks = window / Math.Max(0.001f, 0.015f);

        if (track.At(tick) is not { } now || track.At(Math.Max(0d, tick - ticks)) is not { } was)
        {
            return null;
        }

        float across = now.X - was.X;
        float along = now.Y - was.Y;

        // Below this the direction is numerical noise in the position rather than movement:
        // MOVING_MINIMUM_SPEED is 0.5 units a second (base_playeranimstate.h), which over a tenth
        // of a second is 0.05 units.
        return ((across * across) + (along * along)) < 0.0025f
            ? null
            : MathF.Atan2(along, across) * (180f / MathF.PI);
    }

    /// <summary>The <c>move_x</c> and <c>move_y</c> pose parameters for a moving player.</summary>
    /// <param name="track">The player's own track, which is differenced for a heading.</param>
    /// <param name="tick">The moment being drawn.</param>
    /// <param name="bodyYaw">Which way the player is facing, in degrees.</param>
    /// <returns>The unit vector of travel in the body's frame, or zero when standing still.</returns>
    /// <remarks>
    /// **Ported from <c>CMultiPlayerAnimState::ComputePoseParam_MoveYaw</c>**
    /// (<c>multiplayer_animstate.cpp:1575</c>):
    ///
    /// <code>
    /// float flYaw = flAngle - m_PoseParameterData.m_flEstimateYaw;
    /// flYaw = AngleNormalize( -flYaw );
    /// flYaw = SnapYawTo( flYaw );
    /// vecCurrentMoveYaw.x =  cos( DEG2RAD( flYaw ) );
    /// vecCurrentMoveYaw.y = -sin( DEG2RAD( flYaw ) );
    /// </code>
    ///
    /// **The snap is Valve's and it is not a rounding convenience.** <c>SnapYawTo</c>
    /// (<c>:1443</c>) forces the direction to the nearest of eight compass points using thresholds
    /// of 23, 67, 113 and 157 degrees, so a player strafing slightly off true still plays the
    /// clean sideways animation rather than a permanent blend of two. Leaving it out makes every
    /// player's legs waver between animations as the differenced heading jitters.
    ///
    /// **<c>m_flEstimateYaw</c> is approximated by the body yaw**, which is what this project has.
    /// The engine tracks a separate estimate that lags the eyes while turning on the spot, so a
    /// player spinning in place will differ slightly here. Recorded rather than hidden; it needs
    /// the rest of the turn-in-place state (B61) to do properly.
    /// </remarks>
    private static (float X, float Y) MoveParameters(
        ScenePropTrack track, double tick, float bodyYaw)
    {
        if (HeadingAt(track, tick) is not { } heading)
        {
            return (0f, 0f);
        }

        // **estimateYaw − eyeYaw, and the order is the whole of a defect that lasted until it was
        // measured.** The engine computes `flYaw = flAngle - m_flEstimateYaw` and then
        // `AngleNormalize( -flYaw )`, so the two negations cancel and what reaches the cosine is
        // the direction of travel minus the way the body faces. This project had it the other way
        // round, which is zero for a player running dead forward — so a measurement of a forward
        // run could not see it — and which swaps strafing left with strafing right.
        float yaw = Normalize(heading - bodyYaw);
        (float sine, float cosine) = MathF.SinCos(yaw * (MathF.PI / 180f));

        float x = cosine;
        float y = -sine;

        // **"push edges out to -1 to 1 box"**, Valve's own comment. A unit vector puts a diagonal
        // at 0.707 on each axis, which is halfway along a nine-way grid's cell, so the corner
        // animations authored for the diagonals were never reached. Dividing by the larger
        // component sends every direction to the edge of the box instead of the circle.
        float scale = MathF.Max(MathF.Abs(x), MathF.Abs(y));

        // Guarded exactly where Valve guards it — `if ( flInvScale != 0.0f )`. The vector is a
        // cosine and a sine, so this only fires on a degenerate value, but dividing by it would
        // give two NaNs that reach the blend grid as a plausible-looking mid-cell.
        if (scale != 0f)
        {
            x /= scale;
            y /= scale;
        }

        // **The speed scaling is NOT applied, and this is the one part of the engine's function
        // left out.** After the push-out it does:
        //
        //     float flMaxSpeed = GetBasePlayer()->GetSequenceGroundSpeed( GetSequence() );
        //     if ( flMaxSpeed > flSpeed ) { x *= flSpeed / flMaxSpeed; y *= flSpeed / flMaxSpeed; }
        //
        // which pulls a player moving slower than their animation was authored for back towards
        // the middle of the grid. flMaxSpeed is the authored ground speed of the CHOSEN SEQUENCE,
        // read from mstudiomovement_t in the model file — and this layer decodes a demo and has
        // never opened a model. Recorded in B101 rather than approximated: a guessed maximum would
        // scale every player by a number with no relationship to what they are playing.
        return (x, y);
    }

    /// <summary>Brings an angle into −180 to 180.</summary>
    /// <param name="degrees">Any angle, including one several turns out.</param>
    /// <returns>The same direction, expressed once.</returns>
    /// <remarks>
    /// **One direction must have one representation, or nothing can compare two of them.** The wire
    /// sends yaw as 0..360 and everything here stores (−180, 180]; a player's facing was measured
    /// held as 220.997 and −139.003 at once, which is the same direction and a full turn apart. Any
    /// comparison or interpolation across that boundary is then wrong by 360 — and the boundary is
    /// due south, where players spend a great deal of time.
    ///
    /// Internal so the invariant can be asserted over the whole circle rather than at the one value
    /// somebody happened to measure. A wrap defect lives at a boundary, and a single example never
    /// sits on one.
    /// </remarks>
    internal static float NormalizeAngle(float degrees) => Normalize(degrees);

    private static float Normalize(float degrees)
    {
        float wrapped = degrees % 360f;

        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    // **SnapYawTo was implemented here and is deliberately gone.** It is real engine code
    // (multiplayer_animstate.cpp:1443) and forces a direction to the nearest of eight compass
    // points, but ComputePoseParam_MoveYaw calls it only under `if ( mp_slammoveyaw.GetBool() )` —
    // and that cvar is declared `ConVar mp_slammoveyaw( "mp_slammoveyaw", "0", FCVAR_REPLICATED |
    // FCVAR_DEVELOPMENTONLY, "Force movement yaw along an animation path." )`. Default off, and
    // development-only, so no shipped TF2 client takes that branch.
    //
    // This project applied it unconditionally, with a comment arguing it stopped the legs wavering
    // between animations as the differenced heading jitters. That reasoning is plausible and is not
    // what the engine does: it quantised every direction to eight, so a player running at 30° off
    // their facing animated as though at 45°. Kept in the history rather than in the code, because
    // an unused private method is dead weight and the reason it went belongs with the decision.

    private static int? First(EntityState player, string[] keys)
    {
        foreach (string key in keys)
        {
            if (player.Integer(key) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
