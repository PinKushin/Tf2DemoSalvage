using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes narrowband Speex frames — TF2's voice codec from launch through 2016 — to 16-bit PCM.
/// </summary>
/// <remarks>
/// Narrowband only, matching what the corpus measures: every pre-2016 demo's <c>svc_VoiceInit</c>
/// reports quality 5, and a 28-byte frame is 224 bits — Speex narrowband quality 5's exact,
/// byte-aligned size (<c>docs/findings/02-net-messages.md</c>). TF2 never used Speex's wideband or
/// ultra-wideband modes for voice.
///
/// One decoder per speaker, same reasoning as <see cref="OpusVoiceDecoder"/>: Speex frames are
/// delta-coded against the decoder's running state, so sharing one decoder across speakers
/// desynchronises the moment two speakers' packets interleave.
/// </remarks>
[SuppressMessage("Design", "CA2216:Disposable types should declare finalizer",
    Justification = "No finalizer because there is nothing a finalizer could safely do: the " +
                    "handle is only ever valid on the thread that created it, per libspeex's " +
                    "own contract, and a finalizer runs on a GC thread with no guarantee the " +
                    "native call is even safe there. Deterministic disposal via `using` is the " +
                    "documented lifetime for this type, matching OpusVoiceDecoder and " +
                    "CeltVoiceDecoder.")]
public sealed class SpeexVoiceDecoder : IDisposable
{
    /// <summary>Speex narrowband's fixed sample rate.</summary>
    public const int SampleRate = 8000;

    private readonly nint _state;
    private readonly int _frameSize;
    private bool _disposed;

    static SpeexVoiceDecoder() => NativeLibraryResolver.EnsureRegistered();

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libspeex failed to create a decoder state.</exception>
    /// <summary>Whether the native speex library is present and usable on this machine.</summary>
    /// <remarks>
    /// See <see cref="CeltVoiceDecoder.IsAvailable"/> - same reasoning. The library is built by
    /// <c>tools/native-audio/build.ps1</c> with MSVC and is not committed, so a Linux measurement
    /// box has none, and a throwing test there costs far more than the test: Stryker bails its
    /// initial run early, so a few failures read as "more than 50% failing tests" and it declines
    /// to mutate the project at all.
    /// </remarks>
    /// <remarks>
    /// **Lazy on first access, not a static field initializer, and that is load-bearing.** A
    /// static field initializer is part of type initialization and runs BEFORE the explicit
    /// static constructor body - which is where <c>NativeLibraryResolver.EnsureRegistered()</c>
    /// lives. Probing first therefore resolved the P/Invoke with no resolver registered and took
    /// the whole test host down with an access violation (exit -1073741819), reported by xUnit as
    /// "Catastrophic failure" with zero tests discovered.
    /// </remarks>
    public static bool IsAvailable => _available ??= Probe();

    private static bool? _available;

    private static bool Probe()
    {
        try
        {
            using SpeexVoiceDecoder probe = new();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public unsafe SpeexVoiceDecoder()
    {
        nint mode;

        try
        {
            mode = NativeSpeex.LibGetMode(NativeSpeex.NarrowbandMode);
        }
        catch (DllNotFoundException missing)
        {
            throw new InvalidOperationException(
                "speex.dll was not found next to this assembly. Run " +
                "tools/native-audio/build.ps1 to build it from source - see that " +
                "directory's README.md.",
                missing);
        }

        _state = NativeSpeex.DecoderInit(mode);

        if (_state == 0)
        {
            throw new InvalidOperationException("speex_decoder_init returned no decoder state.");
        }

        int frameSize = 0;
        int ctlResult = NativeSpeex.DecoderCtl(_state, NativeSpeex.GetFrameSize, &frameSize);

        if (ctlResult != 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"speex_decoder_ctl(SPEEX_GET_FRAME_SIZE) failed with result {ctlResult}."));
        }

        _frameSize = frameSize;
    }

    /// <summary>Decodes one Speex frame.</summary>
    /// <param name="frame">The frame's bytes — a whole number of them, concatenated with no
    /// framing of its own; see <c>findings/02-net-messages.md</c>.</param>
    /// <returns>16-bit PCM samples, one channel, at <see cref="SampleRate"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">libspeex rejected the frame.</exception>
    /// <remarks>
    /// Unlike <see cref="OpusVoiceDecoder.Decode"/>, an empty <paramref name="frame"/> is refused
    /// for a more ordinary reason: <c>speex_bits_read_from</c> takes no null-means-loss
    /// convention, so an empty frame is simply not a frame, not a special native code path to
    /// guard against.
    /// </remarks>
    public unsafe short[] Decode(ReadOnlySpan<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.IsEmpty)
        {
            throw new ArgumentException("A Speex frame cannot be empty.", nameof(frame));
        }

        SpeexBits bits = default;
        NativeSpeex.BitsInit(ref bits);

        try
        {
            fixed (byte* dataPointer = frame)
            {
                NativeSpeex.BitsReadFrom(ref bits, dataPointer, frame.Length);
            }

            short[] pcm = new short[_frameSize];

            fixed (short* pcmPointer = pcm)
            {
                int result = NativeSpeex.DecodeInt(_state, ref bits, pcmPointer);

                if (result != 0)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"speex_decode_int returned {result} for a {frame.Length}-byte frame."));
                }
            }

            return pcm;
        }
        finally
        {
            NativeSpeex.BitsDestroy(ref bits);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeSpeex.DecoderDestroy(_state);
        _disposed = true;
    }
}
