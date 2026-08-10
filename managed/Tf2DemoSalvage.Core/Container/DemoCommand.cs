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
/// <param name="View">
/// The camera this command was recorded from, or <c>null</c> for command types that carry no
/// <c>democmdinfo_t</c>. Only <see cref="DemoCommandType.Signon"/> and
/// <see cref="DemoCommandType.Packet"/> carry one.
/// </param>
/// <remarks>
/// <see cref="ReadOnlyMemory{T}"/> rather than <c>ReadOnlySpan</c> so commands can be yielded
/// from an iterator and held across an <c>await</c>; a span cannot leave the stack. Nothing is
/// copied either way, which matters when a 75 MB demo produces 120,000 of these.
/// </remarks>
public readonly record struct DemoCommand(
    DemoCommandType Type,
    int Tick,
    ReadOnlyMemory<byte> Payload,
    ViewInfo? View = null);
