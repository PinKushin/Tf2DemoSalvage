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

        // **Type initialization must not throw**, which is a stronger requirement than it looks.
        // The CLR wraps whatever a static constructor throws in TypeInitializationException and
        // raises it at whatever first TOUCHED the type - so a caller merely asking
        // `IsAvailable` got an exception instead of an answer, and no catch inside this class
        // could help because the class was never usable.
        //
        // Seen on mutation-box: Speex skipped correctly and CELT failed, and the only difference
        // between them is that CELT builds its mode during type initialization. The consequence
        // was out of all proportion - two erroring tests made Stryker abandon the whole project
        // with "more than 50% failing tests".
        //
        // So the failure is CAPTURED rather than thrown, and re-raised from the constructor,
        // which is the operation that genuinely cannot proceed without a mode. Asking whether
        // the library exists is not that operation.
        try
        {
            SharedMode = CreateMode();
        }
        catch (InvalidOperationException failure)
        {
            ModeFailure = failure;
            SharedMode = 0;
        }
    }

    /// <summary>Why the shared mode could not be built, or <c>null</c> if it was.</summary>
    private static readonly InvalidOperationException? ModeFailure;

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">libcelt failed to create a decoder.</exception>
    /// <summary>Whether the native celt library is present and usable on this machine.</summary>
    /// <remarks>
    /// **This must be answerable without throwing.** celt is built by
    /// <c>tools/native-audio/build.ps1</c> with MSVC and is not committed, so a Linux box has
    /// none - and a caller asking whether it exists should get <c>false</c>, not an exception.
    ///
    /// The stakes are higher than one test: two erroring tests were enough for Stryker to abandon
    /// mutation of the entire corpus project with "Initial testrun has more than 50% failing
    /// tests", because it bails its initial run early and a handful of executed tests then
    /// contains a majority of failures.
    /// </remarks>
    public static bool IsAvailable => ModeFailure is null && SharedMode != 0;

    public CeltVoiceDecoder()
    {
        // Re-raised here rather than from the type initializer. Constructing a decoder is the
        // operation that genuinely cannot proceed without a mode, so this is where the informative
        // message about build.ps1 belongs - and it stays exactly as informative as before.
        if (ModeFailure is not null)
        {
            throw new InvalidOperationException(ModeFailure.Message, ModeFailure);
        }

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
