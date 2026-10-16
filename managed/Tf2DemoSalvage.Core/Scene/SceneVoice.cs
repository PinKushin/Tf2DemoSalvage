namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One <c>svc_VoiceData</c> with a body: a packet of a player's microphone audio, as the codec sent it.</summary>
/// <param name="Tick">The tick of the packet that carried it.</param>
/// <param name="Client">The speaking client's slot.</param>
/// <param name="Proximity">Whether the server sent it as proximity voice, spatialised from the speaker.</param>
/// <param name="Body">The codec payload.</param>
public sealed record SceneVoice(int Tick, int Client, bool Proximity, System.ReadOnlyMemory<byte> Body)
{
    /// <summary>The speaker's entity: <c>m_nFromClient + 1</c>, as <c>CClientState::ProcessVoiceData</c> (engine.dll <c>FUN_1801fa0d0</c>) reads it.</summary>
    public int Entity => Client + 1;
}

/// <summary>One <c>svc_VoiceInit</c>: the codec the session's voice is in, from its tick on.</summary>
/// <param name="Tick">The tick of the packet that carried it.</param>
/// <param name="Codec">The codec name, e.g. <c>vaudio_celt</c> or <c>steam</c>.</param>
/// <param name="Quality">The quality byte, or the sample rate behind quality 255.</param>
/// <param name="SampleRate">The rate transmitted behind quality 255, or null.</param>
public sealed record SceneVoiceInit(int Tick, string Codec, int Quality, int? SampleRate);
