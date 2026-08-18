using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Fuzz;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The deterministic layer for the voice fuzz targets.
/// </summary>
/// <remarks>
/// **These live in the audio suite rather than the core one because they need the native codecs.**
/// The other fuzz property suites sit in <c>Tf2DemoSalvage.Core.Tests</c> and run anywhere; these
/// load libopus, libcelt and libspeex, so they run where those exist — which, since
/// <c>tools/native-audio/build.sh</c>, includes the measurement box.
///
/// **What this layer can and cannot show.** It proves the target's MANAGED contract: that random
/// bytes produce only the three documented exceptions, and that an accepted frame returns a
/// plausible number of samples. It cannot prove the absence of memory corruption — an out-of-bounds
/// read that does not segfault is invisible to a managed assertion, and that is precisely why the
/// coverage-guided run under libFuzzer is the layer that matters here. This one exists so a
/// regression in the wrapper fails the ordinary build.
/// </remarks>
public sealed class VoiceFuzzPropertyTests
{
    /// <summary>Fixed so a failure is reproducible.</summary>
    private const int Seed = 20260818;

    private const int RandomCaseCount = 400;

    /// <summary>Real TF2 voice frames are 64, 128 or 192 bytes.</summary>
    private const int MaxRandomLength = 256;

    private static IEnumerable<VoiceCodec> Codecs =>
        [VoiceCodec.Opus, VoiceCodec.Celt, VoiceCodec.Speex];

    [TestCaseSource(nameof(Codecs))]
    public void Consume_SeededRandomFrames_OnlyProducesDocumentedOutcomes(VoiceCodec codec)
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] frame = new byte[random.Next(1, MaxRandomLength + 1)];
            random.NextBytes(frame);

            Should.NotThrow(
                () => VoiceFuzzTarget.Consume(codec, frame),
                $"{codec} should refuse a random {frame.Length}-byte frame, not break its contract");
        }
    }

    [TestCaseSource(nameof(Codecs))]
    public void Consume_TheRealFrameSizes_OnlyProduceDocumentedOutcomes(VoiceCodec codec)
    {
        // The three lengths TF2 actually sends. Random bytes at a REAL length get further into a
        // codec than random bytes at a random length, because the length itself is often the first
        // thing checked - so these reach past the cheapest rejection.
        foreach (int length in new[] { 64, 128, 192 })
        {
            byte[] frame = new byte[length];
            new Random(Seed + length).NextBytes(frame);

            Should.NotThrow(() => VoiceFuzzTarget.Consume(codec, frame), $"{codec} at {length} bytes");
        }
    }

    [TestCaseSource(nameof(Codecs))]
    public void Consume_EmptyAndOversizedFrames_AreSkippedRatherThanThrown(VoiceCodec codec)
    {
        // Both ends of the size guard. Empty is a documented ArgumentException for two of the
        // three; oversized is refused by the target itself so libFuzzer does not spend its budget
        // growing one input for ever.
        Should.NotThrow(() => VoiceFuzzTarget.Consume(codec, []));
        Should.NotThrow(() => VoiceFuzzTarget.Consume(codec, new byte[8192]));
    }

    [TestCaseSource(nameof(Codecs))]
    public void Consume_AllZeroAndAllOnesFrames_OnlyProduceDocumentedOutcomes(VoiceCodec codec)
    {
        // The two structured extremes. All-zero is the frame a truncated stream leaves behind, and
        // all-ones sets every flag and length field the codec reads.
        byte[] zeros = new byte[64];

        byte[] ones = new byte[64];
        Array.Fill(ones, (byte)0xFF);

        Should.NotThrow(() => VoiceFuzzTarget.Consume(codec, zeros), $"{codec} all-zero");
        Should.NotThrow(() => VoiceFuzzTarget.Consume(codec, ones), $"{codec} all-ones");
    }
}
