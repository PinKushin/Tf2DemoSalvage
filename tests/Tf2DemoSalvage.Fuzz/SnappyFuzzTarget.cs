using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// The D8 property for <see cref="Snappy"/>: decompression terminates, and refuses rather than
/// fails.
/// </summary>
/// <remarks>
/// **Targeted deliberately, on evidence.** The 2026-08-11 mutation run showed that a one-character
/// change in <c>Snappy.Decompress</c> — the loop's `read++` becoming `read--` — turns it into a
/// loop that never terminates. A mutant that can do that is a statement about the code, not about
/// the mutant: the loop bound comes from `compressed.Length`, but the *advance* comes from tag
/// bytes read out of the data, and nothing forces an iteration to make progress.
///
/// A string table arrives Snappy-compressed from the network, so this is reachable from a demo
/// file rather than being a theoretical concern.
///
/// Two properties:
///
/// 1. **It terminates.** Enforced by a wall-clock bound rather than left to libFuzzer's own
///    timeout, so a hang is reported as this target's violation naming the input size, instead of
///    as an opaque libFuzzer timeout.
/// 2. **Refusal is documented** — <see cref="InvalidDataException"/> only. An
///    <c>IndexOutOfRangeException</c> means a length or offset from the stream indexed the buffer
///    unchecked, and an <c>OutOfMemoryException</c> means a declared output size was believed.
/// </remarks>
public static class SnappyFuzzTarget
{
    /// <summary>
    /// Longer than any legitimate decompression of a fuzzer-sized input, short enough that a
    /// genuine hang is caught in the same second it happens.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>Decompresses arbitrary bytes.</summary>
    /// <param name="data">Arbitrary bytes, treated as a Snappy stream.</param>
    /// <exception cref="FuzzPropertyViolationException">It hung, or failed undocumented.</exception>
    public static void Consume(ReadOnlySpan<byte> data) => _ = ConsumeAndCountBytes(data);

    /// <summary>
    /// <see cref="Consume"/>, reporting how many bytes came out.
    /// </summary>
    /// <param name="data">Arbitrary bytes.</param>
    /// <returns>Decompressed length, or zero when the input was refused.</returns>
    public static int ConsumeAndCountBytes(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        long start = Stopwatch.GetTimestamp();

        try
        {
            byte[] output = Snappy.Decompress(data);
            EnsureTerminatedPromptly(start, data.Length);
            return output.Length;
        }
        catch (InvalidDataException)
        {
            // The documented refusal. Malformed input is the common case here by construction.
            EnsureTerminatedPromptly(start, data.Length);
            return 0;
        }
        catch (Exception undocumented)
        {
            throw new FuzzPropertyViolationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Decompressing {data.Length} bytes threw {undocumented.GetType().Name} " +
                    $"rather than refusing them. A length or offset read from the stream was " +
                    $"used without being checked against the buffer."),
                undocumented);
        }
    }

    private static void EnsureTerminatedPromptly(long start, int length)
    {
        TimeSpan taken = Stopwatch.GetElapsedTime(start);

        if (taken > Budget)
        {
            throw new FuzzPropertyViolationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Decompressing {length} bytes took {taken.TotalSeconds:F1}s. Every iteration " +
                $"must consume at least one byte of input, so no stream this small can take " +
                $"that long without a loop that fails to advance."));
        }
    }
}
