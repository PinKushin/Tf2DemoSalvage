using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>On a real SourceTV demo the SourceTV client's own entity carries a soundscape and a fog (B544).</summary>
/// <remarks>
/// **Only real bytes say whether the SourceTV client's own entity carries `m_audio` and `m_PlayerFog.m_hCtrl`**: the
/// soundscape and the fog are the local player's alone (`c_baseplayer.cpp:1025-1051`, viewrender.cpp:4713-4718), so if
/// it did not, reading his would leave a SourceTV demo with neither. Which soundscape and which fog is a measurement
/// (D38), recorded in RISKS B544, not asserted here; the synthetic suites own the rule.
/// </remarks>
public sealed class SoundscapeListenerCorpusTests
{
    private const string Match = "demostf-cp_process_f12-2026-08-08-2207";

    [Test]
    public void SoundscapeAt_OnASourceTvMatch_IsCarriedByTheSourceTvClient()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));

        timeline.SoundscapeAt(Middle(timeline)).ShouldNotBeNull();
    }

    [Test]
    public void FogAt_OnASourceTvMatch_IsCarriedByTheSourceTvClient()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Match));

        timeline.FogAt(Middle(timeline)).ShouldNotBeNull();
    }

    private static int Middle(DemoTimeline timeline) => timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);
}
