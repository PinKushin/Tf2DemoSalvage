using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>Mirrors SILK's <c>SKP_SILK_SDK_DecControlStruct</c> (<c>SKP_Silk_control.h</c>), field for field.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SilkDecControl
{
    /// <summary>I: output sample rate in Hz, 8000 to 24000.</summary>
    internal int ApiSampleRate;

    /// <summary>O: samples per frame.</summary>
    internal int FrameSize;

    /// <summary>O: frames in the packet, 1 to 5.</summary>
    internal int FramesPerPacket;

    /// <summary>O: nonzero while the packet holds frames not yet decoded.</summary>
    internal int MoreInternalDecoderFrames;

    /// <summary>O: distance to the in-band FEC payload, in packets.</summary>
    internal int InBandFecOffset;
}

/// <summary>P/Invoke surface for the SILK SDK 1.0.9 decoder, against the published <c>SKP_Silk_SDK_API.h</c>.</summary>
/// <remarks>
/// The native binary is built from Skype's release archive by <c>tools/native-audio/build.ps1</c>
/// (B441, D201); the SDK has no DLL build of its own, so the script names these exports.
/// </remarks>
[SuppressMessage("Security", "CA5393:Use of unsafe DllImportSearchPath value",
    Justification = "Same reasoning as NativeSpeex: AssemblyDirectory is exactly where the build " +
                    "script and this project's own .csproj place silk.dll, and is the narrowest " +
                    "search path CA5392 requires in the first place.")]
internal static partial class NativeSilk
{
    private const string Library = "silk";
    private const DllImportSearchPath SearchPath = DllImportSearchPath.AssemblyDirectory;

    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_Get_Decoder_Size")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial int GetDecoderSize(out int sizeBytes);

    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_InitDecoder")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static partial int InitDecoder(nint state);

    /// <returns>0 on success, a negative <c>SKP_SILK_DEC_*</c> error otherwise.</returns>
    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_Decode")]
    [DefaultDllImportSearchPaths(SearchPath)]
    internal static unsafe partial int Decode(
        nint state, ref SilkDecControl control, int lostFlag, byte* data, int length, short* samples, ref short sampleCount);
}
