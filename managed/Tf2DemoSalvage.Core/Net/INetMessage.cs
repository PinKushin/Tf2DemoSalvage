namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// A decoded network message.
/// </summary>
/// <remarks>
/// Intentionally empty. Implementations carry wholly unrelated payloads — a tick counter, a
/// string table, an entity delta — and forcing a shared shape onto them would invent structure
/// the format does not have. The marker exists so a packet's contents can be held in one list
/// and matched on by type.
/// </remarks>
public interface INetMessage
{
    /// <summary>Which message this is.</summary>
    public NetMessageType Type { get; }
}
