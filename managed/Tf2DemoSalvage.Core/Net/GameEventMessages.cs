using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>svc_GameEventList</c> — the schema for every game event this demo can contain.
/// </summary>
/// <param name="Definitions">Event definitions, in the order transmitted.</param>
/// <remarks>
/// Sent once, early. Everything <see cref="GameEventMessage"/> can report depends on having
/// seen it first.
/// </remarks>
/// <param name="BodyBits">
/// The body length the message stated, kept because it is not derivable from the contents: a
/// sender measures its buffer after rounding, so a body routinely runs a few bits past its last
/// field and those bits are on the wire.
/// </param>
public sealed record GameEventListMessage(
    IReadOnlyList<GameEventDefinition> Definitions,
    int BodyBits = 0) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.GameEventList;
}

/// <summary>
/// <c>svc_GameEvent</c> — one event firing, decoded against its definition.
/// </summary>
/// <param name="EventId">Which event fired.</param>
/// <param name="Name">
/// The event's name, or <c>null</c> if no definition for it has been seen yet.
/// </param>
/// <param name="Values">
/// Field values by name, empty when the event could not be decoded.
/// </param>
/// <remarks>
/// A game event is length-prefixed, so it can be stepped over even when undecodable — unlike
/// most messages in this layer. That is why an event arriving before its definition is
/// reported with a null <paramref name="Name"/> rather than stopping the walk: the loss is one
/// event, not the rest of the packet.
/// </remarks>
/// <param name="BodyBits">
/// The body length the message stated, kept because it is not derivable from the contents: a
/// sender measures its buffer after rounding, so a body routinely runs a few bits past its last
/// field and those bits are on the wire.
/// </param>
public sealed record GameEventMessage(
    int EventId,
    string? Name,
    IReadOnlyDictionary<string, object?> Values,
    int BodyBits = 0) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.GameEvent;

    /// <summary>Whether the event was matched to a definition and decoded.</summary>
    public bool IsDecoded => Name is not null;
}
