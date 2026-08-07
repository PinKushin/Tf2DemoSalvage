using System;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// The D8 property for <see cref="BitReader"/>, expressed once and driven from two places: the
/// coverage-guided runner in <c>Program</c>, and the deterministic seeded suite in
/// <c>Tf2DemoSalvage.Core.Tests</c>. Keeping one definition means the cheap layer and the
/// expensive layer cannot drift apart about what "correct" means.
/// </summary>
public static class BitReaderFuzzTarget
{
    private const int BitsPerByte = 8;
    private const int MaxBitsPerRead = 32;

    /// <summary>
    /// Reads <paramref name="data"/> to exhaustion, using the bytes themselves to choose each
    /// field width, and asserts the reader never violates its contract.
    /// </summary>
    /// <remarks>
    /// Checked on every read:
    /// <list type="bullet">
    /// <item>the position advances by exactly the number of bits requested;</item>
    /// <item><c>BitsRead + BitsRemaining</c> stays equal to the buffer's total bit count;</item>
    /// <item>the returned value is zero-extended - no bits set above the requested width.</item>
    /// </list>
    /// Then, at the end, that an over-read throws <see cref="EndOfStreamException"/> without
    /// consuming anything.
    ///
    /// <see cref="EndOfStreamException"/> is the only exception treated as a documented
    /// outcome. <see cref="ArgumentOutOfRangeException"/> deliberately is not: the harness never
    /// passes a width outside 1-32, so seeing one means the guard fires when it should not.
    /// </remarks>
    /// <exception cref="FuzzPropertyViolationException">The reader broke its contract.</exception>
    public static void Consume(ReadOnlySpan<byte> data) => _ = ConsumeAndCountReads(data);

    /// <summary>
    /// <see cref="Consume"/>, additionally reporting how many successful reads it performed.
    /// </summary>
    /// <remarks>
    /// Exists so the deterministic suite can prove the harness is doing work. A target that
    /// silently stopped exercising the reader would make every property test pass vacuously -
    /// the same failure mode as a libFuzzer run that executes nothing and still reports green.
    /// </remarks>
    /// <returns>The number of reads that completed before the buffer was exhausted.</returns>
    public static int ConsumeAndCountReads(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        int reads = 0;

        long totalBits = (long)data.Length * BitsPerByte;
        var reader = new BitReader(data);
        int cursor = 0;

        while (reader.BitsRemaining > 0)
        {
            // 1-32, never 0: a zero-width read makes no progress and would hang the fuzzer on
            // any input whose bytes happen to select it.
            int bitCount = (data[cursor % data.Length] % MaxBitsPerRead) + 1;
            cursor++;

            if (bitCount > reader.BitsRemaining)
            {
                AssertOverReadIsRejected(ref reader, bitCount);
                return reads;
            }

            int positionBefore = reader.BitsRead;
            uint value = reader.ReadUInt32(bitCount);
            reads++;

            if (reader.BitsRead != positionBefore + bitCount)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Reading {bitCount} bits moved the position from {positionBefore} to " +
                    $"{reader.BitsRead}."));
            }

            if (reader.BitsRead + reader.BitsRemaining != totalBits)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"BitsRead ({reader.BitsRead}) + BitsRemaining ({reader.BitsRemaining}) " +
                    $"no longer equals the buffer's {totalBits} bits."));
            }

            if (bitCount < MaxBitsPerRead && (value >> bitCount) != 0)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {bitCount}-bit read returned 0x{value:X8}, which has bits set above " +
                    $"its width."));
            }
        }

        AssertOverReadIsRejected(ref reader, 1);
        return reads;
    }

    private static void AssertOverReadIsRejected(ref BitReader reader, int bitCount)
    {
        int positionBefore = reader.BitsRead;

        try
        {
            uint value = reader.ReadUInt32(bitCount);
            throw new FuzzPropertyViolationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Reading {bitCount} bits with only {reader.BitsRemaining} remaining returned " +
                $"0x{value:X8} instead of throwing."));
        }
        catch (EndOfStreamException)
        {
            // The documented outcome. The reader must still be intact afterwards, so a caller
            // can report where the file ran out.
            if (reader.BitsRead != positionBefore)
            {
                throw new FuzzPropertyViolationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A rejected read still advanced the position from {positionBefore} to " +
                    $"{reader.BitsRead}."));
            }
        }
    }
}
