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
    /// CELT's native rate, and the one TF2's own <c>vaudio_celt.dll</c> builds its mode at.
    /// </summary>
    /// <remarks>
    /// **Measured irrelevant to decode success, which is itself the finding.** B33 swept all five
    /// rates this build supports (8000/12000/16000/24000/48000, each with its matching 20 ms
    /// frame size) across 200 real corpus packets and got a byte-identical outcome every time —
    /// 103 frames accepted, 163 rejected. So whatever is wrong with CELT decoding here, the
    /// sample rate is not it, and picking a rate on the strength of decode success would be
    /// reading signal into a constant. This is set to the value TF2's binary actually uses,
    /// which is the only defensible basis available.
    /// </remarks>
    internal const int SampleRate = 48000;

    /// <summary>
    /// Frame size in output-rate samples, matching <see cref="SampleRate"/>'s 20 ms frame.
    /// </summary>
    /// <remarks>
    /// The argument is in <em>output</em>-rate samples: <c>celt.c</c> multiplies it by the
    /// decoder's downsample factor internally (<c>frame_size *= st-&gt;downsample</c>) to reach
    /// the mode's native 960, and returns the output-rate count. At the native 48000 the
    /// downsample factor is 1, so the two coincide here.
    /// </remarks>
    internal const int FrameSize = 960;

    /// <summary>libcelt's <c>CELT_OK</c>.</summary>
    internal const int Ok = 0;

    private const string Library = "celt";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    /// <summary>
    /// The plain (non-custom) entry point — required rather than
    /// <c>celt_decoder_create_custom</c> because only this path sets a downsample factor.
    /// <c>_create_custom</c> always assumes the mode's native rate and leaves downsample at 1.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "celt_decoder_create")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint DecoderCreate(int samplingRate, int channels, out int error);

    [LibraryImport(Library, EntryPoint = "celt_decoder_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void DecoderDestroy(nint decoder);

    /// <returns>libcelt's error code; <see cref="Ok"/> on success.</returns>
    [LibraryImport(Library, EntryPoint = "celt_decode")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int Decode(
        nint decoder, byte* data, int length, short* pcm, int frameSize);
}
