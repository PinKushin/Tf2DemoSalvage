namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// A <c>svc_UserMessage</c> that was not interpreted further, reported by type.
/// </summary>
/// <param name="UserMessageType">The game-defined message id.</param>
/// <param name="Name">The registered name, or <c>null</c> if the id is unknown.</param>
/// <param name="BodyBits">How many bits its body occupied.</param>
/// <remarks>
/// **Named rather than merely counted, because a user message is where TF2 puts the things a
/// reader actually wants.** Damage numbers, announcements, vote results, class changes and chat
/// all travel this way. Before this, every one of them appeared in a trace as an anonymous
/// skipped message — 106 of them in a single 2009 demo.
///
/// The body is deliberately not decoded here. Each type has its own layout defined by the game
/// DLL, so decoding them is 79 separate formats; naming the type is most of the readability for
/// a fraction of the work, and it makes the remaining formats individually addressable rather
/// than hidden behind one anonymous count.
/// </remarks>
public sealed record UserMessage(int UserMessageType, string? Name, int BodyBits) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.UserMessage;
}
