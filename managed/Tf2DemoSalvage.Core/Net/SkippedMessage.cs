namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// A message that was read and stepped over without being interpreted.
/// </summary>
/// <param name="Type">The message type.</param>
/// <param name="BodyBits">How many bits its body occupied, excluding the type field.</param>
/// <remarks>
/// **Recorded rather than dropped, because the alternative made the trace lie.** Sixteen message
/// types are consumed for alignment only — their contents are not needed, but the bits they
/// occupy are. Emitting nothing for them meant a trace could show a packet with no messages and
/// a note that a hundred bits had been consumed, which reads as "this packet was corrupt from
/// the first bit" when the truth is "several messages were read and none were worth printing".
///
/// That distinction stopped being cosmetic during the investigation in <c>RISKS.md</c> B16: two
/// point-of-view demos failed at nearly the same offset with different message ids, and the
/// messages that had preceded the failure were invisible. A trace that hides what it consumed
/// cannot be used to find where consumption went wrong.
/// </remarks>
public sealed record SkippedMessage(NetMessageType Type, int BodyBits) : INetMessage
{
    /// <inheritdoc />
    NetMessageType INetMessage.Type => Type;
}
