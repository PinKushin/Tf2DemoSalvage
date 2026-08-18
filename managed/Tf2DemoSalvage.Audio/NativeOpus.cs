using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// P/Invoke surface for libopus's decoder, against the published <c>opus.h</c>/<c>opus_defines.h</c>.
/// </summary>
/// <remarks>
/// The whole decode API is four functions, so this project binds them directly rather than
/// depending on a managed wrapper — one less layer between this project and the bits the engine
/// actually produced, and it keeps the P/Invoke boundary visible rather than hidden inside a
/// third-party abstraction. Same reasoning as D20's choice of thin Direct3D bindings over an
/// engine like Veldrid: keep the layer thin where correctness and performance both live.
///
/// The native binary comes from the <c>libopus</c> NuGet package (MIT, prebuilt per-RID), not
/// from anything downloaded or built by this project.
/// </remarks>
[SuppressMessage("Security", "CA5393:Use of unsafe DllImportSearchPath value",
    Justification = "AssemblyDirectory is exactly where the libopus NuGet package's build " +
                    "targets copy the platform-specific binary, and this project deploys the " +
                    "binary alongside the assembly rather than beside a separately-installed " +
                    "shared copy. CA5393's concern is a wider or more permissive search path " +
                    "(the process's current directory, or the OS default order) picking up an " +
                    "attacker-planted DLL; AssemblyDirectory is the narrowest path available " +
                    "and is what CA5392 above requires in the first place.")]
internal static partial class NativeOpus
{
    /// <summary>libopus's error codes, from <c>opus_defines.h</c>.</summary>
    internal const int Ok = 0;

    private const string Library = "opus";

    // Restricts native resolution to the assembly's own directory - where the libopus NuGet
    // package's build targets copy the platform-specific binary - rather than the wider default
    // search order, which includes the process's working directory. CA5392, on every entry
    // point rather than once at assembly level so each stays correct if one is ever moved.
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    [LibraryImport(Library, EntryPoint = "opus_decoder_create")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint DecoderCreate(int sampleRate, int channels, out int error);

    [LibraryImport(Library, EntryPoint = "opus_decoder_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void DecoderDestroy(nint decoder);

    /// <summary>
    /// How many frames a packet declares, or a negative error code when it is malformed.
    /// </summary>
    /// <remarks>
    /// **Packet inspection exists so a caller can refuse a packet WITHOUT decoding it**, and this
    /// project needs it because <c>opus_decode</c> is not safe to call on arbitrary bytes with the
    /// library built as it is shipped. See <see cref="OpusVoiceDecoder.Decode"/> and RISKS B114.
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "opus_packet_get_nb_frames")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int PacketGetFrameCount(byte* packet, int length);

    /// <summary>Samples per frame for a packet, from its TOC byte and the sample rate.</summary>
    [LibraryImport(Library, EntryPoint = "opus_packet_get_samples_per_frame")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int PacketGetSamplesPerFrame(byte* packet, int sampleRate);

    /// <summary>
    /// Decodes one Opus packet to 16-bit PCM.
    /// </summary>
    /// <returns>Samples per channel decoded, or a negative <c>opus_errorcodes</c> value.</returns>
    /// <remarks>
    /// <paramref name="data"/> takes a raw pointer rather than <c>ReadOnlySpan&lt;byte&gt;</c>
    /// because <c>LibraryImport</c> cannot marshal a span used through <c>ref</c> reliably across
    /// packet-loss calls (a null <paramref name="data"/> with <paramref name="dataLength"/> zero
    /// means "conceal a lost packet", which a span parameter cannot express as cleanly as a
    /// pointer that may be <see cref="nint.Zero"/>).
    /// </remarks>
    [LibraryImport(Library, EntryPoint = "opus_decode")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int Decode(
        nint decoder,
        byte* data,
        int dataLength,
        short* pcm,
        int frameSize,
        int decodeForwardErrorCorrection);
}
