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
/// **Player boxes are in the world it moves through**: <c>CTFGameMovement::PlayerSolidMask</c>
/// (<c>tf_gamemovement.cpp:259</c>) adds the other team's contents, so enemies block him and team-mates do not. They
/// stand where they were at the packet's tick. Buildings (<c>CTraceFilterObject</c>) are not boxes here, filed in B450.
/// </remarks>
public sealed class RecorderPrediction
{
    private readonly DemoTimeline _timeline;
    private readonly Func<MapLevel?> _world;
    private readonly MovementConVars _convars;

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

        if (found is not { IsAlive: true, MaxSpeed: { } maxSpeed } recorder)
        {
            return null;
        }

        int flags = recorder.Flags ?? 0;
        (float X, float Y, float Z) velocity = recorder.Velocity ?? default;

        PredictedPlayer player = new()
        {
            Origin = new Vector3(recorder.X, recorder.Y, recorder.Z),
            Velocity = new Vector3(velocity.X, velocity.Y, velocity.Z),
            OnGround = (flags & OnGroundFlag) != 0,
            Ducked = (flags & DuckingFlag) != 0,
            FlDucking = (flags & DuckingFlag) != 0,
            MaxSpeed = maxSpeed,
            PlayerClass = recorder.PlayerClass ?? 0,
            Conditions = recorder.Conditions,
            PlayerState = recorder.PlayerState,
            WaterLevel = recorder.WaterLevel ?? 0,
            CurTime = packet.Tick * _timeline.IntervalPerTick,
            Gravity = recorder.Gravity,

            // m_pSurfaceData is client state from the last prediction, not networked. *Interpolated:* none, so the first
            // command's CheckParameters reads a speed factor of 1 until CategorizePosition finds the ground.
        };

        List<(Vector3 Min, Vector3 Max)> enemies = Enemies(players, recorder);
        TfGameMovement movement = new(
            (start, end, mins, maxs, mask) => Trace(level, enemies, start, end, mins, maxs, mask),
            _convars)
        {
            Items = ItemsOf(recorder, Hooks?.Invoke()),
            GroundSurface = GroundSurface,
        };

        bool first = true;

        foreach (RecordedUserCommand command in pending)
        {
            if (!movement.ProcessMovement(ref player, command.Command, _timeline.IntervalPerTick, first))
            {
                return null;
            }

            first = false;
        }

        return (player.Velocity.X, player.Velocity.Y, player.Velocity.Z);
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

    private static List<(Vector3 Min, Vector3 Max)> Enemies(IReadOnlyList<ScenePlayer> players, ScenePlayer recorder)
    {
        List<(Vector3, Vector3)> boxes = [];

        foreach (ScenePlayer other in players)
        {
            if (other.EntityIndex == recorder.EntityIndex || !other.IsAlive || other.Team is null || other.Team == recorder.Team)
            {
                continue;
            }

            float top = ((other.Flags ?? 0) & DuckingFlag) != 0 ? 62f : 82f;
            Vector3 origin = new(other.X, other.Y, other.Z);

            boxes.Add((origin + new Vector3(-24f, -24f, 0f), origin + new Vector3(24f, 24f, top)));
        }

        return boxes;
    }

    private static BspTrace? Trace(
        MapLevel level,
        List<(Vector3 Min, Vector3 Max)> enemies,
        Vector3 start,
        Vector3 end,
        Vector3 mins,
        Vector3 maxs,
        int mask)
    {
        if (level.TraceHull(
                (start.X, start.Y, start.Z), (end.X, end.Y, end.Z), (mins.X, mins.Y, mins.Z), (maxs.X, maxs.Y, maxs.Z), mask, [])
            is not { } nearest)
        {
            return null;
        }

        foreach ((Vector3 min, Vector3 max) in enemies)
        {
            if (SweepBox(start, end, min - maxs, max - mins) is { } hit && hit.Fraction < nearest.Fraction)
            {
                nearest = hit;
            }
        }

        return nearest;
    }

    /// <summary>A point swept against a box already grown by the player's: the slab test, stopping <c>DIST_EPSILON</c> short.</summary>
    internal static BspTrace? SweepBox(Vector3 start, Vector3 end, Vector3 min, Vector3 max)
    {
        Vector3 delta = end - start;
        float enter = -1f;
        float leave = 1f;
        Vector3 normal = Vector3.Zero;
        bool startsOutside = false;

        for (int axis = 0; axis < 3; axis++)
        {
            float s = start[axis];
            float d = delta[axis];

            // The two planes of this slab, as CM_ClipBoxToBrush sees a brush's sides: distance outside each.
            foreach ((float outside, float along, float sign) in new[] { (s - max[axis], d, 1f), (min[axis] - s, -d, -1f) })
            {
                float startDistance = outside;
                float endDistance = outside + along;

                if (startDistance > 0f)
                {
                    startsOutside = true;
                }

                if (startDistance > 0f && endDistance >= 0f)
                {
                    return null;
                }

                if (startDistance <= 0f && endDistance <= 0f)
                {
                    continue;
                }

                if (startDistance > endDistance)
                {
                    float fraction = (startDistance - 0.03125f) / (startDistance - endDistance);

                    if (fraction > enter)
                    {
                        enter = fraction;
                        normal = Vector3.Zero;
                        normal[axis] = sign;
                    }
                }
                else
                {
                    leave = MathF.Min(leave, (startDistance + 0.03125f) / (startDistance - endDistance));
                }
            }
        }

        // Already inside an enemy: CTFGameMovement::CheckStuck sets m_isPassingThroughEnemies (tf_gamemovement.cpp:1404)
        // and he stops colliding with them until clear — so a box he starts in does not stop him.
        if (!startsOutside)
        {
            return null;
        }

        return enter < leave && enter >= 0f
            ? new BspTrace(enter, -1, (normal.X, normal.Y, normal.Z), false)
            : null;
    }
}
