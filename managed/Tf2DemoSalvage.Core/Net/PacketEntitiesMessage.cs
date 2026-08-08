using System;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>svc_PacketEntities</c> — the entity delta snapshot. Header only, for now.
/// </summary>
/// <param name="MaxEntries">Entity slots the server is describing.</param>
/// <param name="IsDelta">Whether this snapshot is relative to an earlier one.</param>
/// <param name="DeltaFromTick">
/// The tick this delta is measured against, or <c>null</c> on a full snapshot.
/// </param>
/// <param name="BaselineIndex">Which of the two baseline sets the deltas reference.</param>
/// <param name="UpdatedEntries">How many entities this message updates.</param>
/// <param name="LengthBits">Size of the entity data that follows.</param>
/// <param name="UpdateBaseline">Whether the server wants the client's baseline refreshed.</param>
/// <param name="Body">
/// The undecoded entity data, exactly <paramref name="LengthBits"/> bits long. Carried rather
/// than decoded here because decoding it needs the schema, which arrives in a different demo
/// command entirely.
/// </param>
/// <remarks>
/// The message carries an explicit bit length, so the header can be read and the body isolated
/// without understanding it. <see cref="Tf2DemoSalvage.Core.Schema.EntityDecoder"/> turns that
/// body into entities and properties, but only with the schema in hand — which is why the two
/// are separate types rather than one decoder.
///
/// Isolating the body first also contains the damage. A malformed body cannot read past its
/// declared length into whatever follows, because the outer reader has already moved on.
/// </remarks>
public sealed record PacketEntitiesMessage(
    int MaxEntries,
    bool IsDelta,
    int? DeltaFromTick,
    bool BaselineIndex,
    int UpdatedEntries,
    int LengthBits,
    bool UpdateBaseline,
    ReadOnlyMemory<byte> Body) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.PacketEntities;

    /// <summary>
    /// Whether this is a full snapshot rather than a delta. The first packet of a demo is one,
    /// which is why it is far larger than those that follow.
    /// </summary>
    public bool IsFullSnapshot => !IsDelta;
}
