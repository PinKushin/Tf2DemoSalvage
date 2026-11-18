using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>`GetGroundEntity()` as the client holds it, on a real point-of-view recording (B768).</summary>
/// <remarks>
/// **Only real bytes say what a remote player's handle is**: `m_hGroundEntity` is in `DT_LocalPlayerExclusive`
/// (player.cpp:8117-8138), and the `ground-entity` probe measured on this recording that a bot carries it in one packet
/// or none — so the client holds the instance baseline's worldspawn (handle 534528, slot 0) for the whole demo, while
/// the recorder's arrives every packet. The duck-jump offset is gated on it (c_tf_player.cpp:8764): TF2 never applies
/// it to these bots, airborne or not.
/// </remarks>
public sealed class GroundEntityCorpusTests
{
    private const string Bots = "tf2-live-pov-cp_process_final-bots-2026-11-05";

    [Test]
    public void PlayersAt_OnABotsPovDemo_HoldsTheWorldForEveryRemotePlayerAndChangesOnlyTheRecorders()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo(Bots));
        HashSet<int?> remote = [];
        HashSet<int?> recorder = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 10)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                (player.EntityIndex == timeline.RecorderEntityIndex ? recorder : remote).Add(player.GroundEntity);
            }
        }

        // Every remote sample is the world; the recorder lands and leaves it, so his holds more than one answer.
        (remote.Count, remote.Single(), recorder.Count > 1).ShouldBe((1, (int?)0, true));
    }
}
