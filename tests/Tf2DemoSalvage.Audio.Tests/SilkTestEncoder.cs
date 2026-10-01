using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The SILK SDK's own encoder, for authoring real SILK packets in tests. Test-only: production
/// decodes and never encodes, so these imports live here rather than in <c>NativeSilk</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5393:Use of unsafe DllImportSearchPath value",
    Justification = "Same reasoning as NativeSpeex: AssemblyDirectory is exactly where silk.dll is copied, and is " +
                    "the narrowest search path CA5392 requires in the first place.")]
internal static partial class SilkTestEncoder
{
    private const string Library = "silk";

    /// <summary><c>SKP_SILK_SDK_EncControlStruct</c>, field for field (<c>SKP_Silk_control.h</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EncControl
    {
        public int ApiSampleRate;
        public int MaxInternalSampleRate;
        public int PacketSize;
        public int BitRate;
        public int PacketLossPercentage;
        public int Complexity;
        public int UseInBandFec;
        public int UseDtx;
    }

    /// <summary>Encodes 16 kHz PCM into one SILK packet per <paramref name="packetSamples"/>.</summary>
    public static byte[][] Encode(short[] pcm, int packetSamples)
    {
        Check(GetEncoderSize(out int size), "SKP_Silk_SDK_Get_Encoder_Size");

        nint state = Marshal.AllocHGlobal(size);

        try
        {
            EncControl control = default;
            Check(InitEncoder(state, ref control), "SKP_Silk_SDK_InitEncoder");

            control = new EncControl
            {
                ApiSampleRate = SilkVoiceDecoder.SampleRate,
                MaxInternalSampleRate = SilkVoiceDecoder.SampleRate,
                PacketSize = packetSamples,
                BitRate = 25000,
                Complexity = 2,
            };

            List<byte[]> packets = [];
            byte[] output = new byte[1250];

            for (int at = 0; at + packetSamples <= pcm.Length; at += packetSamples)
            {
                short written = (short)output.Length;
                Check(Encode(state, ref control, pcm.AsSpan(at, packetSamples).ToArray(), packetSamples, output, ref written),
                    "SKP_Silk_SDK_Encode");
                packets.Add(output.AsSpan(0, written).ToArray());
            }

            return [.. packets];
        }
        finally
        {
            Marshal.FreeHGlobal(state);
        }
    }

    private static void Check(int result, string call)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{call} returned {result}.");
        }
    }

    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_Get_Encoder_Size")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static partial int GetEncoderSize(out int size);

    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_InitEncoder")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static partial int InitEncoder(nint state, ref EncControl status);

    [LibraryImport(Library, EntryPoint = "SKP_Silk_SDK_Encode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static partial int Encode(
        nint state, ref EncControl control, short[] samples, int sampleCount, [Out] byte[] output, ref short outputBytes);
}
