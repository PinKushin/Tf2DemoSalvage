using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>The stereo conversion on a real game clip, held to the bytes it produced before it stopped allocating (finding 80).</summary>
/// <remarks>
/// **The oracle is recorded, not recomputed.** The digest below was printed on 2026-10-19 by the allocating
/// <c>ToStereo16</c> as it stood before the change: 149,282 shorts of <c>concrete_impact_bullet1.wav</c> at gains 0.8 and
/// 0.3. The conversion now writes into a reused buffer, since a new array per sound started was 90 MB of <c>short[]</c> per
/// 20 s of f12 (finding 80). The sound must not change, so the new path must reproduce those bytes exactly, and must allocate nothing doing so.
/// </remarks>
public sealed class AudioOutputRealClipTests
{
    private const string Clip = "sound/physics/concrete/concrete_impact_bullet1.wav";

    private const string RecordedDigest = "F66808B2328257212CE3A95308C3ABA937ECF8BE365FD12D2A99EDFFD4BFE4DB";

    [Test]
    public void ToStereo16_IntoABufferOnARealClip_IsByteIdenticalToTheOldConversionAndAllocatesNothing()
    {
        if (GameInstall.Root is not { } game || GameArchives.Open(game).Read(Clip) is not { } file)
        {
            Assert.Ignore("Team Fortress 2 is not installed.");
            return;
        }

        SoundSample sample = SoundSampleReader.Read(file).Sample.ShouldNotBeNull();

        // The clip is a 16-bit WAV, so this also holds the 16-bit storage (finding 80) to the float conversion's bytes.
        sample.Pcm16.IsEmpty.ShouldBeFalse();
        short[] into = new short[(sample.FrameCount * 2) + 10];
        into.AsSpan().Fill(1234);

        _ = AudioOutput.ToStereo16(sample, 0.8f, 0.3f, into);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int written = AudioOutput.ToStereo16(sample, 0.8f, 0.3f, into);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        written.ShouldBe(149_282);
        Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(into.AsSpan(0, written)))).ShouldBe(RecordedDigest);
        allocated.ShouldBe(0);
    }
}
