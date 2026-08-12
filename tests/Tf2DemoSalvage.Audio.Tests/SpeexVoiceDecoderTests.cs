using System;
using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Tests <see cref="SpeexVoiceDecoder"/>'s lifecycle and error handling.</summary>
public sealed class SpeexVoiceDecoderTests
{
    [Fact]
    public void Construction_LoadsTheNativeLibraryAndSucceeds()
    {
        using SpeexVoiceDecoder decoder = new();
        decoder.ShouldNotBeNull();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        SpeexVoiceDecoder decoder = new();
        decoder.Dispose();
        Should.NotThrow(decoder.Dispose);
    }

    [Fact]
    public void Decode_AfterDispose_Throws()
    {
        SpeexVoiceDecoder decoder = new();
        decoder.Dispose();

        Should.Throw<ObjectDisposedException>(
            () => decoder.Decode(new byte[28]));
    }

    [Fact]
    public void Decode_EmptyFrame_Throws()
    {
        using SpeexVoiceDecoder decoder = new();

        Should.Throw<ArgumentException>(() => decoder.Decode([]));
    }
}
