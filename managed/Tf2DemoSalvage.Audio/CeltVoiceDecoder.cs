using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes CELT frames — TF2's voice codec from late 2016 through roughly 2018 — to 16-bit PCM.
/// </summary>
/// <remarks>
/// **The parameters come from TF2's own <c>vaudio_celt.dll</c>, not from guessing.**
/// <c>VoiceEncoder_Celt</c> indexes a table of <c>{ rate, frame size, compressed length }</c>
/// triples and hands the first two to <c>celt_mode_create</c>; the entry whose length is 64 bytes
/// — matching every frame width the corpus measures — is <c>22050 Hz, 512 samples</c>. See
/// <see cref="NativeCelt.FrameSize"/> for the table and how it was read.
///
/// **The library needs two non-default build flags, and B33 cost a long investigation to find
/// the second.** 22050/512 is not among the static modes a default libcelt build compiles in, so
/// this needs <c>CUSTOM_MODES</c> and <c>celt_decoder_create_custom</c>. It also needs
/// <c>ENABLE_POSTFILTER</c>: without it, <c>celt.c</c> does not skip the postfilter fields, it
/// returns <c>CELT_CORRUPTED_DATA</c> outright the moment a frame's postfilter bit is set. That
/// made 56% of perfectly valid corpus frames look corrupt, and looked convincingly like a data
/// problem — byte[1]'s high bit predicted failure with zero exceptions across 1085 frames, which
/// was the postfilter bit's position in the range-coded stream, not a payload-type flag. Both
/// flags are set by <c>tools/native-audio/build.ps1</c>.
///
/// The mode is created once and shared: it is read-only configuration, it is what
/// <c>celt_decoder_create_custom</c> expects to receive by reference, and rebuilding it per
/// speaker would allocate the same tables repeatedly. Decoders themselves stay one-per-speaker,
/// same as <see cref="OpusVoiceDecoder"/> and <see cref="SpeexVoiceDecoder"/>, because CELT
/// frames are delta-coded against the decoder's own running state.
/// </remarks>
public sealed class CeltVoiceDecoder : IDisposable
{
    /// <summary>The rate TF2 encodes voice at, from its own parameter table.</summary>
    public const int SampleRate = NativeCelt.SampleRate;

    /// <summary>Compressed bytes in one frame, from the same table entry.</summary>
    public const int CompressedFrameBytes = NativeCelt.CompressedFrameBytes;

    private const int Channels = 1;

    private static readonly nint SharedMode;

    private readonly nint _decoder;
    private bool _disposed;

    // The resolver must be registered before SharedMode's initializer runs, and field
    // initializers run BEFORE an explicit static constructor's body - so both live here, in this
    // order. The analyzers' "just inline it" advice would silently reintroduce that ordering bug.
    [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline",
        Justification = "Order matters: EnsureRegistered must run before CreateMode's native " +
                        "call, and field initializers run before a static constructor's body.")]
    [SuppressMessage("Minor Code Smell", "S3963:Static field initialization should not use " +
        "user-defined types with elaborate initialization",
        Justification = "Same as the CA1810 suppression: the explicit static constructor is " +
                        "what sequences EnsureRegistered before CreateMode.")]
    static CeltVoiceDecoder()
    {
        NativeLibraryResolver.EnsureRegistered();
        SharedMode = CreateMode();
    }

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libcelt failed to create a decoder.</exception>
    /// <summary>Whether the native celt library is present and usable on this machine.</summary>
    /// <remarks>
    /// **This exists because the library is not committed and is not built everywhere.**
    /// <c>tools/native-audio/build.ps1</c> produces a Windows DLL with the MSVC toolset, so a
    /// Linux box - the measurement box, for instance - has no celt at all.
    ///
    /// Without a way to ask, the corpus voice tests simply throw there, and the consequence is
    /// out of proportion to the cause: Stryker bails its initial test run early, so a handful of
    /// executed tests containing four failures reads as "more than 50% failing tests" and it
    /// refuses to mutate the project at all. Measured on mutation-box 2026-08-12.
    ///
    /// Probed once by actually constructing a decoder, rather than by looking for a file: the
    /// question is whether the P/Invoke resolves, and a file being present does not answer that
    /// on the wrong architecture.
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
            using CeltVoiceDecoder probe = new();
            return true;
        }
        catch (InvalidOperationException)
        {
            // The constructor already translates DllNotFoundException into this, with a message
            // pointing at build.ps1. Absence is the answer here, not an error.
            return false;
        }
    }

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
    /// <param name="frame">
    /// One frame's bytes — <see cref="CompressedFrameBytes"/> of them. Frames arrive bare and
    /// concatenated with no framing of their own; see <c>findings/02-net-messages.md</c>.
    /// </param>
    /// <returns>16-bit PCM, one channel, exactly <see cref="NativeCelt.FrameSize"/> samples.</returns>
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

        // celt_decode's header comment says "@return Error code", which holds only for failure:
        // celt.c returns the decoded sample count on success, the same contract as opus_decode.
        // Checking `!= CELT_OK` treated every successful decode as an error.
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
                $"celt_mode_create({NativeCelt.SampleRate}, {NativeCelt.FrameSize}) failed with " +
                $"error {error}. That mode is not one of the compiled-in static modes, so this " +
                $"needs a CUSTOM_MODES build - see tools/native-audio/build.ps1."));
        }

        return mode;
    }
}
