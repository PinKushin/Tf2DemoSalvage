namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>net_NOP</c> — padding. The six type bits and no body at all.
/// </summary>
/// <remarks>
/// Reported rather than swallowed. It carries no information, but a reader that quietly
/// dropped it would under-report what the stream actually contains, and "two messages" versus
/// "one message plus padding" is the kind of discrepancy that makes a cross-parser diff
/// disagree for no real reason. Callers that find it noisy can filter it out; the reader
/// should not decide that for them.
/// </remarks>
public sealed record NetEmptyMessage : INetMessage
{
    /// <summary>The single instance — it has no state to vary.</summary>
    public static NetEmptyMessage Instance { get; } = new();

    private NetEmptyMessage()
    {
    }

    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.Empty;
}
