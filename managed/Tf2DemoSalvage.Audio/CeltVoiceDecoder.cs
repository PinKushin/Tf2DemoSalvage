using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes CELT frames — TF2's voice codec from late 2016 through roughly 2018 — to 16-bit PCM.
/// </summary>
/// <remarks>
/// The mode is TF2's own: <c>celt_mode_create(48000, 960, ...)</c>, the values every call site in
/// the shipped <c>vaudio_celt.dll</c> uses (see <see cref="NativeCelt"/>). That mode is shared
/// across every instance of this class rather than rebuilt per speaker, because
/// <c>celt_mode_create</c> without <c>CUSTOM_MODES</c> just returns the same static built-in mode
/// every time — building it once avoids nothing but redundant calls, and sharing the read-only
/// mode object across decoders is exactly what CELT's own API expects
/// (<c>celt_decoder_create_custom(mode, channels, ...)</c> takes it by reference, not by value).
///
/// One <see cref="CeltVoiceDecoder"/> per speaker, same reasoning as
/// <see cref="OpusVoiceDecoder"/> and <see cref="SpeexVoiceDecoder"/>: CELT frames are delta-coded
/// against the decoder's own running state.
/// </remarks>
public sealed class CeltVoiceDecoder : IDisposable
{
    /// <summary>CELT's fixed processing rate for TF2's mode.</summary>
    public const int SampleRate = NativeCelt.SampleRate;

    private const int Channels = 1;

    private static readonly nint SharedMode;

    private readonly nint _decoder;
    private bool _disposed;

    // The resolver must be registered before SharedMode's initializer runs. A static
    // constructor's body executes AFTER field initializers, not before, so registering there
    // (as OpusVoiceDecoder and SpeexVoiceDecoder both do) would be too late here specifically -
    // CreateMode's native call would run first. Doing both in one explicit static constructor,
    // in this exact order, is what makes the sequencing correct - the analyzer's "inline
    // everything" suggestion would silently reintroduce the ordering bug this was written to fix.
    [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline",
        Justification = "Order matters here and inlining would break it: EnsureRegistered must " +
                        "run before CreateMode's native call, and field initializers run before " +
                        "an explicit static constructor's body, not after.")]
    [SuppressMessage("Minor Code Smell", "S3963:Static field initialization should not use " +
        "user-defined types with elaborate initialization",
        Justification = "Same reason as the CA1810 suppression above: the explicit static " +
                        "constructor exists specifically to sequence EnsureRegistered before " +
                        "CreateMode, which inlining would undo.")]
    static CeltVoiceDecoder()
    {
        NativeLibraryResolver.EnsureRegistered();
        SharedMode = CreateMode();
    }

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libcelt failed to create a decoder.</exception>
    public CeltVoiceDecoder()
    {
        _decoder = NativeCelt.DecoderCreateCustom(SharedMode, Channels, out int error);

        if (error != NativeCelt.Ok || _decoder == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"celt_decoder_create_custom failed with error {error}."));
        }
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
        // success path: celt.c returns `frame_size / st->downsample` samples decoded, the same
        // shape as opus_decode - only a NEGATIVE value is actually an error. Trusting the
        // docstring here treated every successful decode as a failure; caught immediately by
        // the corpus test, which is real CELT audio and therefore could not silently agree with
        // a wrong check the way a hand-built fixture might.
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

    private static nint CreateMode()
    {
        nint mode;
        int error;

        try
        {
            mode = NativeCelt.ModeCreate(NativeCelt.SampleRate, NativeCelt.FrameSize, out error);
        }
        catch (DllNotFoundException missing)
        {
            throw new InvalidOperationException(
                "celt.dll was not found next to this assembly. Run " +
                "tools/native-audio/build.ps1 to build it from source - see that " +
                "directory's README.md.",
                missing);
        }

        if (error != NativeCelt.Ok || mode == 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"celt_mode_create failed with error {error}."));
        }

        return mode;
    }
}
