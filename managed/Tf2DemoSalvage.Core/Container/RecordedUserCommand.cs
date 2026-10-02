using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>One <c>dem_usercmd</c> as prediction reads it: when it was read, its outgoing sequence, and the command.</summary>
/// <param name="Tick">The demo tick it was recorded at, which is when playback reads it.</param>
/// <param name="Sequence">The outgoing sequence written before it — the number a packet acknowledges.</param>
/// <param name="Command">The command, delta-coded against a null one.</param>
public sealed record RecordedUserCommand(int Tick, int Sequence, UserCommand Command)
{
    /// <summary>A usercmd command's fields.</summary>
    /// <param name="command">A <see cref="DemoCommandType.UserCmd"/> command.</param>
    /// <returns>Its tick, sequence and decoded command.</returns>
    /// <exception cref="ArgumentException">The command is not a usercmd, or its prologue is short.</exception>
    public static RecordedUserCommand From(DemoCommand command)
    {
        if (command.Type != DemoCommandType.UserCmd || command.Prologue.Length < sizeof(int))
        {
            throw new ArgumentException("a dem_usercmd carries its outgoing sequence in a four-byte prologue", nameof(command));
        }

        return new RecordedUserCommand(
            command.Tick,
            BinaryPrimitives.ReadInt32LittleEndian(command.Prologue.Span),
            UserCommand.Decode(command.Payload.Span));
    }

    /// <summary>The last command a packet's state includes — the second sequence number after its <c>democmdinfo_t</c>.</summary>
    /// <param name="prologue">A packet's prologue.</param>
    /// <returns>The number, or null when the prologue does not carry two sequence numbers.</returns>
    /// <remarks>
    /// *Measured, not read:* the engine's writer is not in the SDK. On a 2013 POV demo the packet carrying 6109 already
    /// shows command 6109's input in its velocity, so the number is the one <c>PerformPrediction</c> calls
    /// <c>incoming_acknowledged</c>.
    /// </remarks>
    public static int? Acknowledged(ReadOnlySpan<byte> prologue) =>
        prologue.Length >= RecordedView.SizeBytes + (2 * sizeof(int))
            ? BinaryPrimitives.ReadInt32LittleEndian(prologue[(RecordedView.SizeBytes + sizeof(int))..])
            : null;
}
