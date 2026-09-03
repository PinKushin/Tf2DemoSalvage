using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// <param name="Conditions">
/// The five <c>m_nPlayerCond</c> bitfields, read as <c>CTFPlayerShared::InCond</c> does.
/// </param>
/// <param name="DisguiseClass">Which class a disguised spy appears to be, <c>m_nDisguiseClass</c>.</param>
/// <param name="DisguiseTeam">Which team they appear to be on, <c>m_nDisguiseTeam</c>.</param>
/// <param name="DisguiseMaskClass">
/// <c>m_nMaskClass</c>, read only when an enemy spy is disguised AS a spy — the one case where the
/// mask offset comes from somewhere other than the disguise class (<c>tf_player_shared.h:375</c>).
/// </param>
/// <param name="IsEnemy">
/// Whether this player is an enemy of the RECORDER, which is what <c>IsEnemyPlayer</c> asks of the
/// local player (<c>c_tf_player.cpp:5384</c>). A disguise only fools the other team.
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
/// <param name="Gestures">
/// The gestures this player has going, one per occupied slot in slot order, or <c>null</c> when
/// they have none. Filled from the <c>CTEPlayerAnimEvent</c> temp entities the demo carries, which
/// is the ONLY place a player's gestures appear: <c>tf_player.cpp:774</c> excludes
/// <c>overlay_vars</c> from the player's send table, so <c>m_AnimOverlay</c> is never networked for
/// a player. See <see cref="PlayerGestureFeed"/>.
/// </param>
/// <param name="ClientSideAnimated">
/// Whether the client runs this player's animation cycle itself — <c>m_bClientSideAnimation</c>,
/// one unsigned bit from <c>DT_BaseAnimating</c> (<c>baseanimating.cpp:250</c>).
/// <c>CTFPlayer::CTFPlayer</c> calls <c>UseClientSideAnimation()</c> unconditionally
/// (<c>tf_player.cpp:953</c>), so in practice every TF player sends it set.
///
/// **It is on the player rather than left to the prop path because a player never goes through the
/// prop path.** <c>PropsAt</c> copies the flag off the track; <c>PlayersAt</c> builds its own
/// record, so without this the value the demo stated is dropped between the timeline and the
/// renderer — and a player whose cycle is not advanced holds frame zero while their position
/// interpolates, which is B280.
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

    // **The spy disguise, and the side we are on.** `C_TFPlayer::ValidateModelIndex` and `GetSkin`
    // both branch on `InCond( TF_COND_DISGUISED ) && IsEnemyPlayer()`, so all three travel
    // together: the conditions say whether a disguise is up, the disguise fields say what it is,
    // and `IsEnemy` says whether we are the one meant to be fooled.
    //
    // `IsEnemy` is computed HERE rather than in the scene, because `IsEnemyPlayer` compares against
    // the LOCAL player (`c_tf_player.cpp:5384`) and in a recording that is the recorder — which
    // only the timeline knows.
    PlayerConditions Conditions = default,
    int? DisguiseClass = null,
    int? DisguiseTeam = null,
    int? DisguiseMaskClass = null,
    bool IsEnemy = false,
    float? EyePitch = null,
    float? EyeYaw = null,
    float? AimYaw = null,
    int? WaterLevel = null,
    int? ActiveWeapon = null,
    string? WeaponClass = null,
    int? WeaponItem = null,
    int? ObserverMode = null,
    bool ClientSideAnimated = false,
    IReadOnlyList<SceneGesture>? Gestures = null)
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

/// <summary>Where the time went while a demo was decoded, in milliseconds.</summary>
/// <param name="Commands">Splitting the file into demo commands.</param>
/// <param name="Schema">Parsing <c>dem_datatables</c> into send tables.</param>
/// <param name="Messages">Decoding each packet's net messages.</param>
/// <param name="Entities">Entity delta decode, plus applying and recording what it produced.</param>
/// <param name="Sampling">The per-packet walks: viewmodels, fog and soundscape.</param>
/// <param name="Viewmodels">The viewmodel share of <paramref name="Sampling"/>, which contains it.</param>
/// <param name="Total">The whole of <see cref="DemoTimeline.Build"/>.</param>
/// <remarks>
/// **Building the timeline is thirty seconds on a fourteen-minute match and had no columns at
/// all** (B265). It was one opaque number logged from outside, which is the state the FRAME was in
/// before it was split — and splitting the frame is what took it from 96 to 447 fps, because the
/// fat turned out to be somewhere nobody had guessed. The same rule applies to a number the owner
/// waits through every time he opens a real demo.
///
/// **`Sampling` is separated from `Entities` deliberately**, because they are opposite shapes:
/// entity work is proportional to what the demo SAID, and the sampling walks are proportional to
/// what exists times how many packets there are. Only the second kind can be large for a reason
/// nobody intended.
/// </remarks>
public readonly record struct TimelinePhases(
    double Commands,
    double Schema,
    double Messages,
    double Entities,
    double Sampling,
    double Viewmodels,
    double Total);

/// <summary>Where everyone was at one tick.</summary>
/// <param name="Tick">The demo tick this was recorded at.</param>
/// <param name="Players">Every player with a known position.</param>
/// <param name="RecorderTeam">
/// The recording player's team at this tick, or <c>null</c> when there is no local player — a
/// SourceTV recording, where the engine's own <c>pLocalPlayer &amp;&amp;</c> guards fall through.
/// <para>
/// **Per frame rather than per demo, because a player can switch teams mid-recording.** Everything
/// that compares against the local player — <c>IsEnemyPlayer</c>, a spawn wall's own team — would
/// otherwise answer with whichever team happened to be resolved last.
/// </para>
/// </param>
/// <param name="RoundState">
/// <c>m_iRoundState</c> from the game rules at this tick, or <c>null</c> when the demo carries no
/// game rules entity. <c>GR_STATE_TEAM_WIN</c> is 5.
/// </param>
public readonly record struct TimelineFrame(
    int Tick,
    IReadOnlyList<ScenePlayer> Players,
    int? RecorderTeam = null,
    int? RoundState = null);

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

    /// <summary>The table that declares <c>m_iState</c>, so a class reaching it is a weapon.</summary>
    /// <remarks>
    /// `SendPropInt( SENDINFO(m_iState), 8, SPROP_UNSIGNED )` lives in the MAIN
    /// `DT_BaseCombatWeapon` table (`basecombatweapon_shared.cpp:2871`) rather than in
    /// `DT_LocalWeaponData`, which is what makes another player's weapon answerable at all.
    /// </remarks>
    private const string CombatWeaponTable = "DT_BaseCombatWeapon";

    /// <summary>The entity that carries the round.</summary>
    /// <remarks>
    /// **`CTFGameRulesProxy`, not the teamplay one it inherits from.** A demo's schema declares
    /// three proxies — `CGameRulesProxy`, `CTeamplayRoundBasedRulesProxy` and `CTFGameRulesProxy` —
    /// and TF2 instantiates the last, which reaches `m_iRoundState` through
    /// `teamplayroundbased_gamerules_data`.
    /// </remarks>
    private const string GameRulesClass = "CTFGameRulesProxy";

    /// <summary>Where the round state is, flattened.</summary>
    private const string RoundStateProperty = "DT_TeamplayRoundBasedRules.m_iRoundState";


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
    /// <summary>How far behind the packet each entity's simulation tick was, bucketed.</summary>
    private int[] _simulationLag = new int[LagBuckets];

    /// <summary>Buckets in <see cref="SimulationLag(int)"/>, covering −8 to +8 ticks.</summary>
    public const int LagBuckets = 17;

    /// <summary>Where a lag of zero sits in the histogram.</summary>
    public const int LagZero = 8;

    /// <summary>The slot counting updates that carried no simulation time, past the buckets.</summary>
    private const int LagUnknownBucket = LagBuckets;

    /// <summary>How many entity updates lagged the packet by a given number of ticks.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is no lag.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The engine timestamps an interpolation history entry with the entity's SIMULATION time,
    /// not with the packet's tick** — <c>C_BaseEntity::GetLastChangeTime</c> returns
    /// <c>GetSimulationTime()</c> for simulation-latched variables, which is origin and angles
    /// among others, and <c>OnLatchInterpolatedVariables</c> hands that to every watcher
    /// (<c>c_baseentity.cpp:2806</c>). This project stamps every keyframe with the packet tick, so
    /// this histogram is the size of that divergence, measured rather than argued (B273).
    ///
    /// **Measured 2026-09-02.** On the 2013 SourceTV foundry demo, 32% of updates carry a
    /// simulation tick equal to the packet's, 31% are four ticks ahead of it, and 35% lag by eight
    /// or more — the last group being entities that do not simulate every tick, whose stale
    /// timestamp costs nothing because they are not moving. On the 2011 viaduct point-of-view
    /// recording the four-tick cluster is 81%.
    ///
    /// **Kept because the follow-up needs it.** Changing what a keyframe is stamped with alters
    /// what is on screen, so it is the owner's call rather than a silent correction — and this is
    /// the number that call should be made on.
    /// </remarks>
    public int SimulationLag(int bucket) => _simulationLag[bucket];

    /// <summary>Entity updates whose entity never sent a simulation time.</summary>
    /// <remarks>
    /// **The control for the histogram.** A count of zero here says every update carried the value,
    /// so the distribution above describes the demo rather than describing which entities happened
    /// to answer. Measured as zero on both demos it was first run against — which is what makes the
    /// clusters in it worth reading.
    /// </remarks>
    public int SimulationLagUnknown { get; private set; }

    /// <summary>The same, for the animation clock.</summary>
    private int[] _animationLag = new int[LagBuckets];

    /// <summary>How many entity updates' animation stamp lagged the packet by a given amount.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is no lag.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The engine's OTHER latch clock**, and it is a separate measurement because the two answer
    /// separately: <c>GetLastChangeTime</c> returns <c>GetAnimTime()</c> for
    /// <c>LATCH_ANIMATION_VAR</c> — pose parameters, bone controllers, flexes, overlay layers —
    /// where the simulation clock serves origin and angles.
    /// </remarks>
    public int AnimationLag(int bucket) => _animationLag[bucket];

    /// <summary>Entity updates whose entity never sent an animation time.</summary>
    /// <remarks>
    /// **Expected to be LARGE where the simulation equivalent is zero**, and that is not a fault:
    /// a resting prop simulates without animating, and a player using client-side animation sends
    /// none at all — <c>SendProxy_AnimTime</c> asserts <c>!IsUsingClientSideAnimation()</c>.
    /// </remarks>
    public int AnimationLagUnknown { get; private set; }

    /// <summary>The gap between the two clocks, where an update carried both.</summary>
    private int[] _clockGap = new int[LagBuckets];

    /// <summary>Simulation tick minus animation tick, for updates that sent both.</summary>
    /// <param name="bucket">0 to <see cref="LagBuckets"/> − 1; <see cref="LagZero"/> is agreement.</param>
    /// <returns>The count, with the end buckets holding everything beyond ±8.</returns>
    /// <remarks>
    /// **The measurement that decides whether one keyframe can serve both clocks.** The engine
    /// keeps a separate interpolation history per variable and stamps each with its own
    /// <c>GetLastChangeTime</c>; this project keeps ONE keyframe per entity per packet, carrying
    /// simulation-latched fields and animation-latched fields together. That is faithful exactly to
    /// the extent that the two clocks agree when both are sent — so the answer is measured rather
    /// than assumed, and it is here for anyone who later wonders whether the single history was a
    /// shortcut.
    /// </remarks>
    public int ClockGap(int bucket) => _clockGap[bucket];

    /// <summary>The simulation-lag histogram split by server class.</summary>
    /// <remarks>
    /// **What tells a clock offset from a real disagreement.** If every class sits in the same
    /// bucket the difference between the packet tick and the simulation tick is a constant, which
    /// moves the whole scene together and is invisible; if classes sit in DIFFERENT buckets the
    /// entities disagree with each other by that many ticks, which is what a viewer sees.
    /// </remarks>
    public IReadOnlyDictionary<string, int[]> SimulationLagByClass { get; private set; } =
        new Dictionary<string, int[]>();


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

    /// <summary>Where <see cref="Build"/> spent its time. Zero for a timeline built any other way.</summary>
    public TimelinePhases Phases { get; private init; }

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
    /// <param name="frames">
    /// Frames to answer <see cref="FrameAt"/> from, for tests whose subject is per-frame state —
    /// the recorder's team, which stage C bakes into a persistent prop and must rebuild on a
    /// switch. Empty when omitted, which answers a null team at every tick.
    /// </param>
    internal static DemoTimeline ForTracks(
        List<ScenePropTrack> tracks, List<TimelineFrame>? frames = null) =>
        new(frames ?? [], tracks);

    /// <summary>A timeline whose tracks are PLAYERS, with one frame naming them.</summary>
    /// <param name="tracks">The tracks, which go in the player list rather than the prop list.</param>
    /// <param name="players">The players that frame carries, matched to the tracks by entity.</param>
    /// <returns>A timeline whose <see cref="PlayersAt(double, ICollection{ScenePlayer})"/> answers.</returns>
    /// <remarks>
    /// **The distinction this exists to make is the one B258 turned on.** `ForTracks` puts its
    /// tracks in `_props`, and `PropsAt` is therefore the only way to reach them — which is how two
    /// tests came to assert that the PROP path computes `move_x`, `move_y` and `Speed`. It does not
    /// any more, and in production it never saw a player to compute them for: player tracks live in
    /// `_playerTracks` and `PropsAt` iterates `_props`. Measured on `tf2-2026-pub-pov-clean`, zero
    /// of 79 prop groups are `CTFPlayer`.
    /// </remarks>
    internal static DemoTimeline ForPlayerTracks(
        List<ScenePropTrack> tracks, IReadOnlyList<ScenePlayer> players) =>
        new(
            [new TimelineFrame(0, players), new TimelineFrame(13, players)],
            props: null,
            playerTracks: tracks);

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
        long buildFrom = Stopwatch.GetTimestamp();
        long commandTicks;
        long schemaTicks;
        long messageTicks = 0;

        // Seventeen buckets for −8 to +8 ticks, and one past them for updates that said nothing —
        // the control that tells a real distribution from a partial one (B273).
        int[] simulationLag = new int[LagBuckets + 1];
        int[] animationLag = new int[LagBuckets + 1];
        int[] clockGap = new int[LagBuckets + 1];
        Dictionary<string, int[]> lagByClass = [];
        long entityTicks = 0;
        long samplingTicks = 0;
        long viewmodelTicks = 0;

        // Entity indices seen carrying a viewmodel model index, kept ascending (B265).
        List<int> viewmodelEntities = [];

        // Which entities THIS packet updated, and which weapon each viewmodel last named — the two
        // things the viewmodel sampler needs to know whether it can skip re-deriving a sample.
        HashSet<int> touchedEntities = [];
        Dictionary<int, int> viewmodelWeapon = [];

        DemoHeader header = DemoHeader.Parse(file.Span);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        long phaseFrom = Stopwatch.GetTimestamp();

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file[DemoHeader.SizeBytes..])];

        commandTicks = Stopwatch.GetTimestamp() - phaseFrom;

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

        phaseFrom = Stopwatch.GetTimestamp();

        DemoSchema schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        schemaTicks = Stopwatch.GetTimestamp() - phaseFrom;

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        // **Given the decoder, because an entering entity is a delta against its class baseline.**
        // Without it every entity whose whole state equals its baseline accumulates as an empty
        // one - see IEntityBaselines, and B132, which is what that cost.
        EntityStateTable entities = new(decoder);

        // **Class names come from dem_datatables, not from svc_ClassInfo.** TF2 sets the
        // "create on client" flag and sends no names, so a reader waiting for that message names
        // nothing and finds no players while decoding every entity correctly.
        // **Which classes bone-merge themselves, computed once from the schema** (B231).
        // `CEconWearable::Spawn` calls `AddEffects( EF_BONEMERGE )` outside its server-only guard
        // (`econ_wearable.cpp:112`), so every client sets it for every wearable it creates and the
        // flag never travels — measured, 26 of 26 `CTFWearable` entities send no `m_fEffects` at
        // all. This viewer is the only reader that never runs `Spawn`, so it derives the same
        // answer from the class, which `dem_datatables` does carry.
        //
        // Per class rather than per entity because the answer cannot vary between two instances of
        // one class, and the walk is over send tables rather than over anything cheap.
        HashSet<int> mergesItself = [];

        // **Which classes ARE combat weapons, so an absent `m_iState` can mean its default rather
        // than "not a weapon"** (B245). The two readings of absence want opposite answers: a
        // `CTFWearable` never sends the property and must always draw, while a weapon that has not
        // restated it since re-entering the visible set is at `WEAPON_NOT_CARRIED` and must not.
        //
        // Only the schema can tell them apart, and it can: `m_iState` is declared by
        // `DT_BaseCombatWeapon` (`basecombatweapon_shared.cpp:2871`), so a class whose table chain
        // reaches that table has the field and a class that does not never had it.
        //
        // Per class for the same reason `mergesItself` is: the answer cannot vary between two
        // instances, and the walk is over send tables rather than over anything cheap.
        HashSet<int> combatWeapons = [];

        // **Names for temp entities, which carry a class id and nothing else** (B282). A player's
        // animation layers are excluded from the wire entirely (`tf_player.cpp:774`), so a reload,
        // a flinch or an attack gesture reaches a demo only as a `CTEPlayerAnimEvent` effect — and
        // an effect names its class by id.
        Dictionary<int, string> effectClassNames = [];

        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
            effectClassNames[serverClass.Id] = serverClass.ClassName;

            if (SchemaClasses.BoneMergesItself(schema, serverClass.TableName))
            {
                mergesItself.Add(serverClass.Id);
            }

            if (SchemaClasses.Inherits(schema, serverClass.TableName, CombatWeaponTable))
            {
                combatWeapons.Add(serverClass.Id);
            }
        }

        // **Every player's gesture slots, filled from the temp entity stream** (B282). Not per
        // frame: a gesture is an EVENT with a start, and the slot it lands in holds until something
        // replaces it or its sequence runs out — which only the scene can decide, because only the
        // scene has the model that says how long the sequence is.
        PlayerGestureFeed gestures = new();

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

            // Per packet, because "did the demo mention this entity" is a question about THIS
            // packet and nothing earlier.
            touchedEntities.Clear();

            // Materialised so the decode is timed here rather than being spread through the
            // switch below by lazy enumeration, which would attribute it to whichever case
            // happened to pull the next message.
            long readFrom = Stopwatch.GetTimestamp();

            IReadOnlyList<INetMessage> messages =
                [.. NetMessageReader.Read(command.Payload.Span, state).Messages];

            messageTicks += Stopwatch.GetTimestamp() - readFrom;

            foreach (INetMessage message in messages)
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

                    // **The SERVER's tick, which is not the demo's** (B273). `net_Tick` carries
                    // `gpGlobals->tickcount` as the server had it, and that is the number
                    // `m_flSimulationTime`'s offset was encoded against — a demo's own command tick
                    // starts near zero while a server has been up for hours.
                    // **The SERVER's tick, which is not the demo's.** `net_Tick` carries
                    // `gpGlobals->tickcount` as the server had it — the number
                    // `m_flSimulationTime` and `m_flAnimTime` are encoded against — while a demo's
                    // own commands are numbered from the start of the recording. The two axes are
                    // never mixed: what leaves this decode is a LAG, the difference between two
                    // server-axis numbers, which is then applied to a demo tick (B273).
                    case NetTickMessage netTick:
                        entities.PacketTick = netTick.Tick;
                        continue;

                    // **A player's gestures arrive here and nowhere else** (B282). TF2 excludes
                    // `overlay_vars` from the player's send table (`tf_player.cpp:774`), so the
                    // animation layers a reload or a flinch would occupy are not on the wire at
                    // all; what IS sent is `CTEPlayerAnimEvent` (`tf_player.cpp:324`), a temp
                    // entity naming the player and a `PlayerAnimEvent_t`.
                    //
                    // **The posture is read at ARRIVAL**, because the engine picks the activity
                    // inside `DoAnimationEvent` (`tf_playeranimstate.cpp:969`) — a reload begun
                    // crouched stays the crouching reload even if the player stands during it.
                    case TempEntitiesMessage effects when effects.BodyBits > 0:
                        RecordGestures(
                            decoder, effects, command.Tick * interval, effectClassNames, entities, gestures);
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

                long entityFrom = Stopwatch.GetTimestamp();

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    entities.Apply(entity);

                    touchedEntities.Add(entity.EntityIndex);

                    // **Noticed here, where the cost is proportional to what the demo said**
                    // (B265). An entity is a viewmodel for its whole life, so this asks once per
                    // update rather than once per entity per packet — and the sampler below then
                    // visits twenty-two entities instead of six hundred. Kept ascending so the
                    // recorded order matches what walking `All` produced.
                    if (entities.TryGet(entity.EntityIndex, out EntityState? applied) &&
                        applied.ViewmodelModelIndex() is not null &&
                        !viewmodelEntities.Contains(entity.EntityIndex))
                    {
                        int seat = viewmodelEntities.BinarySearch(entity.EntityIndex);

                        viewmodelEntities.Insert(seat < 0 ? ~seat : seat, entity.EntityIndex);
                    }

                    RecordProp(
                        entity, entities, precache, tracks, props, playerTracks,
                        mergesItself, combatWeapons, protocol, command.Tick, interval,
                        simulationLag, animationLag, clockGap, lagByClass);
                }

                entityTicks += Stopwatch.GetTimestamp() - entityFrom;

                moved = true;
            }

            // **After the packet's messages, not before.** Sampling first reads the table as it
            // stood at the PREVIOUS tick, so an entity that enters on this packet is missed
            // entirely — and on a demo whose viewmodel enters once and never changes, that means
            // it is never recorded at all.
            long sampleFrom = Stopwatch.GetTimestamp();

            RecordViewmodels(
                entities, precache, protocol, command.Tick, lastViewmodel, viewmodels,
                viewmodelEntities, touchedEntities, viewmodelWeapon);

            viewmodelTicks += Stopwatch.GetTimestamp() - sampleFrom;

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

            samplingTicks += Stopwatch.GetTimestamp() - sampleFrom;

            if (!moved)
            {
                continue;
            }

            List<ScenePlayer> players = [];
            EntityState? resource = entities.OfClass(ResourceClass).FirstOrDefault();

            // **The recorder's team, resolved BEFORE the loop that needs it.** `IsEnemyPlayer`
            // compares against the local player, and in a recording that is whoever recorded it.
            // Reading it from the entity table rather than from the list being built is what makes
            // the answer independent of the order players happen to be walked in — a first version
            // searched the partial list and reported every player below the recorder's entity index
            // as friendly, whatever their team.
            //
            // Null for a SourceTV recording, which has no local player: the engine's switch falls
            // through to `return false`, so a spectator sees every spy undisguised.
            int? recorderTeam =
                recorderSlot is { } recording
                && entities.TryGet(recording + 1, out EntityState? recorder)
                    ? resource?.Integer($"m_iTeam.{recording}")
                        ?? First(recorder, TeamProperties)
                    : null;

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

                        // **Landing ends the jump gesture, and this is what was missing** (B284).
                        // `CTFPlayerAnimState::HandleJumping` (`tf_playeranimstate.cpp:1498`):
                        //
                        //     else if ( gpGlobals->curtime - m_flJumpStartTime > 0.2f )
                        //     {
                        //         if ( GetBasePlayer()->GetFlags() & FL_ONGROUND )
                        //         {
                        //             m_bJumping = false;
                        //             RestartMainSequence();
                        //             if ( bNewJump ) RestartGesture( GESTURE_SLOT_JUMP, ACT_MP_JUMP_LAND );
                        //         }
                        //     }
                        //
                        // **A demo carries no event for landing.** The double jump arrives as a
                        // `CTEPlayerAnimEvent`; the landing that replaces it is a decision the
                        // client makes from the ground flag, so a reader driven by events alone
                        // leaves `ACT_MP_DOUBLEJUMP` — a FULL-BODY animation — playing after the
                        // player is back on the ground, and it takes the whole skeleton with it.
                        // That is what laid one scout flat while every other player stood.
                        gestures.Landed(player.EntityIndex, command.Tick * interval);
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
                            : null,

                    // **The disguise, and whose side we are on.** `C_TFPlayer::ValidateModelIndex`
                    // and `GetSkin` both branch on
                    // `InCond( TF_COND_DISGUISED ) && IsEnemyPlayer()`, so the three travel
                    // together and `Disguise` applies them.
                    Conditions: player.Conditions(),
                    DisguiseClass: player.DisguiseClass(),
                    DisguiseTeam: player.DisguiseTeam(),
                    DisguiseMaskClass: player.DisguiseMaskClass(),

                    // **`IsEnemyPlayer` asks about the LOCAL player** (`c_tf_player.cpp:5384`),
                    // which in a recording is whoever recorded it — and only the timeline knows
                    // that. Computed here so the scene never has to guess whose eyes it is behind.
                    IsEnemy: IsEnemyOfRecorder(
                        recorderTeam,
                        resource?.Integer($"m_iTeam.{slot}") ?? First(player, TeamProperties)),

                    // **Whether the CLIENT advances this player's cycle** (B280).
                    // `CTFPlayer::CTFPlayer` calls `UseClientSideAnimation()` unconditionally
                    // (`tf_player.cpp:953`), so `m_bClientSideAnimation` arrives set for every
                    // player and `C_BaseAnimating::UpdateClientSideAnimation`
                    // (`c_baseanimating.cpp:5134`) runs `FrameAdvance( 0.0f )` on them each frame.
                    // A player's own `m_flCycle` is therefore never a driving value — it decodes
                    // to zero and stays there — so a renderer told this is false leaves them in
                    // one pose while their position keeps interpolating.
                    //
                    // **Read off the entity here rather than off the track, deliberately.** The
                    // track's copy is the prop path's, and a player never takes the prop path;
                    // deriving it a second way is exactly the second route that has produced wrong
                    // answers here before. This is the same accessor the track's own assignment
                    // uses, applied to the same entity.
                    ClientSideAnimated: player.ClientSideAnimation() is { } clientSide &&
                        clientSide != 0,

                    // **The gesture slots, because a player's animation layers are excluded from
                    // the wire** (B282, `tf_player.cpp:774`). Each slot holds the last trigger the
                    // demo raised for it and the tick it arrived on; whether it is still playing
                    // depends on the sequence its activity resolves to, which only the scene can
                    // answer. Null when the player has raised nothing, so the common case costs no
                    // allocation.
                    Gestures: GesturesFor(gestures, player.EntityIndex)));
            }

            // **Only when the tick advanced.** Several commands can share a tick, and recording a
            // frame for each would make the timeline's own ordering ambiguous — PlayersAt would
            // then depend on which of them it happened to find first.
            // **The round, from the game rules entity there is exactly one of.** `m_iRoundState`
            // is declared in `DT_TeamplayRoundBasedRules` and reaches `CTFGameRulesProxy` through
            // `teamplayroundbased_gamerules_data` — confirmed present in a modern demo's own
            // schema. Null when the demo has no such entity, which every pre-2009 era specimen
            // does not.
            int? roundState = entities.OfClass(GameRulesClass).FirstOrDefault()?
                .Integer(RoundStateProperty);

            if (frames.Count > 0 && frames[^1].Tick >= command.Tick)
            {
                frames[^1] = new TimelineFrame(
                    frames[^1].Tick, players, recorderTeam, roundState);
                continue;
            }

            frames.Add(new TimelineFrame(command.Tick, players, recorderTeam, roundState));
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
            _simulationLag = simulationLag,
            SimulationLagUnknown = simulationLag[LagUnknownBucket],
            _animationLag = animationLag,
            AnimationLagUnknown = animationLag[LagUnknownBucket],
            _clockGap = clockGap,
            SimulationLagByClass = lagByClass,
            Phases = new TimelinePhases(
                Milliseconds(commandTicks),
                Milliseconds(schemaTicks),
                Milliseconds(messageTicks),
                Milliseconds(entityTicks),
                Milliseconds(samplingTicks),
                Milliseconds(viewmodelTicks),
                Milliseconds(Stopwatch.GetTimestamp() - buildFrom)),
        };
    }

    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

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
    /// <summary>One player's gesture slots, or null when they have none.</summary>
    /// <param name="feed">The gesture feed.</param>
    /// <param name="entityIndex">The player.</param>
    /// <returns>The slots in slot order, or null.</returns>
    /// <remarks>
    /// **Null rather than an empty list**, because most players hold no gesture in most frames and
    /// this runs once per player per sampled tick. The scene reads it as "nothing to layer".
    /// </remarks>
    private static List<SceneGesture>? GesturesFor(
        PlayerGestureFeed feed, int entityIndex)
    {
        List<SceneGesture> gestures = [];

        feed.For(entityIndex, gestures);

        return gestures.Count > 0 ? gestures : null;
    }

    /// <summary>Decodes a temp entities body and records any player gestures in it.</summary>
    /// <param name="decoder">The entity decoder, which knows the effect tables.</param>
    /// <param name="message">The message.</param>
    /// <param name="seconds">Demo time when it arrived, in seconds.</param>
    /// <param name="classNames">Class id to name, since an effect names its class by id.</param>
    /// <param name="entities">The entity table, for the player's posture at this moment.</param>
    /// <param name="into">The feed to record into.</param>
    /// <remarks>
    /// **A body that will not read is skipped rather than fatal**, which is the rule everywhere
    /// else in this project: a demo is salvaged, and a temp entities body is independent of every
    /// other message in the packet.
    ///
    /// **The posture comes from the entity table**, not from the sampled `ScenePlayer`, because
    /// this runs while the packet is being applied and the sampler has not run yet. It is the same
    /// entity and the same accessors either way.
    /// </remarks>
    private static void RecordGestures(
        EntityDecoder decoder,
        TempEntitiesMessage message,
        double seconds,
        Dictionary<int, string> classNames,
        EntityStateTable entities,
        PlayerGestureFeed into)
    {
        try
        {
            foreach (DecodedTempEntity effect in decoder.DecodeTempEntities(
                message.Body.Span, message.Count, message.BodyBits))
            {
                if (!classNames.TryGetValue(effect.ClassId, out string? className) ||
                    !string.Equals(
                        className, PlayerGestureFeed.EventClassName, StringComparison.Ordinal))
                {
                    continue;
                }

                into.Record(className, effect, seconds, PostureOf(effect, entities));
            }
        }
        catch (Exception error)
            when (error is System.IO.InvalidDataException or System.IO.EndOfStreamException)
        {
            // Skipped for the same reason a sounds body is: everything else in this packet is
            // independent of it, and salvaging what is readable is the point of the project.
        }
    }

    /// <summary>What the player named by a gesture event was doing when it arrived.</summary>
    /// <param name="effect">The gesture event.</param>
    /// <param name="entities">The entity table.</param>
    /// <returns>The context the activity choice is made against.</returns>
    /// <remarks>
    /// **Six of the seven context fields.** Each changes WHICH activity a gesture resolves to, and
    /// an activity that resolves to no sequence draws nothing at all — so a field left unread is a
    /// missing animation rather than a slightly wrong one.
    ///
    /// **The weapon in hand answers two of them**, exactly as the engine asks
    /// (`tf_playeranimstate.cpp:987`): `bIsMinigun` is
    /// `pWpn->GetWeaponID() == TF_WEAPON_MINIGUN` and `bIsSniperRifle` is
    /// `WeaponID_IsSniperRifleOrBow( … )`, which covers the rifle, its decapitation and classic
    /// variants and the bow (`tf_weaponbase.cpp:6328`). A demo does not carry a weapon ID, but it
    /// carries the weapon entity's SERVER CLASS, which is one-to-one with it.
    ///
    /// **`TF_COND_ZOOMED` is condition bit 1** (`tf_shareddefs.h:691`), and the zoom matters as
    /// much as the rifle: an unzoomed sniper fires the ordinary stand activity.
    ///
    /// **`IsLoser` is NOT read, and it needs more than this pass has.**
    /// `CTFPlayerShared::IsLoser` (`tf_player_shared.cpp:13654`) wants the round state, the winning
    /// team, whether the match is competitive, the stun flags and a disguised spy's disguise team.
    /// It selects `ACT_MP_DOUBLEJUMP_LOSERSTATE` over `ACT_MP_DOUBLEJUMP` and nothing else, so the
    /// gap is one animation during humiliation.
    ///
    /// **Air-walk is not asked here either.** It is derived over time from vertical speed
    /// (`PlayerActivity.AirwalkRiseSpeed`) rather than read off the entity, and this runs inside
    /// the packet walk where that history is not to hand. A reload begun mid-rocket-jump therefore
    /// resolves to the standing form rather than the air-walking one.
    /// </remarks>
    private static GestureContext PostureOf(DecodedTempEntity effect, EntityStateTable entities)
    {
        int player = 0;

        foreach (DecodedProperty property in effect.Properties)
        {
            if (string.Equals(
                property.Definition.Property.Name,
                PlayerGestureFeed.PlayerIndexProperty,
                StringComparison.Ordinal))
            {
                player = (int)property.Value.AsInt;
                break;
            }
        }

        if (player <= 0 || !entities.TryGet(player, out EntityState? state))
        {
            return default;
        }

        string? weapon = state.ActiveWeapon() is { } held &&
            entities.TryGet(held, out EntityState? carried)
                ? carried.ClassName
                : null;

        return new GestureContext(
            InDuck: state.Flags() is { } flags &&
                (flags & PlayerActivityState.Ducking) != 0,
            InSwim: state.WaterLevel() >= PlayerActivityState.WaistDeepWaterLevel,
            IsMinigun: string.Equals(weapon, MinigunClass, StringComparison.Ordinal),
            IsSniperZoomed: IsSniperRifleOrBow(weapon) &&
                state.Conditions().Has(PlayerConditions.Zoomed));
    }

    /// <summary>The server class of the weapon <c>bIsMinigun</c> tests for.</summary>
    private const string MinigunClass = "CTFMinigun";

    /// <summary>Whether a weapon class is one <c>WeaponID_IsSniperRifleOrBow</c> accepts.</summary>
    /// <param name="weaponClass">The weapon entity's server class, or null.</param>
    /// <returns>Whether it is a sniper rifle or the bow.</returns>
    /// <remarks>
    /// **`tf_weaponbase.cpp:6328` and `:6338`**, by class rather than by weapon ID: the rifle, the
    /// decapitation rifle, the classic rifle and the compound bow. A demo carries the class and not
    /// the ID, and the two are one-to-one.
    /// </remarks>
    private static bool IsSniperRifleOrBow(string? weaponClass) =>
        weaponClass is "CTFSniperRifle"
            or "CTFSniperRifleDecap"
            or "CTFSniperRifleClassic"
            or "CTFCompoundBow";

    private static void RecordViewmodels(
        EntityStateTable entities,
        ModelPrecache precache,
        int protocol,
        int tick,
        Dictionary<int, SceneViewmodel> last,
        List<(int Tick, SceneViewmodel Weapon)> into,
        List<int> candidates,
        HashSet<int> touched,
        Dictionary<int, int> weaponOf)
    {
        // **Only entities already known to BE viewmodels, not every entity in the table** (B265).
        // This walked `entities.All` on every packet and asked each one for a viewmodel model
        // index: on `z1800` that is roughly six hundred entities across 14,386 packets — about
        // 8.6 million lookups, of which twenty-two ever answer. Measured before touching it, it was
        // **22.4 seconds of a 30.3-second load: 74% of the time to open a demo.**
        //
        // `RecordProp`'s own doc comment already forbade exactly this — *"Walking the whole entity
        // table every frame would ask several hundred entities to repeat themselves across a
        // hundred thousand frames"* — and it is written a few lines below a loop that did it.
        //
        // **The candidate list is a superset, filtered by the same predicate as before**, so the
        // recorded output is identical rather than approximately so: an index enters when an
        // update shows it carrying a viewmodel index (proportional to what the demo SAID, which is
        // the shape the rest of this loop already has), and an index whose entity has gone or been
        // reused simply fails the test below, exactly as it would have when walking everything.
        // Ascending order, because `All` yields in index order and the recorded list keeps
        // same-tick entries in the order they were sampled.
        for (int at = 0; at < candidates.Count; at++)
        {
            if (!entities.TryGet(candidates[at], out EntityState? entity))
            {
                continue;
            }

            // **Re-sampled only when this packet touched it, or touched the weapon it names**
            // (B265). Everything below — the model path, the item, the econ attribute lists — is
            // derived from those two entities and nothing else, so if the demo said nothing about
            // either, the sample it would produce is the one already recorded. The old code built
            // the whole thing on every packet and then threw it away on `before == weapon`: about
            // 316,000 constructions on `z1800` to detect a few hundred changes, which is the
            // second half of this method's cost after the scan.
            //
            // `RecordProp`'s own comment is the rule being applied: *"A demo states what changed;
            // this records exactly that."*
            //
            // The weapon entity is remembered from the last sample, because it is reached THROUGH
            // the viewmodel — an econ attribute changing on a weapon whose viewmodel was quiet
            // still has to be seen.
            bool sampled = last.ContainsKey(candidates[at]);

            if (sampled &&
                !touched.Contains(candidates[at]) &&
                !(weaponOf.TryGetValue(candidates[at], out int held) && touched.Contains(held)))
            {
                continue;
            }

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
            EconAttributeWire? weaponEcon = null;

            // Remembered so the gate above can watch it: this is the only entity besides the
            // viewmodel itself whose state reaches the sample.
            weaponOf[candidates[at]] = entity.ViewmodelWeapon() ?? -1;

            if (entity.ViewmodelWeapon() is { } weaponEntity &&
                entities.TryGet(weaponEntity, out EntityState? carried))
            {
                weaponItem = carried.ItemDefinitionIndex();
                weaponClass = carried.ClassName;

                // **The weapon's attributes travel with the viewmodel sample** (B252), read from
                // the same entity `m_hWeapon` already resolved — the festivizer on the gun in your
                // own hands is the same list the world draw reads. Null when both lists are empty,
                // so a bare weapon costs the dedup nothing.
                IReadOnlyList<EconAttributeValue> local =
                    carried.EconAttributes(EconAttributeList.Local);
                IReadOnlyList<EconAttributeValue> forDemos =
                    carried.EconAttributes(EconAttributeList.NetworkedForDemos);

                if (local.Count > 0 || forDemos.Count > 0)
                {
                    long? high = carried.Integer("DT_ScriptCreatedItem.m_iItemIDHigh");
                    long? low = carried.Integer("DT_ScriptCreatedItem.m_iItemIDLow");

                    weaponEcon = new EconAttributeWire(
                        local,
                        forDemos,
                        high is { } h && low is { } l
                            && !(h == uint.MaxValue && l == uint.MaxValue));
                }
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
                startedAt,
                weaponEcon);

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
        HashSet<int> mergesItself,
        HashSet<int> combatWeapons,
        int protocol,
        int tick,
        float interval,
        int[] simulationLag,
        int[] animationLag,
        int[] clockGap,
        Dictionary<string, int[]> lagByClass)
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

        // **Both halves, and neither alone is the answer** (B231).
        //
        // `CalcAbsolutePosition` (`c_baseentity.cpp:4387`) tests `EF_BONEMERGE` to choose between
        // riding a parent's SKELETON and concatenating onto its transform. Weapons carry that flag
        // on the wire; wearables do not, because `CEconWearable::Spawn` adds it on the CLIENT for
        // every wearable any client creates (`econ_wearable.cpp:112`, outside the server-only
        // guard) and so it never needs to travel.
        //
        // Measured on a real match: 26 of 26 `CTFWearable` and 3 of 3 `CTFPowerupBottle` send no
        // `m_fEffects` at all, while every weapon sends the flag. Reading only the wire puts every
        // hat and cosmetic on the transform path — which broke the viewer outright — and treating
        // every parent as a merge is the mistake in the other direction, which leaves a
        // `CDynamicProp` hung on a `func_door` searching for a skeleton brushwork does not have.
        bool boneMerged = state.IsBoneMerged || mergesItself.Contains(entity.ClassId);

        // **Resolved through the table, so the handle's SERIAL is checked** (B231).
        // `RecvProxy_IntToEHandle` keeps index and serial and dereferencing compares the serial
        // against the slot's current occupant; masking it away resolves a dangling handle to a
        // real, existing, different entity. Measured: a spawn resupply locker composed onto a door
        // because its parent slot had been reused, landing thousands of units away.
        if (entities.Resolve(state.AttachmentHandle()) is { } owner)
        {
            attachedTo = owner;

            // **A bone-merged follower's own origin is meaningless and a parented one's is not.**
            // `FollowEntity` calls `SetLocalOrigin( vec3_origin )` on the follower
            // (`baseentity_shared.cpp:2371`), so a merged entity genuinely sends zero and the
            // bones place it; everything else sends its OFFSET from the parent, which is the value
            // `MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent )` needs. Zeroing both
            // discards the second.
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

        // **Which econ item this is, when it is one.** A weapon's model comes from the item schema
        // rather than from the wire — `pItem->GetPlayerDisplayModel( iClass, team )`,
        // `econ_entity.cpp:1167` — and some weapons network no model index at all. Measured on
        // `cp_fulgur`, every weapon with an owner: a rocket launcher sends both indices, a
        // flamethrower sends the world model, and every `CWeaponMedigun` sends NEITHER, while all
        // of them state their item. 211 is the stock Medi Gun.
        int? item = state.ItemDefinitionIndex();

        // **A null model is not the end of the road for something that says which ITEM it is**
        // (B231). This returned outright, so a medigun — no `m_nModelIndex`, no
        // `m_iWorldModelIndex`, item 211 — produced no track at all, and every medigun on every
        // other player went undrawn. `WeaponModels.For` has resolved exactly this for the viewmodel
        // and the followed player since B222; the weapon entities other players carry were the one
        // caller that never asked.
        if (model is null && item is null)
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
            // Empty rather than null for a weapon whose model is not on the wire yet: the track's
            // path is a string, and `Follow` fills it in when a later update names one.
            track = new ScenePropTrack(
                entity.EntityIndex, model ?? string.Empty, state.SerialNumber);
            tracks[entity.EntityIndex] = track;

            // Player tracks are kept apart from Props. They carry poses and no model, so a
            // consumer walking Props to draw models would find one it cannot draw and could only
            // report as a missing asset - which is exactly the false alarm this split avoids.
            // **An ITEM is a model the draw path can still find, so it crosses over** (B231). The
            // reasoning above is right about a player, whose model-less track can never resolve to
            // anything, and wrong about a weapon whose model is merely not on the wire: `items_game`
            // names it, `WeaponModels.For` reads it, and the only thing standing between them was
            // this line putting the track where nothing drawing models ever looks.
            //
            // Narrow deliberately — an item index, not merely "has an owner" — so the false
            // missing-asset alarm the split exists to prevent stays prevented. A track with no
            // model AND no item still has nothing anyone could resolve.
            (string.IsNullOrEmpty(model) && item is null ? players : props).Add(track);
        }

        // Kept current for the same reason the model is: an entity can be created from a baseline
        // that omits its item and be told which one it is on a later update.
        track.ItemDefinitionIndex = item ?? track.ItemDefinitionIndex;
        track.ClassName = state.ClassName ?? track.ClassName;

        // **The wire's half of `IterateAttributes`, recomputed only when this update touched an
        // attribute element** (B234). The accessor walks the whole property bag, so running it on
        // every positional delta would pay for fifty thousand scans a demo; an update that carried
        // no element-scoped property cannot have changed either list.
        foreach (DecodedProperty carried in entity.Properties)
        {
            if (!carried.Definition.ElementScoped)
            {
                continue;
            }

            // `INVALID_ITEM_ID` is `(itemid_t)-1` (`econ_item_constants.h:443`): both networked
            // halves all-ones. Never-sent reads as invalid, which routes an era demo — no econ
            // system at all — to the definition's own attributes at resolve time.
            long? high = state.Integer("DT_ScriptCreatedItem.m_iItemIDHigh");
            long? low = state.Integer("DT_ScriptCreatedItem.m_iItemIDLow");

            bool validId = high is { } h && low is { } l
                && !(h == uint.MaxValue && l == uint.MaxValue);

            track.Econ = new EconAttributeWire(
                state.EconAttributes(EconAttributeList.Local),
                state.EconAttributes(EconAttributeList.NetworkedForDemos),
                validId);

            break;
        }

        // Kept current for the same reason the item is: a disguise's gear is created when the
        // disguise goes up, and the flag arrives with it.
        track.OfDisguise = state.OfDisguise() || track.OfDisguise;

        // Kept current for the same reason: a brush entity states its team on creation and an
        // update that does not mention it must not erase it.
        track.TeamNumber = First(state, TeamProperties) ?? track.TeamNumber;

        // **A parity change means this animation began again, so its clock restarts**
        // (`C_BaseAnimating::OnDataChanged`, `c_baseanimating.cpp:4737`). Everything downstream
        // measures `elapsed = seconds - AnimationStartSeconds`, and leaving that at zero made
        // `elapsed` the whole recording — a one-shot sequence finished before its first frame drew.
        //
        // **A counter rather than a comparison of sequence numbers**, because a cabinet used twice
        // plays `open` twice and only the counter says the second one began
        // (`m_nNewSequenceParity = ( m_nNewSequenceParity + 1 ) & EF_PARITY_MASK`, `:5574`).
        //
        // An entity that never sends the field keeps the clock it was created with, which is right:
        // nothing has told it to restart.
        // **Two signals, because the engine has two modes and reads a different field in each.**
        // `C_BaseAnimating::OnDataChanged` (`c_baseanimating.cpp:5021`) checks
        // `m_bClientSideFrameReset` ONLY when `m_bClientSideAnimation` is set, and resets cycle
        // interpolation on `m_nNewSequenceParity` (`:4737`) regardless. Measured on `cp_fulgur`, the
        // spawn cabinets are client-side animated — they send `m_bClientSideAnimation` 1 and no
        // `DT_ServerAnimationData.m_flCycle` whatsoever — so the toggle is their restart, and a fix
        // built on parity alone did not move them.
        //
        // **The frame reset is a TOGGLE and only its CHANGE means anything.**
        // `CBaseAnimating::ResetClientsideFrame` is `m_bClientSideFrameReset =
        // !(bool)m_bClientSideFrameReset` (`server/baseanimating.cpp:3055`), so reading it as a
        // boolean "should reset" would restart on every update where it happened to be one.
        // **A first sighting is not a restart.** An entity's opening update states whatever value
        // it holds, and treating that as a change would stamp the clock for every prop the moment
        // it appears — wrong for one that has been idling since the map loaded.
        bool restarted = false;

        if (state.NewSequenceParity() is { } parity)
        {
            restarted |= track.LastSequenceParity is not null && parity != track.LastSequenceParity;
            track.LastSequenceParity = parity;
        }

        // **Both are consumed even once the first has fired**, because the stored value must track
        // the wire whether or not anyone acted on it, or the next genuine change is missed.
        //
        // **And the toggle counts only in CLIENT-side mode**, which is the guard Valve puts around
        // it and which a first version of this left out — caught by auditing every EntityState
        // accessor for a production caller and finding `ClientSideAnimation` had none.
        // `c_baseanimating.cpp:5021`:
        //
        //     if ( m_bClientSideAnimation )
        //         if ( m_bClientSideFrameReset != m_bLastClientSideFrameReset )
        //             ResetClientsideFrame();
        //
        // A server-animated entity that toggles the field would otherwise restart on it, which is
        // the same class of mistake as reading the toggle's VALUE instead of its change.
        // **Kept current on every update, as PostDataUpdate joins and leaves the list on every
        // update** (B259). A server-animated entity that is later told to animate itself must start
        // being advanced, and one told to stop must stop.
        if (state.ClientSideAnimation() is { } clientSide)
        {
            track.ClientSideAnimated = clientSide != 0;
        }

        if (state.ClientSideFrameReset() is { } reset)
        {
            restarted |= track.LastFrameReset is not null
                && reset != track.LastFrameReset
                && state.ClientSideAnimation() is not 0 and not null;

            track.LastFrameReset = reset;
        }

        if (restarted)
        {
            track.AnimationStartSeconds = tick * interval;
        }

        // **The model, kept current for the same reason and on the engine's own adjacent line.**
        // `C_BaseEntity::PostDataUpdate` (`c_baseentity.cpp:2603`) calls `HierarchySetParent` and
        // then `ValidateModelIndex`, both ABOVE the `DATA_UPDATE_CREATED` test, so both run on
        // every update. This followed the parent and fixed the model at construction.
        //
        // A creating update rarely carries the model, because it is a delta against the class
        // INSTANCE BASELINE — one representative entity's state. Measured on `cp_fulgur`, slot 432,
        // the BLU spawn's windowed door: created from two properties with the baseline's model
        // index 1154 (`resupply_locker.mdl`) and the baseline's origin (3440 -2096 240), which is
        // `prop_locker_blu_5`'s world position out of the map. The next update said 1177 and
        // (2 0 -59), and the track never heard it. Nine other entities took the same identity.
        track.Follow(model);

        // Kept current rather than set once: a wearable can arrive before its owner handle does,
        // and a track stuck on the first answer would draw the hat on whoever wore it last.
        track.AttachedTo = attachedTo;

        // Kept current alongside the parent, because an entity can gain or lose the flag on a
        // later delta and the branch it takes has to follow.
        track.BoneMerged = boneMerged;

        // Ownership regardless of attachment, because the first-person view hides a followed
        // player's weapon by OWNER and a carried weapon that sends an origin is parented to nobody.
        track.OwnedBy = state.Owner();

        // **The weapon state is written into the POSE below rather than here** (B244). The comment
        // this replaces said "kept current … a state fixed at the first delta would freeze whichever
        // weapon happened to be out when the track began" — which had the hazard exactly right and
        // the cure exactly backwards. Keeping a scalar current while parsing does not make it
        // current when READ: by then it holds the last value the whole demo wrote, so the state
        // froze at the demo's end instead of at its beginning.

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

        // **The size of a divergence, counted where the two numbers are both in hand** (B273). The
        // engine stamps an interpolation history entry with the entity's simulation time and this
        // project stamps a keyframe with the packet; the difference is what this bucket holds.
        if (state.SimulatedAtTick is { } simulated)
        {
            int lag = Math.Clamp(state.SimulationBaseTick - simulated, -LagZero, LagZero);

            simulationLag[lag + LagZero]++;

            // **Which entities sit in which cluster, because that decides whether the divergence is
            // visible at all.** A constant offset shared by everything is a clock difference and
            // moves nothing on screen; entities disagreeing with EACH OTHER is what a viewer sees.
            if (state.ClassName is { } className)
            {
                if (!lagByClass.TryGetValue(className, out int[]? counts))
                {
                    counts = new int[LagBuckets];
                    lagByClass[className] = counts;
                }

                counts[lag + LagZero]++;
            }
        }
        else
        {
            simulationLag[LagUnknownBucket]++;
        }

        if (state.AnimatedAtTick is { } animated)
        {
            animationLag[Math.Clamp(state.SimulationBaseTick - animated, -LagZero, LagZero)
                + LagZero]++;

            // **The question that decides whether one keyframe can serve both clocks.** The engine
            // keeps a separate interpolation history per variable and stamps each with its own
            // clock; this project keeps ONE keyframe per entity per packet. If the two clocks agree
            // whenever both are sent, that single keyframe is faithful and no split is needed.
            if (state.SimulatedAtTick is { } alsoSimulated)
            {
                clockGap[Math.Clamp(alsoSimulated - animated, -LagZero, LagZero) + LagZero]++;
            }
        }
        else
        {
            animationLag[LagUnknownBucket]++;
        }

        // **Stamped with when the value APPLIED, not with the packet that carried it** (B273).
        // `OnLatchInterpolatedVariables` gives every simulation-latched variable — origin and
        // angles among them — the entity's own `GetSimulationTime()` as its history timestamp
        // (`c_baseentity.cpp:2806`), and this project stamped the packet tick.
        //
        // **The lag is applied to the demo's tick because it is a DIFFERENCE**, taken between two
        // numbers on the server's axis. The two axes are never mixed: the server's tickcount and
        // the demo's command numbering are unrelated, and subtracting one from the other is what
        // made the first attempt at this measurement pure noise.
        //
        // Measured on the 2013 SourceTV foundry recording, this moves `CTFPlayer` samples by four
        // ticks on exactly half of their updates — the two clusters are 50/50 — so the players,
        // which are the fastest things on screen, were being sampled with 60 ms of jitter that no
        // other entity shared.
        track.Add(
            tick,
            new ScenePose
            {
                // **When the animation now playing began**, so `elapsed` downstream is the time
                // since the server stamped it rather than since the recording opened.
                AnimationStartSeconds = track.AnimationStartSeconds,
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

                // **The layers this entity sends, which a player never has** (B285).
                // `tf_player.cpp:774` excludes the array from the player's send table, so these
                // belong to everything else that animates — sentries carry two to four, measured
                // on `z1800.dem`, and teleporters, dispensers, sappers and taunt props carry them
                // too. `AccumulateLayers` walks them in `m_nOrder` over the main sequence.
                Layers = state.AnimationLayers(),

                // **Networked, and read by `CalcBoneAdj` to bend one bone** (B287). Decoded since
                // the whitelist was written and applied by nothing until now — a sentry's barrel
                // and a door's hinge are wired to these rather than to an animation.
                BoneControllers = state.BoneControllers(),

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

                // **The distance fade's two bounds** (B268). Zero for both is "does not fade",
                // which is the common case and the engine's own first branch, so an entity that
                // never sent them behaves exactly as it did before.
                FadeMinimumDistance = state.FadeMinimumDistance() ?? 0f,
                FadeMaximumDistance = state.FadeMaximumDistance() ?? 0f,

                // **What the entity says its pose parameters are.** Empty for a player, because
                // `tf_player.cpp:769` excludes the array from their send table and the client
                // computes theirs — so this cannot override the ones `PoseValues` derives.
                PoseParameters = state.PoseParameters(),

                // **Compared, not read** (B275). `DoAnimationEvents` restarts its walk when this
                // differs from the value it saw last, which is how a taunt played twice sounds
                // twice while its sequence number never moves.
                ResetEventsParity = state.ResetEventsParity() ?? 0,

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

                // **In the POSE because it changes while the entity lives, which is the test the
                // other nine track scalars pass and this one does not** (B244). A weapon is
                // holstered and drawn again as its owner switches, and `ShouldDraw` reduces to
                // `m_iState == WEAPON_IS_ACTIVE` for another player's weapon — so a value read off
                // the track answers with the state at the END of the recording and a medic whose
                // medigun was away at the final tick drew empty-handed for the whole demo.
                //
                // Null rather than 0 for anything that is not a weapon: `m_iState` is declared by
                // `DT_BaseCombatWeapon`, so a wearable never sends it, and 0 is `WEAPON_NOT_CARRIED`
                // — a real state a dropped weapon has. Conflating the two would strip the shield
                // off every demoman.
                // **Absent means the DEFAULT for a weapon, and "not a weapon" for anything else**
                // (B245). `CL_CopyNewEntity` decodes an entering entity from its baseline, so a
                // weapon that has not restated `m_iState` since re-entering the visible set is at
                // `WEAPON_NOT_CARRIED` — a real value, and one `ShouldDraw` refuses.
                //
                // Reading absence as null instead makes it "this is not a weapon", which is the
                // answer that DRAWS, and it is right for a `CTFWearable`: the Mantreads, a
                // demoman's shield and a sniper's Razorback are worn whatever is in the hands. The
                // schema is what separates them, because `m_iState` is declared by
                // `DT_BaseCombatWeapon` and a class that never reaches that table never had it.
                WeaponState = state.WeaponState()
                    ?? (combatWeapons.Contains(entity.ClassId)
                        ? EntityState.WeaponNotCarried
                        : null),

                // EF_NODRAW, or gone from the visible set. A taken health pack is hidden rather
                // than deleted because it respawns, so this is a property of the moment.
                Hidden = !state.IsDrawn,
            },
            appliedAt: state.SimulatedAtTick is { } simulatedAt
                ? tick - (state.SimulationBaseTick - simulatedAt)
                : tick,

            // **The engine's other latch clock** (B274). `GetLastChangeTime` returns
            // `GetAnimTime()` for the cycle and the pose parameters where it returns
            // `GetSimulationTime()` for origin and angles, and a server sets the two at different
            // moments — on the 2013 SourceTV foundry recording they disagree by more than eight
            // ticks on 95.5% of the updates carrying both.
            //
            // Falls back to the packet tick for an entity that sends none, which is every player:
            // TF2's use client-side animation and `SendProxy_AnimTime` asserts they encode no
            // animation time at all.
            animationAppliedAt: state.AnimatedAtTick is { } animatedAt
                ? tick - (state.SimulationBaseTick - animatedAt)
                : tick);
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
    /// <param name="interpolate">
    /// The entities to interpolate — the engine's <c>g_InterpolationList</c> (B259). Anything not
    /// named holds its last stated pose instead of being blended, which is what the engine leaves a
    /// non-member at. Null interpolates everything, which is the safe direction and what every
    /// caller that does not care relies on.
    /// </param>
    public void PropsAt(
        double tick, ICollection<SceneProp> into, IReadOnlySet<int>? interpolate = null)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();

        // **The signature stays abstract on purpose.** CA1002 refuses `List<T>` in a public API and
        // is right to; `Collection<T>` would be slower still, since it wraps. Narrowing inside costs
        // one type test, and a caller that passes something else keeps working through the slow
        // path rather than being refused.
        List<SceneProp>? fast = into as List<SceneProp>;
        HashSet<int>? named = interpolate as HashSet<int>;

        // **The recorder's team AT THIS TICK**, because a player can switch sides mid-recording and
        // "is this my own team's spawn wall" then changes answer. Null for a SourceTV recording,
        // which has no local player at all — and the engine's `pLocalPlayer &&` guard means that
        // case DRAWS, so an entity of no known relation must come out false rather than true.
        int? recorderTeam = FrameAt((int)Math.Floor(tick))?.RecorderTeam;

        // **Sampling is proportional to what changed, not to what exists** (B259 fix 3, stage C).
        // The engine never walks its entity array per frame: `ProcessInterpolatedList` walks
        // `g_InterpolationList` — *"Interpolate the minimal set of entities that need it"*
        // (`c_baseentity.cpp:3123`) — which an entity joins when an update latches a changed
        // variable (`OnLatchInterpolatedVariables`, `:2832`) and leaves the moment its
        // interpolation has nothing more to do (`bNoMoreChanges`, `:2927`).
        //
        // **Our updates are already on disk, so `NoteChanged` becomes arithmetic**: each track
        // names the next tick at which its own answer can change (`Motion`), those wakes sit in
        // one queue, and a rebuild pays for the boundaries it crossed plus the tracks mid-lerp.
        // Everything else serves the prop it already built.
        //
        // **A seek is the case the engine does not have** (D131): state surviving across frames is
        // wrong the moment the clock jumps backwards, so a rewind — and the rarer team switch,
        // which is baked into every prop as `OfRecordersTeam` — rebuilds everything from nothing.
        if (!_sampleSynced || tick < _sampledTo || recorderTeam != _sampledTeam)
        {
            ResyncSample(tick, interpolate, named, recorderTeam);
        }
        else
        {
            AdvanceSample(tick, interpolate, named, recorderTeam);
        }

        _sampleSynced = true;
        _sampledTo = tick;
        _sampledTeam = recorderTeam;

        // The list is refilled to nearly the same length every frame, so growing it from empty
        // re-allocates the backing array a dozen times a second for nothing.
        fast?.EnsureCapacity(_props.Count);

        // **The collate is the engine's own closing step** — `BuildRenderablesList` also emits a
        // fresh per-frame list from its maintained structures. This walk touches one reference and
        // one null test per track; the work a track used to cost here (a binary search, an
        // interpolation, a sixteen-field construction) happens only when something changed.
        foreach (ScenePropTrack track in _props)
        {
            if (track.Live is not { } prop)
            {
                continue;
            }

            // One virtual call saved per prop. `fast` is the same object as `into`; the branch
            // only chooses whether the add can inline.
            if (fast is not null)
            {
                fast.Add(prop);
            }
            else
            {
                into.Add(prop);
            }
        }
    }

    /// <summary>The wake queue: each scheduled track, keyed by the tick to re-derive it at.</summary>
    /// <remarks>
    /// At most one entry per track at any moment: a track is enqueued when it is derived and
    /// dequeued exactly once before being derived again, so the queue cannot accumulate stale
    /// entries — the engine's equivalent invariant is <c>AddToInterpolationList</c> checking
    /// <c>m_InterpolationListEntry</c> before adding.
    /// </remarks>
    private readonly PriorityQueue<ScenePropTrack, double> _wakes = new();

    /// <summary>Tracks whose pose changes continuously right now — <c>g_InterpolationList</c>.</summary>
    private readonly List<ScenePropTrack> _lerping = [];

    private bool _sampleSynced;
    private double _sampledTo;
    private int? _sampledTeam;

    /// <summary>Rebuilds every track's sample from nothing, at one tick.</summary>
    /// <remarks>
    /// The cold path: the first call, any seek backwards, and a recorder team switch. It is the
    /// old per-frame walk, demoted to the cases that genuinely need one.
    /// </remarks>
    private void ResyncSample(
        double tick, IReadOnlySet<int>? interpolate, HashSet<int>? named, int? recorderTeam)
    {
        _wakes.Clear();

        foreach (ScenePropTrack track in _lerping)
        {
            track.Lerping = false;
        }

        _lerping.Clear();

        foreach (ScenePropTrack track in _props)
        {
            DeriveSample(track, tick, interpolate, named, recorderTeam);
        }
    }

    /// <summary>Advances the samples to a later tick, paying only for what changed.</summary>
    /// <remarks>
    /// Two costs and nothing else: every wake whose tick has arrived is re-derived (a boundary
    /// crossed — the engine's update latching), and every track mid-lerp is re-sampled (the
    /// engine's `ProcessInterpolatedList` walk). A parked track is not touched.
    /// </remarks>
    private void AdvanceSample(
        double tick, IReadOnlySet<int>? interpolate, HashSet<int>? named, int? recorderTeam)
    {
        // A re-derived track re-enqueues its NEXT wake, which a long forward jump may also have
        // passed — the loop keeps popping until the head is in the future, so every crossed
        // boundary is processed and `Motion` returning a wake strictly beyond the asked tick is
        // what guarantees termination.
        while (_wakes.TryPeek(out ScenePropTrack? due, out double at) && at <= tick)
        {
            _wakes.Dequeue();

            DeriveSample(due, tick, interpolate, named, recorderTeam);
        }

        foreach (ScenePropTrack track in _lerping)
        {
            // Mid-lerp tracks are blend-sampled by construction: `Motion` only reports a track
            // changing under blended sampling. Hidden is re-read because it is a field of the
            // pose, discrete at the keyframe the lerp is walking away from.
            track.Live = track.At(tick) is { Hidden: false } pose
                ? BuildProp(track, pose, recorderTeam)
                : null;
        }
    }

    /// <summary>Re-derives one track's whole sampling state at a tick.</summary>
    /// <remarks>
    /// **This is the engine's latch, with the decision it takes there** —
    /// <c>OnLatchInterpolatedVariables</c> consults <c>ShouldInterpolate()</c> when an update
    /// arrives (`c_baseentity.cpp:2832`), so whether a track blends or holds is decided at its
    /// wake, from the interpolation set as it stands then, and not revisited per frame. A prop
    /// granted visibility between keyframes therefore joins the lerp at the next keyframe, which
    /// is when the engine would re-latch it.
    /// </remarks>
    private void DeriveSample(
        ScenePropTrack track,
        double tick,
        IReadOnlySet<int>? interpolate,
        HashSet<int>? named,
        int? recorderTeam)
    {
        bool blend = interpolate is null
            || (named is not null
                ? named.Contains(track.EntityIndex)
                : interpolate.Contains(track.EntityIndex));

        (bool changing, double nextWake) = track.Motion(tick, blend);

        ScenePose? sampled = blend ? track.At(tick) : track.Held(tick);

        // A hidden entity is not drawn but is still tracked: it is coming back.
        track.Live = sampled is { Hidden: false } pose
            ? BuildProp(track, pose, recorderTeam)
            : null;

        if (changing != track.Lerping)
        {
            if (changing)
            {
                _lerping.Add(track);
            }
            else
            {
                _lerping.Remove(track);
            }

            track.Lerping = changing;
        }

        if (!double.IsPositiveInfinity(nextWake))
        {
            _wakes.Enqueue(track, nextWake);
        }
    }

    /// <summary>Builds the prop a track serves until something changes it.</summary>
    private static SceneProp BuildProp(ScenePropTrack track, in ScenePose pose, int? recorderTeam) =>
        new(
            // **The pose as sampled, with no player-animation inputs derived from it** (B258).
            // `Moving` was called here for every prop, and it computes `move_x`, `move_y` and
            // `Speed` — which come from `CBasePlayerAnimState::ComputePoseParam_MoveYaw` and exist
            // nowhere outside `base_playeranimstate.cpp` and `multiplayer_animstate.cpp`. A
            // `C_BaseAnimating` prop has no animation state, so the engine derives none of this
            // for one: a resupply locker does not have legs. And it never ran for a player here
            // anyway — player tracks live in `_playerTracks`, and `PlayerProps` carries the values
            // `PlayersAt` computes onto the player's own `SceneProp`.
            track.EntityIndex, track.ModelPath, track.Kind, pose,

            // **From the POSE, because the weapon state is the one of these that changes while an
            // entity lives** (B244). The rest — the parent, the owner, the item, the class — are
            // fixed for a track's lifetime, so reading them off the track is right; reading
            // `m_iState` off it answered with the demo's final tick, and a medic whose medigun was
            // holstered at the end never drew it at all.
            track.AttachedTo, track.AttachmentPoint, track.OwnedBy, pose.WeaponState,
            track.BoneMerged, track.ItemDefinitionIndex, track.ClassName,
            track.OfDisguise,
            OfRecordersTeam: recorderTeam is { } mine && track.TeamNumber == mine,
            Econ: track.Econ,
            ClientSideAnimated: track.ClientSideAnimated);

    /// <summary>Whether a player is on the opposite team from the recorder.</summary>
    /// <param name="recorderTeam">The recording player's team, or null when it is not known.</param>
    /// <param name="team">The player being asked about.</param>
    /// <returns>Whether a disguise is meant to fool us.</returns>
    /// <remarks>
    /// **`C_TFPlayer::IsEnemyPlayer`, `c_tf_player.cpp:5384`**, which switches on the LOCAL player's
    /// team and answers true only for the opposite one:
    ///
    /// <code>
    ///   case TF_TEAM_RED:  return ( GetTeamNumber() == TF_TEAM_BLUE );
    ///   case TF_TEAM_BLUE: return ( GetTeamNumber() == TF_TEAM_RED );
    ///   default: break;
    ///   return false;
    /// </code>
    ///
    /// **The default matters and is kept.** A recorder who is a spectator, unassigned or unknown is
    /// on NEITHER team and the engine answers false — so a SourceTV recording sees every spy
    /// undisguised, which is what a spectator sees in game.
    ///
    /// **The recorder's team is resolved BEFORE the player loop, not from the list being built.**
    /// A first version searched the players added so far, so everybody with an entity index below
    /// the recorder's answered false regardless of team — measured on `cp_fulgur`, entities 1 and 2
    /// reported friendly while on the opposite side. That is an ordering bug of exactly the kind
    /// this project keeps shipping, and it was caught by reading the output rather than by the
    /// tests, which had no ordering to get wrong.
    ///
    /// **The coaching branch is not implemented**: the engine substitutes the student's team when
    /// `m_hStudent` is set and `m_bIsCoaching`. Neither field is read here, so a coached recording
    /// reports the coach's own team.
    /// </remarks>
    private static bool IsEnemyOfRecorder(int? recorderTeam, int? team) =>
        (recorderTeam, team) switch
        {
            (SceneTeams.Red, SceneTeams.Blu) => true,
            (SceneTeams.Blu, SceneTeams.Red) => true,
            _ => false,
        };

    /// <summary>Where everyone was at a tick, or the most recent moment before it.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <returns>The players, empty before the first recorded frame.</returns>
    /// <remarks>
    /// **The most recent frame rather than an exact match**, because positions arrive with packets
    /// and the server sends no packet on most ticks. Requiring an exact tick would blink the map
    /// empty between updates.
    /// </remarks>
    public IReadOnlyList<ScenePlayer> PlayersAt(int tick) =>
        FrameAt(tick)?.Players ?? [];


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

    /// <summary>The round the game rules were in, or <c>null</c> when the demo does not say.</summary>
    /// <param name="tick">The moment being shown.</param>
    /// <returns><c>m_iRoundState</c>; <c>GR_STATE_TEAM_WIN</c> is 5.</returns>
    /// <remarks>
    /// **Null is "the demo did not say", not a state.** Era demos predate the game rules proxy this
    /// reads, and a consumer that treated absent as any particular state would apply that state's
    /// behaviour to every one of them — which for the spawn walls means blanking all of them for a
    /// whole recording (`RespawnRoomVisibility`).
    /// </remarks>
    public int? RoundStateAt(double tick) => FrameAt((int)Math.Floor(tick))?.RoundState;

    /// <summary>The most recent frame at or before a tick.</summary>
    /// <remarks>
    /// **The server does not send a packet every tick**, so this answers with the last one rather
    /// than requiring an exact match — the same rule <see cref="PlayersAt(int)"/> has always used,
    /// lifted out so the round state and the props answer from the same frame the players do.
    /// </remarks>
    private TimelineFrame? FrameAt(int tick)
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

        return found >= 0 ? _frames[found] : null;
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
