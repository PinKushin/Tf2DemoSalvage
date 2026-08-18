using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
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
/// <param name="Drawn">
/// Whether the engine would draw this player's model, which is <c>EF_NODRAW</c> rather than
/// anything about life state. TF2 turns the player off on death — <c>AddEffects( EF_NODRAW |
/// EF_NOSHADOW )</c> at the end of <c>CreateRagdollEntity</c>, <c>tf_player.cpp:15637</c> — and
/// spawns a separate <c>CTFRagdoll</c> to be the corpse. A dead player stays in this list as data
/// for the scoreboard and the kill feed with this false.
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
    int? ActiveWeapon = null,
    string? WeaponClass = null)
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

    private DemoTimeline(
        List<TimelineFrame> frames,
        List<ScenePropTrack>? props = null,
        List<ScenePropTrack>? playerTracks = null)
    {
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

    /// <summary>Every player, with the pose the interpolator works from.</summary>
    /// <remarks>
    /// **Separate from <see cref="Props"/> because these carry no model.** A player's model is
    /// resolved from the installed game rather than from the demo — see
    /// <c>PlayerClassModels</c> — so a consumer walking <see cref="Props"/> to draw models would
    /// find entries it could only report as missing assets.
    /// </remarks>
    public IReadOnlyList<ScenePropTrack> PlayerTracks => _playerTracks;

    /// <summary>Every recorded moment, in tick order.</summary>
    public IReadOnlyList<TimelineFrame> Frames => _frames;

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

        DemoCommand? tables = commands.FirstOrDefault(
            command => command.Type == DemoCommandType.DataTables);

        if (tables is not { } dataTables)
        {
            return new DemoTimeline([]);
        }

        DemoSchema schema = SendTableParser.Parse(
            dataTables.Payload.Span, (ushort)header.NetworkProtocol);

        EntityDecoder decoder = new(
            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        EntityStateTable entities = new();

        // **Class names come from dem_datatables, not from svc_ClassInfo.** TF2 sets the
        // "create on client" flag and sends no names, so a reader waiting for that message names
        // nothing and finds no players while decoding every entity correctly.
        foreach (ServerClass serverClass in schema.ServerClasses)
        {
            entities.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        List<TimelineFrame> frames = [];

        float interval = 0f;

        ModelPrecache precache = new();
        int protocol = header.NetworkProtocol;

        // Live tracks by slot, plus every track ever started. A slot is reused when its occupant
        // is destroyed, so the two are not the same list - keeping only the live ones would lose
        // every rocket the moment the next one took its index.
        Dictionary<int, ScenePropTrack> tracks = [];
        List<ScenePropTrack> props = [];
        List<ScenePropTrack> playerTracks = [];

        foreach (DemoCommand command in commands)
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
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
                    Yaw: facing,

                    // Null on a POV demo for everyone but the recorder, because the send prop is in
                    // DT_LocalPlayerExclusive; a SourceTV recording carries it for every player.
                    Flags: player.Flags(),

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
                    ActiveWeapon: player.ActiveWeapon(),
                    WeaponClass: player.ActiveWeapon() is { } held &&
                        entities.TryGet(held, out EntityState? weapon)
                            ? weapon.ClassName
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

        return new DemoTimeline(frames, props, playerTracks) { IntervalPerTick = interval };
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
    private static string? ModelFor(EntityState state, ModelPrecache precache, int protocol)
    {
        if (PlayerClass.Equals(state.ClassName, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // The engine's own compatibility shim: protocol 20 and below packed indices below -1.
        // See ModelPrecache.Unpack and docs/findings/19-model-indices.md.
        return state.ModelIndex() is { } rawIndex
            ? precache.Path(ModelPrecache.Unpack(rawIndex, protocol))
            : null;
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

        if (!entities.TryGet(entity.EntityIndex, out EntityState? state))
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
        int? attachedTo = null;
        (float X, float Y, float Z) origin;

        if (state.Origin() is { } placed)
        {
            origin = placed;
        }
        else if (state.Attachment() is { } owner)
        {
            attachedTo = owner;
            origin = (0f, 0f, 0f);
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
                    track.AttachedTo));
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
        (float moveX, float moveY) = MoveParameters(track, tick, pose.Yaw);

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

            (float moveX, float moveY) = MoveParameters(track, tick, pose.Yaw);

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
