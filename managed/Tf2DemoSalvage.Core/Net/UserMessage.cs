using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// A <c>svc_UserMessage</c> that was not interpreted further, reported by type.
/// </summary>
/// <param name="UserMessageType">The game-defined message id.</param>
/// <param name="Name">The registered name, or <c>null</c> if the id is unknown.</param>
/// <param name="BodyBits">How many bits its body occupied.</param>
/// <param name="Fields">
/// The body's decoded fields, or <c>null</c> when the body was not decoded — either because no
/// layout is implemented for this type, or because the layout that was tried did not consume the
/// body exactly. Never a partial or best-effort reading.
/// </param>
/// <remarks>
/// **Named rather than merely counted, because a user message is where TF2 puts the things a
/// reader actually wants.** Damage numbers, announcements, vote results, class changes and chat
/// all travel this way. Before this, every one of them appeared in a trace as an anonymous
/// skipped message — 106 of them in a single 2009 demo.
///
/// Bodies are decoded only for the types worth reading, by <see cref="UserMessageBody"/>. Each
/// type has its own layout defined by the game DLL, so decoding all 79 would be 79 formats — and
/// most say nothing a reader wants. <c>CheapBreakModel</c> alone is 259 of the corpus's 756 user
/// messages and describes a piece of scenery shattering.
/// </remarks>
public sealed record UserMessage(
    int UserMessageType,
    string? Name,
    int BodyBits,
    IReadOnlyList<KeyValuePair<string, object?>>? Fields = null) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.UserMessage;
}
