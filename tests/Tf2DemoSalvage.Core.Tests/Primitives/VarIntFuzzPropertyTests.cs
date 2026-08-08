using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Fuzz;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Deterministic layer of D8 target #2, mirroring <see cref="BitReaderFuzzPropertyTests"/>.
/// </summary>
public sealed class VarIntFuzzPropertyTests
{
    private const int Seed = 20260807;
    private const int RandomCaseCount = 2000;
    private const int MaxRandomLength = 96;
    private const int ModeCount = 4;

    [Fact]
    public void Consume_SeededRandomBuffers_NeverViolatesTheProperty()
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, MaxRandomLength + 1)];
            random.NextBytes(data);

            Should.NotThrow(() => VarIntFuzzTarget.Consume(data));
        }
    }

    [Fact]
    public void Consume_AllContinuationBytes_TerminatesInsteadOfReadingForever()
    {
        // The shape that would hang an unbounded decoder: every byte asks for another one.
        // Lengths well past both the 5- and 10-group limits.
        foreach (int length in new[] { 1, 5, 10, 11, 64, 512 })
        {
            byte[] data = new byte[length];
            Array.Fill(data, (byte)0xFF);

            Should.NotThrow(() => VarIntFuzzTarget.Consume(data));
        }
    }

    [Fact]
    public void Consume_EveryTruncationOfABuffer_NeverViolatesTheProperty()
    {
        byte[] full = new byte[64];
        new Random(Seed).NextBytes(full);

        for (int length = 0; length <= full.Length; length++)
        {
            byte[] truncated = full[..length];

            Should.NotThrow(() => VarIntFuzzTarget.Consume(truncated));
        }
    }

    [Fact]
    public void Consume_EverySingleBitFlipOfABuffer_NeverViolatesTheProperty()
    {
        byte[] original = new byte[16];
        new Random(Seed).NextBytes(original);

        for (int byteIndex = 0; byteIndex < original.Length; byteIndex++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] flipped = (byte[])original.Clone();
                flipped[byteIndex] ^= (byte)(1 << bit);

                Should.NotThrow(() => VarIntFuzzTarget.Consume(flipped));
            }
        }
    }

    /// <summary>
    /// The decoder is chosen from the input, so the corpus decides which of the four are reached
    /// at all. Measured, not assumed - same trap as the bit reader's field widths.
    /// </summary>
    [Fact]
    public void SeededCorpus_ReachesEveryDecoder()
    {
        HashSet<int> modes = new();
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, MaxRandomLength + 1)];
            random.NextBytes(data);

            VarIntFuzzTarget.ConsumeAndCountReads(data, modes);
        }

        IEnumerable<int> missing = Enumerable.Range(0, ModeCount).Except(modes);

        missing.ShouldBeEmpty(
            "the seeded corpus never reaches these decoders, so nothing about them is being fuzzed");
    }

    /// <summary>
    /// Proves the measurement above can fail, rather than passing for free.
    /// </summary>
    [Fact]
    public void ModeRecording_ClusteredCorpus_ReportsOnlyTheDecodersItReaches()
    {
        HashSet<int> modes = new();

        // Every byte 0x00 selects mode 0 and decodes as a single-byte varint, forever.
        for (int i = 0; i < 50; i++)
        {
            VarIntFuzzTarget.ConsumeAndCountReads(new byte[16], modes);
        }

        modes.ShouldBe([0]);
    }

    [Fact]
    public void ConsumeAndCountReads_ActuallyDecodes()
    {
        // 16 zero bytes: mode 0 every time, one byte consumed per decode.
        VarIntFuzzTarget.ConsumeAndCountReads(new byte[16], null).ShouldBe(16);

        VarIntFuzzTarget.ConsumeAndCountReads([], null).ShouldBe(0);
    }
}
