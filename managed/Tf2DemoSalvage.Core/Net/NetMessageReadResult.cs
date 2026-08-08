using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// What came out of walking one packet's message stream, and where the walk ended.
/// </summary>
/// <remarks>
/// A plain list of messages would not be honest. Because messages are not length-prefixed,
/// reaching an unimplemented type means the rest of the packet is unreachable — not absent.
/// The difference between "this packet held two messages" and "we could only read two of
/// them" matters enormously, so it is in the type rather than left to be inferred.
/// </remarks>
public sealed record NetMessageReadResult
{
    /// <summary>Messages decoded, in stream order.</summary>
    public required IReadOnlyList<INetMessage> Messages { get; init; }

    /// <summary>
    /// Number of bits successfully consumed. Excludes the type field of whatever stopped the
    /// walk, so it marks the last known-good position.
    /// </summary>
    public required int BitsConsumed { get; init; }

    /// <summary>
    /// The recognised-but-unimplemented message that ended the walk, or <c>null</c> if the
    /// walk ended for any other reason.
    /// </summary>
    public NetMessageType? StoppedAt { get; init; }

    /// <summary>
    /// Why the walk ended early, or <c>null</c> if it consumed the packet normally. Trailing
    /// padding bits are normal and do not set this.
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>Whether the whole packet was read without hitting something unhandled.</summary>
    public bool IsComplete => StopReason is null;
}
