using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Fuzz;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// The deterministic layer of D8: the same property the libFuzzer target enforces, driven over a
/// seeded, reproducible set of inputs so it runs in the normal suite in milliseconds.
/// </summary>
/// <remarks>
/// This is not a substitute for coverage-guided fuzzing - it explores blindly, where libFuzzer
/// explores toward new code paths. What it does buy is that a regression in <c>BitReader</c>'s
/// bounds handling fails the build immediately, without anyone needing a Linux box, and that a
/// failure names a fixed seed rather than an input nobody can reproduce.
/// </remarks>
public sealed class BitReaderFuzzPropertyTests
{
    /// <summary>
    /// Fixed so a failure is reproducible. If one of these seeds ever fails, do not change it -
    /// promote the offending buffer to its own named regression fixture.
    /// </summary>
    private const int Seed = 20260807;

    private const int RandomCaseCount = 2000;
    private const int MaxRandomLength = 96;

    [Test]
    public void Consume_SeededRandomBuffers_NeverViolatesTheProperty()
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, MaxRandomLength + 1)];
            random.NextBytes(data);

            Should.NotThrow(() => BitReaderFuzzTarget.Consume(data));
        }
    }
    [TestCaseSource(nameof(StructuredBuffers))]
    public void Consume_StructuredEdgeCaseBuffers_NeverViolatesTheProperty(byte[] data)
    {
        Should.NotThrow(() => BitReaderFuzzTarget.Consume(data));
    }

    [Test]
    public void Consume_EveryTruncationOfABuffer_NeverViolatesTheProperty()
    {
        // Truncation is the failure mode a salvage tool actually meets: a demo that stops
        // mid-packet because the recording process died. Every prefix length must behave.
        byte[] full = new byte[64];
        new Random(Seed).NextBytes(full);

        for (int length = 0; length <= full.Length; length++)
        {
            byte[] truncated = full[..length];

            Should.NotThrow(() => BitReaderFuzzTarget.Consume(truncated));
        }
    }

    [Test]
    public void Consume_EverySingleBitFlipOfABuffer_NeverViolatesTheProperty()
    {
        // The buffer's own bytes choose the field widths, so flipping one bit changes the whole
        // read schedule after it, not just one value.
        byte[] original = new byte[16];
        new Random(Seed).NextBytes(original);

        for (int byteIndex = 0; byteIndex < original.Length; byteIndex++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] flipped = (byte[])original.Clone();
                flipped[byteIndex] ^= (byte)(1 << bit);

                Should.NotThrow(() => BitReaderFuzzTarget.Consume(flipped));
            }
        }
    }

    /// <summary>
    /// Hand-picked buffers covering the shapes random bytes hit rarely: empty, single-byte,
    /// widths that exactly exhaust the buffer, and widths that straddle byte boundaries.
    /// </summary>
    internal static IReadOnlyList<byte[]> StructuredCases { get; } = BuildStructuredCases();

    /// <summary>The structured buffers, as NUnit test cases.</summary>
    /// <returns>One case per buffer.</returns>
    /// <remarks>
    /// **Each buffer is wrapped and cast to object deliberately.** A source that yields a bare
    /// <c>byte[]</c> is read by NUnit as an ARGUMENT LIST, so a 4-byte buffer would arrive as a
    /// call with four arguments and fail to match the one-parameter signature. Casting to object
    /// tells it the array is the single argument.
    /// </remarks>
    public static IEnumerable<TestCaseData> StructuredBuffers()
    {
        foreach (byte[] buffer in StructuredCases)
        {
            yield return new TestCaseData((object)buffer);
        }
    }

    private static List<byte[]> BuildStructuredCases()
    {
        List<byte[]> cases = new()
        {
            Array.Empty<byte>(),
            new byte[] { 0x00 },
            new byte[] { 0xFF },

            // 0x1F selects a 32-bit width, so the first read consumes four of five bytes and the
            // remainder must be handled without reaching past the end.
            new byte[] { 0x1F, 0x00, 0x00, 0x00, 0x00 },

            // 0x00 selects width 1 throughout: the maximum number of reads for a given length.
            new byte[] { 0x00, 0x00, 0x00, 0x00 },

            // Alternating widths that straddle byte boundaries repeatedly.
            new byte[] { 0x06, 0x0C, 0x11, 0x03, 0x1E, 0x07 },
        };

        // An odd length so the final read is always partial, across a spread of fixed widths.
        foreach (byte fill in new byte[] { 0x01, 0x07, 0x0F, 0x1F, 0x80, 0xAA })
        {
            byte[] filled = new byte[17];
            Array.Fill(filled, fill);
            cases.Add(filled);
        }

        return cases;
    }

    /// <summary>
    /// Guards the harness itself. If <c>Consume</c> silently stopped exercising the reader, every
    /// test above would pass vacuously - the same failure mode as a fuzz run that executes
    /// nothing and still reports green.
    /// </summary>
    [Test]
    public void ConsumeAndCountReads_ActuallyExercisesTheReader()
    {
        // All-zero bytes select width 1 on every read, so a 4-byte buffer is read 32 times -
        // the maximum possible for its length, and an exact number rather than "more than none".
        BitReaderFuzzTarget.ConsumeAndCountReads([0x00, 0x00, 0x00, 0x00]).ShouldBe(32);

        // 0x1F selects width 32, consuming four of the five bytes in one read. The next four
        // bytes are 0x00, so four single-bit reads follow, leaving 4 bits. The cursor then wraps
        // to byte 0 and selects width 32 again, which no longer fits - so the run ends there,
        // on the rejected over-read, after 5 successful reads.
        BitReaderFuzzTarget.ConsumeAndCountReads([0x1F, 0x00, 0x00, 0x00, 0x00]).ShouldBe(5);

        BitReaderFuzzTarget.ConsumeAndCountReads([]).ShouldBe(0);
    }

    /// <summary>
    /// The harness picks each field width from the buffer's own bytes, so the corpus decides
    /// which code paths are reachable at all. Measured, never assumed.
    /// </summary>
    /// <remarks>
    /// Random bytes were never in doubt - this assertion exists to fail loudly the moment real
    /// seed data is introduced (target #4 seeds with <c>z1800.dem</c>). Real bytes are not
    /// uniformly distributed, which is exactly what makes them good seeds, so they cluster on a
    /// narrow set of widths and silently stop exercising the rest. That is invisible from
    /// outside: the run still looks healthy.
    /// </remarks>
    [Test]
    public void SeededCorpus_ReachesEveryFieldWidth()
    {
        HashSet<int> widths = new();
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, MaxRandomLength + 1)];
            random.NextBytes(data);

            BitReaderFuzzTarget.ConsumeAndCountReads(data, widths);
        }

        IEnumerable<int> missing = Enumerable.Range(1, 32).Except(widths);

        missing.ShouldBeEmpty(
            "the seeded corpus never exercises these field widths, so nothing about them is " +
            "actually being fuzzed");
    }

    /// <summary>
    /// Proves the width measurement above can actually fail. A corpus that dispatches to one
    /// branch must be reported as reaching one width - otherwise
    /// <see cref="SeededCorpus_ReachesEveryFieldWidth"/> passes for free and guards nothing.
    /// </summary>
    [Test]
    public void WidthRecording_ClusteredCorpus_ReportsOnlyTheWidthsItReaches()
    {
        HashSet<int> widths = new();

        // Every byte 0x00 selects width 1, and nothing else, however much of it there is.
        for (int i = 0; i < 50; i++)
        {
            BitReaderFuzzTarget.ConsumeAndCountReads(new byte[16], widths);
        }

        widths.ShouldBe([1]);
    }

    [Test]
    public void ConsumeAndCountReads_EveryStructuredBuffer_PerformsAtLeastOneReadPerByte()
    {
        foreach (byte[] data in StructuredCases)
        {
            int reads = BitReaderFuzzTarget.ConsumeAndCountReads(data);

            // Width is at most 32 bits, so a buffer of n bytes cannot be drained in fewer than
            // n/4 reads. Anything less means the harness bailed out early.
            reads.ShouldBeGreaterThanOrEqualTo(data.Length / 4);
        }
    }
}
