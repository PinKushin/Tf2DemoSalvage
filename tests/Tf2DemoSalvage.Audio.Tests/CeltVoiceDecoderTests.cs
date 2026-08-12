using System;
using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Tests <see cref="CeltVoiceDecoder"/>'s lifecycle and error handling.
/// </summary>
/// <remarks>
/// As with <see cref="OpusVoiceDecoderTests"/>, real CELT frames are not hand-built here. The
/// corpus's real 64/128/192-byte frames from <c>z1800.dem</c> are the decisive test, in
/// <c>Tf2DemoSalvage.Corpus.Tests</c>.
/// </remarks>
public sealed class CeltVoiceDecoderTests
{
    [Fact]
    public void Construction_LoadsTheNativeLibraryAndSucceeds()
    {
        using CeltVoiceDecoder decoder = new();
        decoder.ShouldNotBeNull();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        CeltVoiceDecoder decoder = new();
        decoder.Dispose();
        Should.NotThrow(decoder.Dispose);
    }

    [Fact]
    public void Decode_AfterDispose_Throws()
    {
        CeltVoiceDecoder decoder = new();
        decoder.Dispose();

        Should.Throw<ObjectDisposedException>(() => decoder.Decode([0x18, 0x01, 0x02]));
    }

    [Fact]
    public void Decode_EmptyFrame_Throws()
    {
        using CeltVoiceDecoder decoder = new();

        Should.Throw<ArgumentException>(() => decoder.Decode([]));
    }

    [Fact]
    public void Decode_GarbageBytes_ThrowsRatherThanCrashing()
    {
        using CeltVoiceDecoder decoder = new();

        byte[] garbage = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        Should.Throw<InvalidOperationException>(() => decoder.Decode(garbage));
    }
}
