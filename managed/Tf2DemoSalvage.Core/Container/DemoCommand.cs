using System;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// One command from a demo's command stream, with its payload as a window onto the caller's
/// buffer rather than a copy.
/// </summary>
/// <param name="Type">Which command this is.</param>
/// <param name="Tick">Game tick the command was recorded at.</param>
/// <param name="Payload">
/// The command's data, empty for commands that carry none. For <see cref="DemoCommandType.Packet"/>
/// and <see cref="DemoCommandType.Signon"/> this is the network message data only —
/// <c>democmdinfo_t</c> and the sequence numbers are consumed by the reader.
/// </param>
/// <param name="Prologue">
/// The raw bytes between the command header and the payload, exactly as they appeared: for
/// <see cref="DemoCommandType.Signon"/> and <see cref="DemoCommandType.Packet"/> that is
/// <c>democmdinfo_t</c> plus two sequence numbers, and for <see cref="DemoCommandType.UserCmd"/>
/// the outgoing command number. Empty for every other type.
/// </param>
/// <param name="View">
/// The camera this command was recorded from, or <c>null</c> for command types that carry no
/// <c>democmdinfo_t</c>. Only <see cref="DemoCommandType.Signon"/> and
/// <see cref="DemoCommandType.Packet"/> carry one.
/// </param>
/// <remarks>
/// **Kept rather than skipped, because a demo has to be reproducible from what was read.** The
/// reader used to step over the prologue to reach the payload, which made a byte-exact rewrite
/// impossible: sequence numbers and the three view vectors this project does not model would have
/// had to be invented. Holding the raw bytes costs nothing — it is a window onto the same buffer
/// — and it is what lets a decompiled demo be compiled back.
///
/// <see cref="ReadOnlyMemory{T}"/> rather than <c>ReadOnlySpan</c> so commands can be yielded
/// from an iterator and held across an <c>await</c>; a span cannot leave the stack. Nothing is
/// copied either way, which matters when a 75 MB demo produces 120,000 of these.
/// </remarks>
public readonly record struct DemoCommand(
    DemoCommandType Type,
    int Tick,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> Prologue = default,
    ViewInfo? View = null);
