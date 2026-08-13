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
/// <remarks>
/// **Not everything here is playing.** A spectator and a SourceTV camera are <c>CTFPlayer</c>
/// entities with real positions that fly around the map, and drawing them puts dots where nobody
/// is standing. The team number separates them, and it is the engine's own:
/// <c>TEAM_UNASSIGNED</c> is 0, <c>TEAM_SPECTATOR</c> is 1, and TF2's own
/// <c>TF_TEAM_RED = LAST_SHARED_TEAM + 1</c> makes RED 2 and BLU 3.
/// </remarks>
public readonly record struct ScenePlayer(
    int EntityIndex, float X, float Y, float Z, int? Team, int? Health, int? PlayerClass)
{
    /// <summary>Whether this is someone actually playing, rather than watching.</summary>
    /// <remarks>
    /// **The distinction a map view has to make.** Team 0 is unassigned and team 1 is spectator;
    /// only 2 and 3 are playing. A viewer that draws everything shows the SourceTV camera as a
    /// player, and it moves convincingly - it follows the action, because that is its job.
    /// </remarks>
    public bool IsPlaying => Team is SceneTeams.Red or SceneTeams.Blu;
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

                players.Add(new ScenePlayer(
                    player.EntityIndex,
                    origin.X,
                    origin.Y,
                    origin.Z,
                    resource?.Integer($"m_iTeam.{slot}") ?? First(player, TeamProperties),
                    resource?.Integer($"m_iHealth.{slot}") ?? First(player, HealthProperties),
                    resource?.Integer($"m_iPlayerClass.{slot}")));
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

        if (!entities.TryGet(entity.EntityIndex, out EntityState? state) ||
            state.Origin() is not { } origin)
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

        // **A slot is reused, so the model is what identifies the occupant.** A rocket that
        // explodes frees its index for the next one, and appending that one's positions to the
        // old track would draw a rocket flying between two unrelated places.
        if (tracks.TryGetValue(entity.EntityIndex, out ScenePropTrack? track) &&
            !string.Equals(track.ModelPath, model, StringComparison.Ordinal))
        {
            track.End(tick);
            tracks.Remove(entity.EntityIndex);
            track = null;
        }

        if (track is null)
        {
            track = new ScenePropTrack(entity.EntityIndex, model);
            tracks[entity.EntityIndex] = track;

            // Player tracks are kept apart from Props. They carry poses and no model, so a
            // consumer walking Props to draw models would find one it cannot draw and could only
            // report as a missing asset - which is exactly the false alarm this split avoids.
            (model.Length == 0 ? players : props).Add(track);
        }

        (float pitch, float yaw, float roll) = state.Angles() ?? (0f, 0f, 0f);

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

                // Scale and sequence default rather than zero: an absent scale is authored size,
                // and sequence -1 is "does not animate" where zero is a real animation.
                Scale = state.ModelScale() ?? 1f,
                Sequence = state.AnimationSequence() ?? -1,
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
                into.Add(new SceneProp(track.EntityIndex, track.ModelPath, track.Kind, pose));
            }
        }
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
            into.Add(
                _trackByEntity.TryGetValue(player.EntityIndex, out ScenePropTrack? track) &&
                track.At(tick) is { } pose
                    ? player with { X = pose.X, Y = pose.Y, Z = pose.Z }
                    : player);
        }
    }

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
