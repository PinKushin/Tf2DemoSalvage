using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>svc_ServerInfo</c> — the first thing a joining client is told, and the first message in
/// a demo's signon stream.
/// </summary>
/// <param name="NetworkProtocol">Network protocol version. Should match the demo header's.</param>
/// <param name="ServerCount">Server's spawn counter, incremented on each map change.</param>
/// <param name="IsSourceTv">Whether the connection is a SourceTV relay.</param>
/// <param name="IsDedicated">Whether the server is dedicated rather than a listen server.</param>
/// <param name="MapCrc">Map checksum, used by the client to detect a mismatched map.</param>
/// <param name="MaxClasses">
/// Number of networked server classes. Load-bearing later: the entity decoder reads a class id
/// whose bit width is derived from this.
/// </param>
/// <param name="MapHash">16-byte map hash. Present from protocol 18 onward.</param>
/// <param name="PlayerSlot">Slot of the receiving client.</param>
/// <param name="MaxPlayers">Server's player capacity.</param>
/// <param name="IntervalPerTick">Seconds per tick. TF2's 66.67 tick rate is 0.015 here.</param>
/// <param name="Platform">Server platform: <c>l</c> for Linux, <c>w</c> for Windows.</param>
/// <param name="GameDirectory">Game directory, <c>tf</c> for Team Fortress 2.</param>
/// <param name="Map">Map name. Should match the demo header's.</param>
/// <param name="Skybox">Skybox material name.</param>
/// <param name="ServerName">The server's <c>hostname</c> cvar — operator-chosen free text.</param>
/// <param name="IsReplay">Whether this is a Replay recording. Present from protocol 16 onward.</param>
/// <remarks>
/// Nothing in the signon stream is length-prefixed, so this message gates everything behind it
/// — the string tables, the class list, the game event definitions, and the entity schema. Its
/// field widths have to be exactly right or none of that is reachable.
///
/// Two fields make it unusually easy to verify: <paramref name="NetworkProtocol"/> and
/// <paramref name="Map"/> both duplicate values in the demo's fixed header, which is written
/// through an entirely different path. Agreement is strong evidence the layout is correct.
/// </remarks>
public sealed record ServerInfoMessage(
    ushort NetworkProtocol,
    uint ServerCount,
    bool IsSourceTv,
    bool IsDedicated,
    uint MapCrc,
    ushort MaxClasses,
    IReadOnlyList<byte> MapHash,
    byte PlayerSlot,
    byte MaxPlayers,
    float IntervalPerTick,
    char Platform,
    string GameDirectory,
    string Map,
    string Skybox,
    string ServerName,
    bool IsReplay) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.ServerInfo;

    /// <summary>
    /// Ticks per second, derived from <see cref="IntervalPerTick"/>. Zero when the interval is
    /// zero, rather than infinity — a malformed demo should not produce a value that poisons
    /// every calculation downstream.
    /// </summary>
    public float TickRate => IntervalPerTick > 0f ? 1f / IntervalPerTick : 0f;
}
