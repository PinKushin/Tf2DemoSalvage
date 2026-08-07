using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// D8 target #2: varint decoding. Length-prefix decoders are where unbounded allocations come
/// from, so this one matters more than its size suggests.
/// </summary>
/// <remarks>
/// Adds <see cref="InvalidDataException"/> to the documented-outcome set - a varint whose
/// encoding runs past its maximum length is invalid input, not a defect. What must never happen
/// is the decoder reading on indefinitely, so the bound is part of the property, not an
/// implementation detail.
/// </remarks>
public static class VarIntFuzzTarget
{
    private const int ModeCount = 4;

    /// <summary>
    /// Reads <paramref name="data"/> as a stream of varints until it is exhausted, cycling
    /// through the four decoders, and asserts none of them ever fails in an undocumented way.
    /// </summary>
    /// <param name="data">Buffer to decode.</param>
    /// <exception cref="FuzzPropertyViolationException">A decoder broke its contract.</exception>
    public static void Consume(ReadOnlySpan<byte> data) => _ = ConsumeAndCountReads(data, null);

    /// <summary>
    /// <see cref="Consume"/>, additionally reporting the number of successful decodes and
    /// recording which decoder produced each one.
    /// </summary>
    /// <param name="data">Buffer to decode.</param>
    /// <param name="modesObserved">Collects each decoder used, or <c>null</c> to skip recording.</param>
    /// <returns>The number of values decoded before the buffer was exhausted.</returns>
    /// <remarks>
    /// The mode is chosen from the input, so the same clustering trap applies here as to the
    /// bit reader: real seed data will not spread evenly across the four decoders, and a run
    /// that only ever exercises one looks exactly like a healthy one from outside.
    /// </remarks>
    public static int ConsumeAndCountReads(ReadOnlySpan<byte> data, ICollection<int>? modesObserved)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        int decoded = 0;
        var reader = new BitReader(data);
        int cursor = 0;

        while (reader.BitsRemaining > 0)
        {
            int mode = data[cursor % data.Length] % ModeCount;
            cursor++;

            int positionBefore = reader.BitsRead;

            try
            {
                Decode(ref reader, mode);
            }
            catch (EndOfStreamException)
            {
                // Documented: the buffer ended part-way through an encoding.
                return decoded;
            }
            catch (InvalidDataException)
            {
                // Documented: the encoding is longer than the type allows. The decoder is
                // required to stop rather than read on, which is the whole point of the bound.
                return decoded;
            }

            if (reader.BitsRead <= positionBefore)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Decoding mode {mode} at bit offset {positionBefore} consumed nothing, so " +
                    $"the stream cannot make progress."));
            }

            decoded++;
            modesObserved?.Add(mode);
        }

        return decoded;
    }

    private static void Decode(ref BitReader reader, int mode)
    {
        switch (mode)
        {
            case 0:
                _ = VarInt.ReadUInt32(ref reader);
                break;
            case 1:
                _ = VarInt.ReadInt32(ref reader);
                break;
            case 2:
                _ = VarInt.ReadUInt64(ref reader);
                break;
            default:
                _ = VarInt.ReadInt64(ref reader);
                break;
        }
    }
}
