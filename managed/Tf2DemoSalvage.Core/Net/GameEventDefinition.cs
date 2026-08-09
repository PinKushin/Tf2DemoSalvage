using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Value types a game event field can hold. Three bits on the wire.
/// </summary>
/// <remarks>
/// <see cref="None"/> doubles as the terminator for a definition's field list, which is why it
/// must be 0 and why the enum genuinely wants a zero member — unlike
/// <see cref="NetMessageType"/>, where a zero value would weaken validation.
/// </remarks>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte matches the on-disk field, which is 3 bits wide.")]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "String, Float, Long, Short and Byte are Valve's own names for these " +
                    "wire types, as they appear in the game event resource files. Renaming " +
                    "them to satisfy the analyzer would obscure the mapping to the format.")]
public enum GameEventValueType : byte
{
    /// <summary>End of a definition's field list. Not a value type in its own right.</summary>
    None = 0,

    /// <summary>NUL-terminated string.</summary>
    String = 1,

    /// <summary>32-bit float.</summary>
    Float = 2,

    /// <summary>32-bit signed integer.</summary>
    Long = 3,

    /// <summary>16-bit signed integer.</summary>
    Short = 4,

    /// <summary>8-bit unsigned integer.</summary>
    Byte = 5,

    /// <summary>Single bit.</summary>
    Bool = 6,

    /// <summary>
    /// A field the server declares but does not broadcast. Occupies no bits in an event body.
    /// </summary>
    /// <remarks>
    /// **Not a 64-bit integer, which is what this project called it until RISKS B14 was
    /// settled.** The mistake was assuming the wire numbering matched CS:GO's protobuf field
    /// ordering, where <c>val_uint64</c> lands eighth. That ordering needs a type field wide
    /// enough for <c>val_wstring</c> at nine; this one is three bits, so it stops at seven.
    /// The arithmetic ruled out the guess, and the comment two lines above the enum had
    /// recorded the width the whole time.
    ///
    /// Valve's own ordering in <c>igameevents.h</c> is the corroboration: "Valid data types are
    /// string, float, long, short, byte &amp; bool. If a data field should not be broadcasted to
    /// clients, use the type 'local'." That is 1 through 6, then 7.
    /// </remarks>
    Local = 7,
}

/// <summary>One named field of a game event.</summary>
/// <param name="Name">Field name, e.g. <c>userid</c>.</param>
/// <param name="Type">How the field is encoded.</param>
public readonly record struct GameEventField(string Name, GameEventValueType Type);

/// <summary>
/// The schema for one game event, as the demo itself describes it.
/// </summary>
/// <param name="Id">Event id, referenced by <c>svc_GameEvent</c>.</param>
/// <param name="Name">Event name, e.g. <c>player_death</c>.</param>
/// <param name="Fields">Fields in wire order.</param>
/// <remarks>
/// This is what makes game events decodable generically while user messages are not: the
/// demo carries the schema for its own events, so a parser needs no compiled-in knowledge of
/// what TF2's events looked like in any given year.
/// </remarks>
public sealed record GameEventDefinition(
    int Id,
    string Name,
    IReadOnlyList<GameEventField> Fields);
