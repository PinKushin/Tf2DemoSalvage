using System;
using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Tests <see cref="OpusVoiceDecoder"/>'s lifecycle and error handling.
/// </summary>
/// <remarks>
/// Real Opus frames are not hand-built here — a byte-level Opus fixture is fragile in the way
/// this project's own memory warns against for hand-written fixtures, and the corpus already
/// contains 1452 real ones. <c>Tf2DemoSalvage.Corpus.Tests</c> is where actual decode correctness
/// is checked, against real frames extracted by <c>SteamVoicePayload</c>. This file covers what
/// does not need a real frame: construction, disposal, and rejection of garbage input.
/// </remarks>
public sealed class OpusVoiceDecoderTests
{
    [Test]
    public void Construction_LoadsTheNativeLibraryAndSucceeds()
    {
        // The cheapest possible proof that the libopus native asset actually resolved for this
        // RID: opus_decoder_create either returns a real handle or this throws.
        using OpusVoiceDecoder decoder = new();
        decoder.ShouldNotBeNull();
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        OpusVoiceDecoder decoder = new();
        decoder.Dispose();
        Should.NotThrow(decoder.Dispose);
    }

    [Test]
    public void Decode_AfterDispose_Throws()
    {
        OpusVoiceDecoder decoder = new();
        decoder.Dispose();

        Should.Throw<ObjectDisposedException>(() => decoder.Decode([0x68, 0x01, 0x02]));
    }

    [Test]
    public void ConcealLoss_AfterDispose_Throws()
    {
        OpusVoiceDecoder decoder = new();
        decoder.Dispose();

        Should.Throw<ObjectDisposedException>(() => decoder.ConcealLoss(120));
    }

    [Test]
    public void Decode_GarbageBytes_ThrowsRatherThanCrashing()
    {
        // Not a real Opus frame - the point is that libopus's own validation rejects it through
        // this wrapper's documented exception rather than through a native crash or a silent
        // wrong answer. This is the boundary the fuzz targets in Core exist to police for this
        // project's own formats; libopus is expected to police itself the same way for its own.
        using OpusVoiceDecoder decoder = new();

        byte[] garbage = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        Should.Throw<InvalidOperationException>(() => decoder.Decode(garbage));
    }

    [Test]
    public void Decode_EmptyFrame_ThrowsRatherThanSilentlyConcealing()
    {
        // The bug this test caught the first time it ran: `fixed` over an empty span yields a
        // null pointer, and opus_decode reads a null data pointer as "conceal a lost packet"
        // regardless of the length passed - `lost_flag = data == NULL`, not `len == 0`, per
        // libopus's own opus_decoder.c. An empty frame would have silently produced concealment
        // audio instead of an error, which is a far worse failure than a crash: it looks like
        // real decoded audio. ArgumentException, not InvalidOperationException, because this is
        // a caller error - pass an empty span and get told to call ConcealLoss instead.
        using OpusVoiceDecoder decoder = new();

        Should.Throw<ArgumentException>(() => decoder.Decode([]));
    }

    [Test]
    public void ConcealLoss_ProducesAudioFromNothing()
    {
        // The property that makes this worth having at all: concealment does not need a frame,
        // only a duration and the decoder's own running state. A freshly constructed decoder has
        // no prior state, so this also confirms concealment does not require having decoded
        // anything first.
        using OpusVoiceDecoder decoder = new();

        // 20ms at 24kHz - a whole multiple of the 2.5ms Opus quantum, and a duration real TF2
        // frames are commonly sent at.
        const int twentyMillisecondsAt24Khz = 480;

        short[] concealed = decoder.ConcealLoss(twentyMillisecondsAt24Khz);

        concealed.Length.ShouldBe(twentyMillisecondsAt24Khz);
    }
}
