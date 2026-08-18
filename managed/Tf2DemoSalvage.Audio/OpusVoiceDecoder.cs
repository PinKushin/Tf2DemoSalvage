using System;
using System.Globalization;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes the Opus frames a <c>steam</c>-codec voice packet carries into 16-bit PCM.
/// </summary>
/// <remarks>
/// One decoder per speaker, not one per packet or one shared across speakers. Opus is stateful —
/// each packet is delta-coded against the decoder's running state, the same reason
/// <see cref="Tf2DemoSalvage.Core.Container.UserCommand"/>'s baseline mattered — so feeding two
/// speakers' frames through one decoder desynchronises both the moment they interleave, which a
/// multi-speaker voice channel does constantly.
///
/// Sample rate is fixed at 24000 Hz because that is the only rate <c>svc_VoiceInit</c> has ever
/// been measured reporting for the <c>steam</c> codec across the corpus (see
/// <c>findings/02-net-messages.md</c>), and mono because voice chat carries one speaker.
/// </remarks>
public sealed class OpusVoiceDecoder : IDisposable
{
    /// <summary>The only sample rate measured on the corpus for <c>steam</c>-codec voice.</summary>
    public const int SampleRate = 24000;

    private const int Channels = 1;

    /// <summary>
    /// Largest frame Opus can produce: 120 ms at <see cref="SampleRate"/>. The decode buffer is
    /// sized to this regardless of the packet, because <c>opus_decode</c> needs the space
    /// available up front rather than the space the packet will turn out to need.
    /// </summary>
    private const int MaxFrameSamples = SampleRate * 120 / 1000;

    private readonly nint _decoder;
    private bool _disposed;

    static OpusVoiceDecoder() => NativeLibraryResolver.EnsureRegistered();

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libopus failed to allocate a decoder.</exception>
    public OpusVoiceDecoder()
    {
        _decoder = NativeOpus.DecoderCreate(SampleRate, Channels, out int error);

        if (error != NativeOpus.Ok || _decoder == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"opus_decoder_create failed with error {error}."));
        }
    }

    /// <summary>Decodes one Opus frame.</summary>
    /// <param name="frame">The frame's bytes, as <see cref="Core.Net.VoiceChunk"/> carries them.</param>
    /// <returns>16-bit PCM samples, one channel, at <see cref="SampleRate"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="InvalidOperationException">libopus rejected the frame.</exception>
    public unsafe short[] Decode(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // **A genuinely dangerous shape, found by testing rather than reasoning about it.**
        // `fixed` over an empty ReadOnlySpan<byte> yields a null pointer, and opus_decode reads
        // a null data pointer as "this packet was lost, conceal it" regardless of the length
        // passed - `lost_flag = data == NULL`, not `len == 0`, per opus_decoder.c. Call this with
        // an empty frame and libopus would silently return concealment audio instead of
        // rejecting a malformed frame. Loss concealment already has its own explicit entry point
        // below; this one refuses rather than falling into that path by accident.
        if (frame.IsEmpty)
        {
            throw new ArgumentException(
                "An empty frame is indistinguishable from a lost packet to libopus once " +
                "pinned; call ConcealLoss for that case instead of decoding nothing.",
                nameof(frame));
        }

        Span<short> pcm = stackalloc short[MaxFrameSamples];

        int samples;
        fixed (byte* dataPointer = frame)
        fixed (short* pcmPointer = pcm)
        {
            // **Precautionary, and the comment that used to be here overstated it.** It claimed
            // this guard fixed a crash the voice fuzz target had found — libopus aborting on
            // `assertion failed: ret==packet_frame_size` — and that was wrong. The abort was real
            // but the cause was the FUZZ HARNESS sharing one decoder across NUnit's parallel
            // fixtures, and these codecs are not thread-safe. With the harness corrected the same
            // inputs decode fine with this guard disabled, measured both ways. See RISKS B114.
            //
            // Kept anyway, because it is defensible on its own terms rather than as a fix: a voice
            // frame arrives inside `svc_VoiceData`, so its bytes are chosen by whoever supplies the
            // demo, and asking libopus about a packet before asking it to decode is what the
            // inspection entry points are for. The oversize check in particular is a real property
            // — a packet can be well-formed and still declare more audio than the buffer holds.
            //
            // What it is NOT is evidence that `opus_decode` mishandles malformed input. Nothing
            // here has shown that, and this comment should not be read as claiming it.
            int frameCount = NativeOpus.PacketGetFrameCount(dataPointer, frame.Length);
            int samplesPerFrame = NativeOpus.PacketGetSamplesPerFrame(dataPointer, SampleRate);

            if (frameCount <= 0 || samplesPerFrame <= 0)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {frame.Length}-byte frame is not a valid Opus packet: " +
                    $"opus_packet_get_nb_frames returned {frameCount} and " +
                    $"opus_packet_get_samples_per_frame returned {samplesPerFrame}."));
            }

            // Checked as well as the framing, because a packet can be well-formed and still
            // declare more audio than the buffer holds — 120 ms is the most Opus can produce, so
            // anything beyond it would have overrun a buffer sized to that.
            if ((long)frameCount * samplesPerFrame > MaxFrameSamples)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {frame.Length}-byte frame declares {frameCount} frames of " +
                    $"{samplesPerFrame} samples, beyond the {MaxFrameSamples}-sample maximum."));
            }

            samples = NativeOpus.Decode(
                _decoder, dataPointer, frame.Length, pcmPointer, MaxFrameSamples, 0);
        }

        if (samples < 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"opus_decode failed with error {samples} on a {frame.Length}-byte frame."));
        }

        return pcm[..samples].ToArray();
    }

    /// <summary>Conceals one frame of lost audio using the decoder's own state.</summary>
    /// <param name="lostFrameSamples">
    /// Duration of the gap, in samples at <see cref="SampleRate"/>. Must be a multiple of the
    /// 2.5 ms Opus frame quantum.
    /// </param>
    /// <returns>Concealment audio the same length as the gap.</returns>
    /// <remarks>
    /// A dropped voice packet is not silence — Opus's packet-loss concealment extrapolates from
    /// the decoder's state, which sounds far closer to what was actually said than a gap of
    /// zeros. <c>opus_decode</c> documents this as passing a null data pointer.
    /// </remarks>
    public unsafe short[] ConcealLoss(int lostFrameSamples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<short> pcm = stackalloc short[MaxFrameSamples];

        int samples;
        fixed (short* pcmPointer = pcm)
        {
            samples = NativeOpus.Decode(_decoder, null, 0, pcmPointer, lostFrameSamples, 0);
        }

        if (samples < 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"opus_decode (loss concealment) failed with error {samples}."));
        }

        return pcm[..samples].ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeOpus.DecoderDestroy(_decoder);
        _disposed = true;
    }
}
