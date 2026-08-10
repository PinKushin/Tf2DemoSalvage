namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>svc_Sounds</c> — one or more sound events, reported by header rather than decoded.
/// </summary>
/// <param name="IsReliable">Whether this was sent reliably, which also changes the wire shape.</param>
/// <param name="Count">How many sounds the body carries. Always 1 when reliable.</param>
/// <param name="BodyBits">How many bits the body occupies.</param>
/// <param name="Body">The body bits, decoded by <see cref="SoundDecoder"/>.</param>
/// <remarks>
/// **The body is deliberately not decoded, and the reference implementation does not decode it
/// either.** `demostf/parser` reads these same three header fields and keeps the payload as an
/// opaque stream. Decoding it would be novel work rather than parity work.
///
/// It is also the riskiest part of the format to attempt blind: four of the protocol boundaries
/// in Valve's `proto_version.h` are sound-related — the 13-bit sound index at 22, the special
/// DSP at 21, and the Halloween sound flag bit at 18 and 19 — and the corpus has nothing between
/// protocols 15 and 24 to exercise any of them. See `docs/TIMELINE.md`.
///
/// **The header shape is confirmed**, and it is the interesting part of it: the reliable flag
/// changes two fields at once. A reliable message implies a single sound and shrinks its length
/// field to eight bits; an unreliable one sends a count byte and a sixteen-bit length. Reading
/// one shape for the other consumes the wrong number of bits and desynchronises the packet.
/// </remarks>
public sealed record SoundsMessage(
    bool IsReliable, int Count, int BodyBits,
    System.ReadOnlyMemory<byte> Body = default) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.Sounds;
}
