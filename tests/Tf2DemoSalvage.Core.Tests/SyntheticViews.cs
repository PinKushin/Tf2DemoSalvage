using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A point-of-view demo made of exactly the commands <c>CDemoPlayer</c>'s view half reads: packets carrying a
/// <c>democmdinfo_t</c>, and bare <c>dem_synctick</c> and <c>dem_stop</c> (B56).
/// </summary>
/// <remarks>
/// **The prologue is written field by field here, never by the code under test** — a fixture built by the reader
/// agrees with it by construction (<c>docs/memory/fixtures-are-the-weak-point.md</c>). Ends with exactly the commands
/// given: no <c>dem_stop</c> is appended, because where the stop sits is what <c>ParseAheadForInterval</c> answers.
/// </remarks>
internal static class SyntheticViews
{
    /// <summary>The SDK's <c>FDEMO_USE_ORIGIN2</c>.</summary>
    public const int UseOrigin2 = 1 << 0;

    /// <summary>The SDK's <c>FDEMO_USE_ANGLES2</c>.</summary>
    public const int UseAngles2 = 1 << 1;

    /// <summary>The SDK's <c>FDEMO_NOINTERP</c>.</summary>
    public const int NoInterpolation = 1 << 2;

    /// <summary>TF2's 66.67 tick.</summary>
    public const float Interval = 0.015f;

    /// <summary>A packet at a tick carrying a view.</summary>
    public static DemoCommand Packet(
        int tick,
        (float X, float Y, float Z) origin,
        (float Pitch, float Yaw, float Roll) angles = default,
        (float Pitch, float Yaw, float Roll)? localAngles = null,
        int flags = 0,
        (float X, float Y, float Z) origin2 = default,
        (float Pitch, float Yaw, float Roll) angles2 = default,
        (float Pitch, float Yaw, float Roll) localAngles2 = default)
    {
        byte[] prologue = new byte[RecordedView.SizeBytes + 8];
        (float Pitch, float Yaw, float Roll) local = localAngles ?? angles;

        BitConverter.GetBytes(flags).CopyTo(prologue, 0);
        Write(prologue, 4, origin.X, origin.Y, origin.Z);
        Write(prologue, 16, angles.Pitch, angles.Yaw, angles.Roll);
        Write(prologue, 28, local.Pitch, local.Yaw, local.Roll);
        Write(prologue, 40, origin2.X, origin2.Y, origin2.Z);
        Write(prologue, 52, angles2.Pitch, angles2.Yaw, angles2.Roll);
        Write(prologue, 64, localAngles2.Pitch, localAngles2.Yaw, localAngles2.Roll);

        return new DemoCommand(DemoCommandType.Packet, tick, ReadOnlyMemory<byte>.Empty, prologue);
    }

    /// <summary>A <c>dem_synctick</c>.</summary>
    public static DemoCommand SyncTick(int tick) => new(DemoCommandType.SyncTick, tick, ReadOnlyMemory<byte>.Empty);

    /// <summary>A <c>dem_stop</c>.</summary>
    public static DemoCommand Stop(int tick) => new(DemoCommandType.Stop, tick, ReadOnlyMemory<byte>.Empty);

    /// <summary>One command per tick from <paramref name="first"/> to <paramref name="last"/>.</summary>
    public static IEnumerable<DemoCommand> Run(int first, int last, Func<int, DemoCommand> packet)
    {
        for (int tick = first; tick <= last; tick++)
        {
            yield return packet(tick);
        }
    }

    /// <summary>The demo: <c>svc_ServerInfo</c> at tick 0, the schema, then exactly <paramref name="commands"/>.</summary>
    /// <param name="maxPlayers"><c>cl.m_nMaxClients</c>; 1 is a single-player listen server.</param>
    /// <param name="commands">Everything after the schema, in stream order.</param>
    public static byte[] Demo(byte maxPlayers, IEnumerable<DemoCommand> commands)
    {
        ServerInfoMessage info = new(
            NetworkProtocol: SyntheticDemo.DefaultProtocol,
            ServerCount: 1,
            IsSourceTv: false,
            IsDedicated: true,
            MapCrc: 0,
            MaxClasses: 1,
            MapHash: new byte[16],
            PlayerSlot: 0,
            MaxPlayers: maxPlayers,
            IntervalPerTick: Interval,
            Platform: 'w',
            GameDirectory: "tf",
            Map: "cp_process_final",
            Skybox: "sky_tf2_04",
            ServerName: "synthetic",
            IsReplay: false);

        List<DemoCommand> all =
        [
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 0, info),
            SyntheticDemo.DataTables(SyntheticPlayer.Schema()),
            .. commands,
        ];

        return DemoWriter.Write(SyntheticDemo.Header(), all);
    }

    /// <summary>A demo on a 24-player server.</summary>
    public static byte[] Demo(params DemoCommand[] commands) => Demo(24, commands);

    private static void Write(byte[] into, int at, float first, float second, float third)
    {
        BitConverter.GetBytes(first).CopyTo(into, at);
        BitConverter.GetBytes(second).CopyTo(into, at + 4);
        BitConverter.GetBytes(third).CopyTo(into, at + 8);
    }
}
