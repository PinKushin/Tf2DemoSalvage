using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// P/Invoke surface for libcelt 0.11.3's decoder, against the published <c>celt.h</c>.
/// </summary>
/// <remarks>
/// The native binary is built from source by <c>tools/native-audio/build.ps1</c> — see that
/// directory's README for why 0.11.3 is exact rather than "the latest CELT" (no such thing
/// exists as a standalone release; development moved into Opus's <c>celt/</c> subdirectory and
/// the bitstream diverged further).
///
/// **The mode parameters are two integers, sourced from TF2's own shipped
/// <c>vaudio_celt.dll</c>, not guessed.** All seven call sites to <c>celt_mode_create</c> in that
/// binary push the identical constants — <c>celt_mode_create(48000, 960, NULL)</c> — found by
/// scanning the binary for call sites and reading the immediate push values ahead of them, the
/// same technique this project used to recover the user-message registration order from six
/// shipped clients. Nothing beyond those two integers was extracted or is reproduced here.
///
/// <c>48000</c>/<c>960</c> is CELT's own built-in default mode — the only one this library
/// compiles when <c>CUSTOM_MODES</c> is off, matching upstream's default configuration — so no
/// custom-mode parameters were ever needed. The corpus's measured 22050 in <c>svc_VoiceInit</c>
/// is therefore *not* CELT's internal processing rate; what it actually denotes is still open
/// (see <c>docs/RISKS.md</c>).
/// </remarks>
[SuppressMessage("Security", "CA5393:Use of unsafe DllImportSearchPath value",
    Justification = "Same reasoning as NativeOpus/NativeSpeex: AssemblyDirectory is exactly " +
                    "where the build script and this project's own .csproj place celt.dll.")]
internal static partial class NativeCelt
{
    /// <summary>
    /// Sample rate, from TF2's own parameter table — see <see cref="FrameSize"/>.
    /// </summary>
    internal const int SampleRate = 22050;

    /// <summary>
    /// Frame size in samples, from TF2's own parameter table.
    /// </summary>
    /// <remarks>
    /// **Read out of <c>vaudio_celt.dll</c>, and this is what B33 was missing.**
    /// <c>VoiceEncoder_Celt</c> does not hardcode its parameters: it indexes a table of
    /// <c>{ sample rate, frame size, compressed length }</c> triples at RVA <c>0x2f00c</c> by a
    /// quality field, and passes the first two straight to <c>celt_mode_create</c> and the third
    /// to <c>celt_decode</c> as the frame length. The table, read from the binary:
    ///
    /// | idx | rate | frame size | bytes |
    /// |---|---|---|---|
    /// | 0 | 44100 | 256 | 120 |
    /// | 1 | 22050 | 120 | 60 |
    /// | 2 | 22050 | 256 | 60 |
    /// | **3** | **22050** | **512** | **64** |
    /// | 4 | 44100 | 1024 | 128 |
    ///
    /// Entry 3's 64-byte length is exactly the frame width the corpus measures, so that is the
    /// entry TF2 used for these recordings. **22050 Hz at 512 samples is not one of the static
    /// modes** a default libcelt build compiles in — those are 48000 Hz only — which is why
    /// every earlier attempt failed identically regardless of which standard rate was tried:
    /// <c>celt_mode_create</c> was rejecting the mode before a single frame was ever decoded.
    /// The build now enables <c>CUSTOM_MODES</c> so this mode can actually be constructed.
    ///
    /// This also explains the "22 kHz" in community documentation and the <c>22050</c> the
    /// project had been carrying: both are real, and neither was ever the problem.
    /// </remarks>
    internal const int FrameSize = 512;

    /// <summary>Compressed bytes per frame, from the same table entry.</summary>
    internal const int CompressedFrameBytes = 64;

    /// <summary>libcelt's <c>CELT_OK</c>.</summary>
    internal const int Ok = 0;

    private const string Library = "celt";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    /// <summary>
    /// Builds a mode from an explicit rate and frame size. Requires a <c>CUSTOM_MODES</c> build
    /// for anything but the compiled-in static modes — see <see cref="FrameSize"/>.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "celt_mode_create")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint ModeCreate(int samplingRate, int frameSize, out int error);

    /// <summary>Frees a mode built by <see cref="ModeCreate"/>.</summary>
    [LibraryImport(Library, EntryPoint = "celt_mode_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void ModeDestroy(nint mode);

    /// <summary>
    /// The custom-mode decoder constructor, matching what <c>VoiceEncoder_Celt</c> itself calls.
    /// </summary>
    /// <remarks>
    /// The plain <c>celt_decoder_create(rate, ...)</c> cannot express this configuration: it
    /// always builds the internal 48000/960 mode and only varies a downsample factor drawn from
    /// a fixed five-entry table that does not contain 22050.
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "celt_decoder_create_custom")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint DecoderCreateCustom(nint mode, int channels, out int error);

    [LibraryImport(Library, EntryPoint = "celt_decoder_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void DecoderDestroy(nint decoder);

    /// <returns>libcelt's error code; <see cref="Ok"/> on success.</returns>
    [LibraryImport(Library, EntryPoint = "celt_decode")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int Decode(
        nint decoder, byte* data, int length, short* pcm, int frameSize);
}
