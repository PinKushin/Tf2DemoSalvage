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
    /// <summary>Sample rate every call site in TF2's own <c>vaudio_celt.dll</c> uses.</summary>
    internal const int SampleRate = 48000;

    /// <summary>Frame size (samples) every call site in TF2's own <c>vaudio_celt.dll</c> uses.</summary>
    internal const int FrameSize = 960;

    /// <summary>libcelt's <c>CELT_OK</c>.</summary>
    internal const int Ok = 0;

    private const string Library = "celt";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    [LibraryImport(Library, EntryPoint = "celt_mode_create")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint ModeCreate(int samplingRate, int frameSize, out int error);

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
