using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>Mirrors Speex's <c>SpeexBits</c>, byte-for-byte, for direct marshalling.</summary>
/// <remarks>
/// Fields, types and order taken from the published <c>speex_bits.h</c>. Layout on x64: an
/// 8-byte pointer, six 4-byte ints, four bytes of padding to re-align the trailing pointer, then
/// an 8-byte pointer — 48 bytes total, which <see cref="LayoutKind.Sequential"/> reproduces
/// automatically because the field order and types match the C struct exactly.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct SpeexBits
{
    internal nint Chars;
    internal int NumberOfBits;
    internal int CharPointer;
    internal int BitPointer;
    internal int Owner;
    internal int Overflow;
    internal int BufferSize;
    internal int Reserved1;
    internal nint Reserved2;
}

/// <summary>
/// P/Invoke surface for libspeex's decoder, against the published <c>speex.h</c>/<c>speex_bits.h</c>.
/// </summary>
/// <remarks>
/// The native binary is built from source by <c>tools/native-audio/build.ps1</c> — see that
/// directory's README for why Speex has no NuGet native-asset package the way libopus does, and
/// why the exact version is 1.2.1 rather than an older, period-matched release (Speex's bitstream
/// has stayed stable across the whole 1.2.x line, unlike CELT's).
///
/// <c>speex_lib_get_mode(SPEEX_MODEID_NB)</c> is a header macro that short-circuits to the
/// <c>speex_nb_mode</c> data symbol directly for C callers — a symbol this build's <c>.def</c>
/// file does not export, since it was never meant to cross a DLL boundary that way. P/Invoke
/// never sees the macro; it calls the real exported <c>speex_lib_get_mode</c> function, which
/// resolves the same mode without needing that data symbol at all.
/// </remarks>
[SuppressMessage("Security", "CA5393:Use of unsafe DllImportSearchPath value",
    Justification = "Same reasoning as NativeOpus: AssemblyDirectory is exactly where the " +
                    "build script and this project's own .csproj place speex.dll, and is the " +
                    "narrowest search path CA5392 (below) requires in the first place.")]
internal static partial class NativeSpeex
{
    /// <summary>Selects the narrowband mode — the only one TF2's voice ever used.</summary>
    internal const int NarrowbandMode = 0;

    /// <summary><c>speex_decoder_ctl</c> request to read the mode's frame size.</summary>
    internal const int GetFrameSize = 3;

    private const string Library = "speex";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    [LibraryImport(Library, EntryPoint = "speex_lib_get_mode")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint LibGetMode(int modeId);

    [LibraryImport(Library, EntryPoint = "speex_decoder_init")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial nint DecoderInit(nint mode);

    [LibraryImport(Library, EntryPoint = "speex_decoder_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void DecoderDestroy(nint state);

    [LibraryImport(Library, EntryPoint = "speex_decoder_ctl")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int DecoderCtl(nint state, int request, int* value);

    [LibraryImport(Library, EntryPoint = "speex_bits_init")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void BitsInit(ref SpeexBits bits);

    [LibraryImport(Library, EntryPoint = "speex_bits_destroy")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial void BitsDestroy(ref SpeexBits bits);

    [LibraryImport(Library, EntryPoint = "speex_bits_read_from")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial void BitsReadFrom(ref SpeexBits bits, byte* bytes, int length);

    /// <returns>0 on success, negative on error, 1 to signal end-of-stream (no more frames).</returns>
    [LibraryImport(Library, EntryPoint = "speex_decode_int")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int DecodeInt(nint state, ref SpeexBits bits, short* pcm);
}
