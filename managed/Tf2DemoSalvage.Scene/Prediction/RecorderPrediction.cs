using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary>
/// <c>CPrediction</c> for a point-of-view demo's recorder: each frame, his last networked state with every usercmd read
/// since re-run on top (D205).
/// </summary>
/// <remarks>
/// <c>CL_RunPrediction</c> runs during demo playback (engine x64 <c>0x180092710</c>, D205), and <c>_Update</c>
/// (<c>prediction.cpp:1742</c>) restores the last received state, then <c>PerformPrediction</c> (<c>:1570</c>) runs the
/// commands from <c>incoming_acknowledged + 1</c> to the last one made. So it holds after a seek or a rewind exactly as
/// after a packet: nothing is carried between frames.
///
/// **The world it moves through is <see cref="MovementWorld"/>** at the packet's tick: the map, brush entities by TF's
/// filter, buildings, and enemy players, who stand where they were then.
/// </remarks>
public sealed class RecorderPrediction
{
    private readonly DemoTimeline _timeline;
    private readonly Func<MapLevel?> _world;
    private readonly MovementConVars _convars;

    private MapLevel? _brushLevel;
    private IReadOnlyList<(ScenePropTrack Track, int HeadNode)> _brushTracks = [];
    private double _askedTick = double.NaN;
    private (float X, float Y, float Z)? _answer;

    /// <summary>Prediction over a decoded demo and the map its world is read from.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="world">The loaded map, asked per frame because it is read on its own schedule; null for none.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RecorderPrediction(DemoTimeline timeline, Func<MapLevel?> world)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _convars = MovementConVars.From(timeline.ServerConVars);
    }

    /// <summary>The item schema's attribute hooks, asked per prediction because the schema loads on its own schedule; null for none.</summary>
    public Func<AttributeHooks?>? Hooks { get; init; }

    /// <summary>The surface data a ground trace stands on (<see cref="TfGameMovement.GroundSurface"/>); null for none.</summary>
    public Func<BspTrace, VphysicsSurface?>? GroundSurface { get; init; }

    /// <summary>What the movement asks a player's items, as of his networked state.</summary>
    /// <param name="recorder">The player.</param>
    /// <param name="hooks">The attribute hooks; null hooks nothing.</param>
    /// <returns>The hooks on him and his active weapon, and that weapon's <c>OwnerCanJump</c>.</returns>
    /// <remarks>
    /// <c>OwnerCanJump</c> is true but for <c>CTFCompoundBow</c>, false while <c>GetInternalChargeBeginTime()</c> is nonzero
    /// (<c>tf_weapon_compound_bow.cpp:657-660</c>, <c>tf_weaponbase.h:339</c>) — the bow's networked <c>m_flChargeBeginTime</c>.
    /// </remarks>
    public static MovementItems ItemsOf(ScenePlayer recorder, AttributeHooks? hooks)
    {
        SceneItem? active = null;

        foreach (SceneItem item in recorder.Items ?? [])
        {
            if (item.EntityIndex == recorder.ActiveWeapon)
            {
                active = item;
            }
        }

        Func<string, float, float> onPlayer = hooks is null
            ? MovementItems.None.OnPlayer
            : (name, value) => hooks.OnPlayer(recorder, name, value);
        Func<string, float, float>? onWeapon = hooks is null || active is null
            ? null
            : (name, value) => hooks.OnWeapon(recorder, active, name, value);

        return new MovementItems(onPlayer, onWeapon, active is not { ClassName: "CTFCompoundBow", ChargeBeginTime: not 0f });
    }

    /// <summary>The recorder's predicted <c>m_vecVelocity</c> at a moment of playback.</summary>
    /// <param name="tick">The playback position.</param>
    /// <returns>
    /// The velocity, or null when prediction has nothing to add or cannot run: every command read is already in the
    /// last packet, no map, the recorder not alive, or a move this port declines.
    /// </returns>
    public (float X, float Y, float Z)? VelocityAt(double tick)
    {
        if (BitConverter.DoubleToInt64Bits(tick) == BitConverter.DoubleToInt64Bits(_askedTick))
        {
            return _answer;
        }

        _askedTick = tick;
        _answer = Predict((int)Math.Floor(tick));

        return _answer;
    }

    private (float X, float Y, float Z)? Predict(int tick)
    {
        return LastPacket(_timeline.PacketAcknowledgements, tick) is { } packet ? PredictFrom(packet, tick) : null;
    }

    /// <summary>The velocity prediction reaches from one packet's state, with every command read by a tick re-run on it.</summary>
    /// <param name="packet">The packet's tick and acknowledgement.</param>
    /// <param name="tick">The tick whose commands are read; later than the packet's for there to be any.</param>
    /// <returns>The velocity, or null as for <see cref="VelocityAt"/>.</returns>
    /// <remarks>Public so the next packet can be compared with it: its state includes exactly these commands.</remarks>
    public (float X, float Y, float Z)? PredictFrom((int Tick, int Acknowledged) packet, int tick)
    {
        if (_timeline.RecorderEntityIndex is not { } recorderIndex)
        {
            return null;
        }

        IReadOnlyList<RecordedUserCommand> pending = Pending(_timeline.UserCommands, packet.Acknowledged, tick);

        if (pending.Count == 0 || _world() is not { } level)
        {
            return null;
        }

        IReadOnlyList<ScenePlayer> players = _timeline.PlayersAt(packet.Tick);
        ScenePlayer? found = null;

        foreach (ScenePlayer each in players)
        {
            if (each.EntityIndex == recorderIndex)
            {
                found = each;
            }
        }

        if (found is not { IsAlive: true, MaxSpeed: not null } recorder)
        {
            return null;
        }

        if (!ReferenceEquals(_brushLevel, level))
        {
            _brushTracks = level.BrushModels is { } models ? MovementWorld.BrushTracks(_timeline.Props, models) : [];
            _brushLevel = level;
        }

        MovementWorld world = MovementWorld.At(_timeline, _brushTracks, level, packet.Tick, recorder);
        TfGameMovement movement = new(world.Trace, _convars, _timeline.MaxClients)
        {
            Items = ItemsOf(recorder, Hooks?.Invoke()),
            GroundSurface = GroundSurface,
        };
        PredictedPlayer player = Restore(recorder, _timeline.IntervalPerTick, packet.Tick);

        // m_isPassingThroughEnemies is the movement object's own and is not networked; the client carries it from the
        // last CheckStuck. *Interpolated:* the restore takes what that check would conclude here — inside an enemy or not.
        Vector3 maxs = new(24f, 24f, player.Ducked ? 62f : 82f);

        player.PassingThroughEnemies =
            world.Trace(player.Origin, player.Origin, new Vector3(-24f, -24f, 0f), maxs, MovementWorld.MaskPlayerSolid | EnemyContents(player.Team))
            is { StartSolid: true, BrushEntity: > 0 and var entity } && entity <= _timeline.MaxClients;

        bool first = true;

        foreach (RecordedUserCommand command in pending)
        {
            if (!movement.ProcessMovement(ref player, command.Command, _timeline.IntervalPerTick, first, command.Sequence))
            {
                return null;
            }

            first = false;
        }

        return (player.Velocity.X, player.Velocity.Y, player.Velocity.Z);
    }

    private static int EnemyContents(int? team) => team switch
    {
        2 => MovementWorld.ContentsBlueTeam,
        3 => MovementWorld.ContentsRedTeam,
        _ => 0,
    };

    /// <summary>
    /// The recorder's last networked state as <c>CPrediction::_Update</c> restores it: position, velocity, flags, and
    /// <c>DT_Local</c>'s duck and jump state where the demo carries it.
    /// </summary>
    /// <param name="recorder">The recorder at the packet.</param>
    /// <param name="intervalPerTick">The tick interval.</param>
    /// <param name="packetTick">The packet's tick, <c>curtime</c>'s fallback when <c>m_nTickBase</c> is unsent.</param>
    /// <returns>The player prediction starts from.</returns>
    internal static PredictedPlayer Restore(ScenePlayer recorder, float intervalPerTick, int packetTick)
    {
        int flags = recorder.Flags ?? 0;
        (float X, float Y, float Z) velocity = recorder.Velocity ?? default;
        bool flDucking = (flags & DuckingFlag) != 0;

        PredictedPlayer player = new()
        {
            Origin = new Vector3(recorder.X, recorder.Y, recorder.Z),
            Velocity = new Vector3(velocity.X, velocity.Y, velocity.Z),
            OnGround = (flags & OnGroundFlag) != 0,
            Ducked = flDucking,
            FlDucking = flDucking,
            MaxSpeed = recorder.MaxSpeed ?? 0f,
            PlayerClass = recorder.PlayerClass ?? 0,
            Conditions = recorder.Conditions,
            PlayerState = recorder.PlayerState,
            WaterLevel = recorder.WaterLevel ?? 0,
            CurTime = packetTick * intervalPerTick,
            Team = recorder.Team,
            EntityIndex = recorder.EntityIndex,
            Gravity = recorder.Gravity,

            // m_pSurfaceData is client state from the last prediction, not networked. *Interpolated:* none, so the first
            // command's CheckParameters reads a speed factor of 1 until CategorizePosition finds the ground.
        };

        if (recorder.Movement is not { } local)
        {
            return player;
        }

        return player with
        {
            Ducked = local.Ducked,
            Ducking = local.Ducking,
            InDuckJump = local.InDuckJump,
            DuckTime = local.DuckTime,
            DuckJumpTime = local.DuckJumpTime,
            JumpTime = local.JumpTime,
            FallVelocity = local.FallVelocity,
            AllowAutoMovement = local.AllowAutoMovement,
            BaseVelocity = new Vector3(local.BaseVelocity.X, local.BaseVelocity.Y, local.BaseVelocity.Z),
            AirDash = local.AirDash,
            AirDucked = local.AirDucked,
            DuckTimer = local.DuckTimer,
            CurTime = local.TickBase is { } tickBase ? tickBase * intervalPerTick : player.CurTime,
        };
    }

    /// <summary><c>FL_ONGROUND</c> and <c>FL_DUCKING</c> (<c>const.h:148-149</c>), the two bits every era agrees on.</summary>
    private const int OnGroundFlag = 1 << 0;
    private const int DuckingFlag = 1 << 1;

    /// <summary>The commands <c>PerformPrediction</c> runs: past the acknowledgement, read by now, in order.</summary>
    /// <param name="commands">Every usercmd, in stream order.</param>
    /// <param name="acknowledged">The last command the packet's state includes.</param>
    /// <param name="tick">The playback tick; a usercmd is read when playback reaches its tick.</param>
    /// <returns>The commands to re-run.</returns>
    public static IReadOnlyList<RecordedUserCommand> Pending(
        IReadOnlyList<RecordedUserCommand> commands, int acknowledged, int tick)
    {
        ArgumentNullException.ThrowIfNull(commands);

        List<RecordedUserCommand> pending = [];

        // The first command read at or after the packet's sequence, found by bisection; usercmds rise in both.
        int low = 0;
        int high = commands.Count;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (commands[middle].Sequence <= acknowledged)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int index = low; index < commands.Count && commands[index].Tick <= tick; index++)
        {
            pending.Add(commands[index]);
        }

        return pending;
    }

    private static (int Tick, int Acknowledged)? LastPacket(IReadOnlyList<(int Tick, int Acknowledged)> packets, int tick)
    {
        int low = 0;
        int high = packets.Count;

        while (low < high)
        {
            int middle = (low + high) / 2;

            if (packets[middle].Tick <= tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low == 0 ? null : packets[low - 1];
    }

}
