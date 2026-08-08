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
/// <remarks>
/// **The values are not decoded yet.** The message carries an explicit bit length, so the
/// header can be read and the body stepped over — which is what unblocks the roughly 90% of
/// gameplay packets that previously stopped here. What the body contains is entity index
/// deltas, update types, and property values indexed into the flattened schema.
///
/// That last part is deliberately not attempted in the same step. A property decoder that is
/// subtly wrong produces plausible positions rather than an error, so it wants the
/// cross-parser differential harness alongside it rather than after (<c>RISKS.md</c> B4).
/// </remarks>
public sealed record PacketEntitiesMessage(
    int MaxEntries,
    bool IsDelta,
    int? DeltaFromTick,
    bool BaselineIndex,
    int UpdatedEntries,
    int LengthBits,
    bool UpdateBaseline) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.PacketEntities;

    /// <summary>
    /// Whether this is a full snapshot rather than a delta. The first packet of a demo is one,
    /// which is why it is far larger than those that follow.
    /// </summary>
    public bool IsFullSnapshot => !IsDelta;
}
