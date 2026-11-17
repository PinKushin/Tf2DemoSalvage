using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>On a real SourceTV demo the soundscape is the SourceTV client's own, as the engine's (B544).</summary>
/// <remarks>
/// **Only real bytes say whether the SourceTV client's own entity carries `m_audio`**: if it did not, the local player's
/// soundscape — the only one `Soundscape_Update` is fed (`c_baseplayer.cpp:1025-1051`) — would leave a SourceTV demo with
/// none. Measured 2026-11-17: it does — the SourceTV client (entity 1) stands in soundscape 41, from `env_soundscape` 42,
/// for the whole match, one sample. The walk-last sampler held 886 samples (D217), every player's in turn.
/// </remarks>
public sealed class SoundscapeListenerCorpusTests
{
    private const string Match = "demostf-cp_process_f12-2026-08-08-2207";

    [Test]
    public void Soundscapes_OnF12_AreTheSourceTvClientsOneSoundscape()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));
        int middle = timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        (timeline.RecorderEntityIndex, timeline.Soundscapes.Count, timeline.SoundscapeAt(middle)?.Index,
         timeline.SoundscapeAt(middle)?.EntityIndex)
            .ShouldBe(((int?)1, 1, (int?)41, (int?)42));
    }
}
