using System;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Names the leading byte of an <c>svc_EntityMessage</c>, given the class that will receive it.
/// </summary>
/// <remarks>
/// **This message is not schema-driven, but it is a closed set, and the set is tiny.** Its body is
/// handled by the receiving entity's class through
/// <c>ReceiveMessage( int classID, bf_read &amp;msg )</c>, every implementation of which opens
/// identically:
///
/// <code>
/// int messageType = msg.ReadByte();
/// switch( messageType ) { ... }
/// </code>
///
/// So the class id on the wire picks the handler and the first byte picks the case. The SDK
/// contains eighteen <c>ReceiveMessage</c> overrides in total, most of them HL2 and episodic, and
/// **`game/client/tf/` overrides it not at all** — so TF2's set is the inherited one and this
/// table can be complete rather than partial.
///
/// **Why the class is required, and why the byte was reported unnamed until now.** The same value
/// means different things to different handlers:
///
/// | value | in `C_BaseEntity` | in `C_BasePlayer` |
/// |---|---|---|
/// | 1 | `BASEENTITY_MSG_REMOVE_DECALS` | `PLAY_PLAYER_JINGLE` |
///
/// Both are 1, both are read from the same position, and nothing in the body distinguishes them.
/// Naming the byte without resolving the class id to a class name would be a claim about which
/// handler applies — the same failure the user message table's era gate exists to prevent.
///
/// Measured: every entity message in the corpus is class `CBaseAnimating`, eight bits, type 1 —
/// 590 of them in one RGL pug POV and a handful elsewhere. `CBaseAnimating` declares no
/// <c>ReceiveMessage</c>, so it inherits <c>C_BaseEntity</c>'s, and those 590 are
/// <c>RemoveAllDecals</c> with no payload. See `RISKS.md` B30.
/// </remarks>
public static class EntityMessageNames
{
    /// <summary><c>BASEENTITY_MSG_REMOVE_DECALS</c>, from `baseentity_shared.h`.</summary>
    /// <remarks>Internal so <c>EntityMessageConformanceTests</c> checks these rather than copies.</remarks>
    internal const int RemoveDecals = 1;

    /// <summary><c>PLAY_PLAYER_JINGLE</c>, the player handler's case for the same value.</summary>
    internal const int PlayerJingle = 1;

    /// <summary>The name of an entity message's type byte, or <c>null</c> if it is not known.</summary>
    /// <param name="className">The receiving class, e.g. <c>CBaseAnimating</c>.</param>
    /// <param name="messageType">The leading byte of the body.</param>
    /// <returns>The SDK's constant name, or <c>null</c> when nothing here can name it safely.</returns>
    /// <remarks>
    /// Returning <c>null</c> rather than a guess keeps the existing behaviour for anything outside
    /// the set: the number is reported and no meaning is claimed.
    /// </remarks>
    public static string? Lookup(string? className, int messageType)
    {
        if (className is null)
        {
            return null;
        }

        // C_BasePlayer is the only handler in the inherited set that reuses value 1, so the player
        // check has to come first. Matching on the suffix rather than an exact name because the
        // class on the wire is the game's own subclass - CTFPlayer here, CCSPlayer elsewhere - and
        // every one of them inherits C_BasePlayer's handler unchanged.
        bool isPlayer = className.EndsWith("Player", StringComparison.Ordinal);

        return messageType switch
        {
            PlayerJingle when isPlayer => "PLAY_PLAYER_JINGLE",
            RemoveDecals => "BASEENTITY_MSG_REMOVE_DECALS",
            _ => null,
        };
    }
}
