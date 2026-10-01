using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// Decodes SILK — the codec inside Steam Voice from 2011 to 2016 — to 16-bit PCM at 16 kHz.
/// </summary>
/// <remarks>
/// **These demos announce <c>vaudio_speex</c> and carry no Speex at all** (B441): the engine hands
/// the whole payload to Steam, whose packet <c>SteamVoicePayload</c> unwraps into SILK frames. Each
/// frame is one <c>SKP_Silk_SDK_Decode</c> packet of one to five 20 ms frames, decoded the way the
/// SDK's own <c>test/Decoder.c</c> does: call until <c>moreInternalDecoderFrames</c> is clear.
///
/// One decoder per speaker, same reasoning as <see cref="SpeexVoiceDecoder"/>: SILK predicts from
/// the previous frame, so interleaving two speakers through one state desynchronises both.
/// </remarks>
[SuppressMessage("Design", "CA2216:Disposable types should declare finalizer",
    Justification = "Same lifetime contract as SpeexVoiceDecoder: deterministic disposal via `using`.")]
public sealed class SilkVoiceDecoder : IDisposable
{
    /// <summary>Steam Voice's SILK rate, the <c>0x0B</c> record's value in every packet measured.</summary>
    public const int SampleRate = 16000;

    /// <summary>The SDK's own cap on frames per packet (<c>SILK_MAX_FRAMES_PER_PACKET</c>).</summary>
    private const int MaxFramesPerPacket = 5;

    /// <summary>One 20 ms frame at the SDK's highest API rate, 24 kHz.</summary>
    private const int MaxFrameSamples = 480;

    private readonly nint _state;
    private bool _disposed;

    static SilkVoiceDecoder() => NativeLibraryResolver.EnsureRegistered();

    /// <summary>Whether the native SILK library is present and usable on this machine.</summary>
    /// <remarks>See <see cref="SpeexVoiceDecoder.IsAvailable"/> — same reasoning, same laziness.</remarks>
    public static bool IsAvailable => _available ??= Probe();

    private static bool? _available;

    private static bool Probe()
    {
        try
        {
            using SilkVoiceDecoder probe = new();
            return true;
        }
        catch (DllNotFoundException)
        {
            // Absence of the library is the answer to this question, however it is reported.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Creates a decoder for one speaker's stream.</summary>
    /// <exception cref="InvalidOperationException">silk.dll is missing or refused to initialise.</exception>
    public SilkVoiceDecoder()
    {
        int size;

        // Stryker disable all : a mutant that empties the catch removes the throw the compiler
        // relies on, leaving 'size' unassigned below (CS0165), and Safe Mode then drops every
        // mutation in this method — B410.
        try
        {
            Check(NativeSilk.GetDecoderSize(out size), "SKP_Silk_SDK_Get_Decoder_Size");
        }
        catch (DllNotFoundException missing)
        {
            throw new InvalidOperationException(
                "silk.dll was not found next to this assembly. Run tools/native-audio/build.ps1 " +
                "to build it from source - see that directory's README.md.",
                missing);
        }

        // Stryker restore all

        _state = Marshal.AllocHGlobal(size);

        try
        {
            Check(NativeSilk.InitDecoder(_state), "SKP_Silk_SDK_InitDecoder");
        }
        catch (InvalidOperationException)
        {
            Marshal.FreeHGlobal(_state);
            throw;
        }
    }

    /// <summary>Decodes one SILK packet — every 20 ms frame in it.</summary>
    /// <param name="packet">One length-prefixed frame of a Steam Voice SILK record, without its prefix.</param>
    /// <returns>16-bit PCM, one channel, at <see cref="SampleRate"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="packet"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The SDK rejected the packet.</exception>
    /// <remarks>
    /// Empty is refused rather than taken as loss: the SDK reads a loss from <c>lostFlag</c>, not
    /// from a length, and Steam marks the end of speech with <c>FFFF</c>, never an empty frame.
    /// </remarks>
    public unsafe short[] Decode(ReadOnlySpan<byte> packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (packet.IsEmpty)
        {
            throw new ArgumentException("A SILK packet cannot be empty.", nameof(packet));
        }

        SilkDecControl control = new() { ApiSampleRate = SampleRate };
        List<short> pcm = new(MaxFrameSamples);
        short* frame = stackalloc short[MaxFrameSamples];
        int frames = 0;

        fixed (byte* data = packet)
        {
            do
            {
                short count = MaxFrameSamples;
                int result = NativeSilk.Decode(_state, ref control, 0, data, packet.Length, frame, ref count);

                if (result != 0)
                {
                    // Stryker disable all : the String mutator wraps the interpolated literal in a
                    // ternary that cannot bind to string.Create's handler (CS1620) — B410.
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"SKP_Silk_SDK_Decode returned {result} for a {packet.Length}-byte packet."));

                    // Stryker restore all
                }

                pcm.AddRange(new ReadOnlySpan<short>(frame, count));
                frames++;
            }
            while (control.MoreInternalDecoderFrames != 0 && frames < MaxFramesPerPacket);
        }

        return [.. pcm];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Marshal.FreeHGlobal(_state);
        _disposed = true;
    }

    private static void Check(int result, string call)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture, $"{call} failed with result {result}."));
        }
    }
}
