using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// Writes a header and command stream back out as a <c>.dem</c> file.
/// </summary>
/// <remarks>
/// **The inverse of the reader, and the point is the round trip rather than the writing.** A demo
/// that is read and written back byte for byte proves every field was understood: anything
/// misread, mis-sized or silently skipped comes back different. That is a far stronger statement
/// than "it decoded without stopping", which is satisfied by a reader that quietly discards
/// whatever it does not recognise — and this project did exactly that with <c>democmdinfo_t</c>
/// for its entire life.
///
/// Modelled on <c>lmpc</c>, the Quake tool this project's trace already borrows its shape from:
/// decompile to text, compile back, get the same file. This is the container half of that.
///
/// **Bytes the reader does not model are carried, not regenerated.** A command's prologue is
/// written back exactly as it arrived, because two of its fields are sequence numbers and most of
/// <c>democmdinfo_t</c> is a second split-screen view TF2 never fills. Reproducing them from
/// decoded values would mean inventing them.
/// </remarks>
public static class DemoWriter
{
    private const int Int32Bytes = 4;

    /// <summary>Bytes of the tick TF2 actually writes after <c>dem_stop</c>.</summary>
    /// <remarks>
    /// Three, not four. Every TF2 demo ends one byte short — confirmed across the corpus — so a
    /// writer emitting the full four produces a file one byte longer than the one it read.
    /// </remarks>
    private const int StopTickBytes = 3;

    /// <summary>Writes the demo.</summary>
    /// <param name="header">The header to write.</param>
    /// <param name="commands">Commands in stream order.</param>
    /// <returns>The complete file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> or <paramref name="commands"/> is null.</exception>
    public static byte[] Write(DemoHeader header, IReadOnlyList<DemoCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        using MemoryStream output = new();
        output.Write(WriteHeader(header));

        // One buffer, reused. A stackalloc inside the loop is a stack overflow waiting for a
        // 120,000-command demo (CA2014).
        Span<byte> scratch = stackalloc byte[Int32Bytes];

        foreach (DemoCommand command in commands)
        {
            output.WriteByte((byte)command.Type);

            // dem_stop's tick is truncated to three bytes by TF2's own writer, and the file ends
            // there. Emitting four would append a byte no real demo has.
            if (command.Type == DemoCommandType.Stop)
            {
                BinaryPrimitives.WriteInt32LittleEndian(scratch, command.Tick);
                output.Write(scratch[..StopTickBytes]);
                break;
            }

            BinaryPrimitives.WriteInt32LittleEndian(scratch, command.Tick);
            output.Write(scratch);

            output.Write(command.Prologue.Span);

            if (command.Type == DemoCommandType.SyncTick)
            {
                // No length prefix at all, which is the one command shape that is not
                // length-prefixed. Writing a zero length here would insert four bytes.
                continue;
            }

            BinaryPrimitives.WriteInt32LittleEndian(scratch, command.Payload.Length);
            output.Write(scratch);
            output.Write(command.Payload.Span);
        }

        return output.ToArray();
    }

    private static byte[] WriteHeader(DemoHeader header)
    {
        byte[] buffer = new byte[DemoHeader.SizeBytes];

        WriteText(buffer, 0, 8, "HL2DEMO");
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), header.DemoProtocol);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), header.NetworkProtocol);
        WriteText(buffer, 16, 260, header.ServerName);
        WriteText(buffer, 276, 260, header.ClientName);
        WriteText(buffer, 536, 260, header.MapName);
        WriteText(buffer, 796, 260, header.GameDirectory);
        BinaryPrimitives.WriteInt32LittleEndian(
            buffer.AsSpan(1056), BitConverter.SingleToInt32Bits(header.PlaybackTimeSeconds));
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1060), header.PlaybackTicks);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1064), header.PlaybackFrames);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1068), header.SignonLengthBytes);

        return buffer;
    }

    /// <summary>Writes a NUL-padded fixed-width field.</summary>
    /// <remarks>
    /// UTF-8, matching the reader. The bytes after the terminator are undefined in a real demo —
    /// TF2 leaves whatever was in its buffer — so a written file will differ from the original
    /// there even when every field is correct. That is a known limit of this round trip, not a
    /// decoding error, and it is why header comparison is by field rather than by bytes.
    /// </remarks>
    private static void WriteText(byte[] buffer, int offset, int width, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= width)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{value}' needs {bytes.Length} bytes and the field holds {width - 1}."));
        }

        bytes.CopyTo(buffer, offset);
    }
}
