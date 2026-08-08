using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Network message types carried inside a <c>dem_packet</c> payload, at network protocol 24.
/// </summary>
/// <remarks>
/// The type field is <see cref="NetMessage.TypeBits"/> bits wide, matching Source's
/// <c>NETMSG_TYPE_BITS</c>. Values come from prior art rather than Valve: <c>protocol.h</c> and
/// <c>netmessages.h</c> are not shipped in source-sdk-2013, so there is no authoritative header
/// to read them from (see <c>docs/RISKS.md</c> B3).
///
/// Ids 1, 9, 16, 20 and 22 are unused at this protocol and are deliberately absent — a stream
/// producing one is malformed, and the decoder should say so rather than guess.
///
/// **These messages are not length-prefixed.** There is no skip: the next message starts
/// wherever the previous one's body ended, so the stream can only be walked by decoding every
/// message well enough to know its width. That makes support strictly incremental — until a
/// type is implemented, everything after its first occurrence in a packet is unreachable.
/// </remarks>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte matches the on-disk field, which is 6 bits wide.")]
public enum NetMessageType : byte
{
    /// <summary>No-op padding, <c>net_NOP</c>. Used to pad a packet to a byte boundary.</summary>
    Empty = 0,

    /// <summary>File transfer request/deny, <c>svc_File</c>.</summary>
    File = 2,

    /// <summary>Server tick and frame timing, <c>net_Tick</c>.</summary>
    NetTick = 3,

    /// <summary>A console command string sent to the client, <c>net_StringCmd</c>.</summary>
    StringCmd = 4,

    /// <summary>Console variable updates, <c>net_SetConVar</c>.</summary>
    SetConVar = 5,

    /// <summary>Connection handshake progress, <c>net_SignonState</c>.</summary>
    SignOnState = 6,

    /// <summary>Text printed to the client console, <c>svc_Print</c>.</summary>
    Print = 7,

    /// <summary>Map, tick rate, player count and more, <c>svc_ServerInfo</c>.</summary>
    ServerInfo = 8,

    /// <summary>Server class list — needed before entities can be decoded, <c>svc_ClassInfo</c>.</summary>
    ClassInfo = 10,

    /// <summary>Pause state, <c>svc_SetPause</c>.</summary>
    SetPause = 11,

    /// <summary>Creates a string table, <c>svc_CreateStringTable</c>.</summary>
    CreateStringTable = 12,

    /// <summary>Updates an existing string table, <c>svc_UpdateStringTable</c>.</summary>
    UpdateStringTable = 13,

    /// <summary>Voice codec setup, <c>svc_VoiceInit</c>.</summary>
    VoiceInit = 14,

    /// <summary>Voice payload, <c>svc_VoiceData</c>.</summary>
    VoiceData = 15,

    /// <summary>Sound events, <c>svc_Sounds</c>.</summary>
    Sounds = 17,

    /// <summary>Forces the client's view to an entity, <c>svc_SetView</c>.</summary>
    SetView = 18,

    /// <summary>Forces the client's view angles, <c>svc_FixAngle</c>.</summary>
    FixAngle = 19,

    /// <summary>A decal placed on world geometry, <c>svc_BSPDecal</c>.</summary>
    BspDecal = 21,

    /// <summary>
    /// Game-specific message, <c>svc_UserMessage</c>. Chat arrives this way. Not
    /// self-describing — see <c>docs/RISKS.md</c> B1.
    /// </summary>
    UserMessage = 23,

    /// <summary>A message addressed to one entity, <c>svc_EntityMessage</c>.</summary>
    EntityMessage = 24,

    /// <summary>A fired game event, decoded against <see cref="GameEventList"/>.</summary>
    GameEvent = 25,

    /// <summary>Entity delta snapshot, <c>svc_PacketEntities</c>. The heart of the format.</summary>
    PacketEntities = 26,

    /// <summary>Short-lived effect entities, <c>svc_TempEntities</c>.</summary>
    TempEntities = 27,

    /// <summary>Resource prefetch hint, <c>svc_Prefetch</c>.</summary>
    Prefetch = 28,

    /// <summary>Menu display, <c>svc_Menu</c>.</summary>
    Menu = 29,

    /// <summary>
    /// Descriptors for every game event, <c>svc_GameEventList</c>. This is what makes game
    /// events self-describing, unlike user messages.
    /// </summary>
    GameEventList = 30,

    /// <summary>Server querying a client's cvar, <c>svc_GetCvarValue</c>.</summary>
    GetCvarValue = 31,

    /// <summary>Key-value payload, <c>svc_CmdKeyValues</c>.</summary>
    CmdKeyValues = 32,
}
