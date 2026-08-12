using System;
using System.Globalization;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes CELT frames — TF2's voice codec from late 2016 through roughly 2018 — to 16-bit PCM.
/// </summary>
/// <remarks>
/// **B33, corrected.** The earlier version of this class used <c>celt_mode_create(48000, 960)</c>
/// and <c>celt_decoder_create_custom</c>, sourced from every call site in TF2's shipped
/// <c>vaudio_celt.dll</c> pushing those exact constants. That was real data, read from the wrong
/// call site: <c>celt_decoder_create</c> — the plain, non-custom entry point — always builds that
/// same internal 48000/960 mode regardless of the caller's requested rate, so those constants say
/// nothing about what rate TF2 actually decodes voice at. Community documentation states TF2's
/// CELT runs at "22kHz" — matching <c>svc_VoiceInit</c>'s independently measured
/// <c>22050</c> — but 22050 is not one of the five rates this build's <c>resampling_factor</c>
/// table supports (only 8000/12000/16000/24000/48000). <b>24000</b> is the closest supported rate
/// and is used here provisionally; see `docs/RISKS.md` B33 for the corpus test that is the actual
/// arbiter of whether this is right.
///
/// **Must use the plain <c>celt_decoder_create(rate, ...)</c>, not <c>_create_custom</c>.** Only
/// the plain path computes a downsample factor at all —
/// <c>st-&gt;downsample = resampling_factor(sampling_rate)</c> — and <c>_create_custom</c> always
/// leaves it at 1, silently decoding at the mode's native rate no matter what was asked for.
///
/// **<c>frame_size</c> is in output-rate samples, not the mode's native rate.** Read directly from
/// <c>celt.c</c>: the argument is multiplied by the downsample factor internally
/// (<c>frame_size *= st-&gt;downsample</c>) to reach the native 960, and the return value is the
/// original output-rate count. At 24000 Hz (downsample 2) that is 480 for a 20 ms frame — this
/// is why <see cref="NativeCelt.FrameSize"/> is 480, not 960.
///
/// One <see cref="CeltVoiceDecoder"/> per speaker, same reasoning as
/// <see cref="OpusVoiceDecoder"/> and <see cref="SpeexVoiceDecoder"/>: CELT frames are delta-coded
/// against the decoder's own running state.
/// </remarks>
public sealed class CeltVoiceDecoder : IDisposable
{
    /// <summary>The rate under test for B33 — see the remarks above.</summary>
    public const int SampleRate = NativeCelt.SampleRate;

    private const int Channels = 1;

    private readonly nint _decoder;
    private bool _disposed;

    static CeltVoiceDecoder() => NativeLibraryResolver.EnsureRegistered();

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libcelt failed to create a decoder.</exception>
    public CeltVoiceDecoder()
    {
        nint decoder;
        int error;

        try
        {
            decoder = NativeCelt.DecoderCreate(NativeCelt.SampleRate, Channels, out error);
        }
        catch (DllNotFoundException missing)
        {
            throw new InvalidOperationException(
                "celt.dll was not found next to this assembly. Run " +
                "tools/native-audio/build.ps1 to build it from source - see that " +
                "directory's README.md.",
                missing);
        }

        if (error != NativeCelt.Ok || decoder == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"celt_decoder_create failed with error {error}."));
        }

        _decoder = decoder;
    }

    /// <summary>Decodes one CELT frame.</summary>
    /// <param name="frame">The frame's bytes — bare, concatenated frames with no framing of
    /// their own; see <c>findings/02-net-messages.md</c>.</param>
    /// <returns>16-bit PCM samples, one channel, always exactly <see cref="NativeCelt.FrameSize"/>
    /// long — unlike Opus, CELT has no variable frame duration within one mode.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">libcelt rejected the frame.</exception>
    public unsafe short[] Decode(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.IsEmpty)
        {
            throw new ArgumentException("A CELT frame cannot be empty.", nameof(frame));
        }

        short[] pcm = new short[NativeCelt.FrameSize];

        int result;
        fixed (byte* dataPointer = frame)
        fixed (short* pcmPointer = pcm)
        {
            result = NativeCelt.Decode(
                _decoder, dataPointer, frame.Length, pcmPointer, NativeCelt.FrameSize);
        }

        // celt_decode's header comment says "@return Error code", which is wrong for the
        // success path: celt.c returns the output-rate sample count decoded, the same shape as
        // opus_decode - only a NEGATIVE value is actually an error.
        if (result < 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"celt_decode failed with error {result} on a {frame.Length}-byte frame."));
        }

        return pcm;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeCelt.DecoderDestroy(_decoder);
        _disposed = true;
    }
}
