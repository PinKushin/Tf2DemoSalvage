using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// The seek scan's one-player sampler answers what the frame's sampler answers (D233).
/// </summary>
/// <remarks>
/// `MainSequence`'s seek recovery reads earlier ticks through <see cref="DemoTimeline.PlayerAt(double, int)"/> so it does not
/// disturb the per-frame caches; if the two disagreed, a seek would key its roll on an activity the frame never
/// showed. A SourceTV recording, so there is no viewpoint for the frame's sampler to differ by.
/// </remarks>
public sealed class PlayerAtCorpusTests
{
    [Test]
    public void PlayerAt_EveryPlayerOfAFrameBetweenTicks_EqualsPlayersAt()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("tf2-2013-build1729296-spec-stv-cp_granary"));
        double tick = timeline.Frames[timeline.Frames.Count / 2].Tick + 0.37d;
        List<ScenePlayer> frame = [];

        timeline.PlayersAt(tick, frame);

        frame.Count.ShouldBeGreaterThan(1, "the control: a frame with players in it");

        foreach (ScenePlayer player in frame)
        {
            timeline.PlayerAt(tick, player.EntityIndex).ShouldBe(player);
        }

        timeline.PlayerAt(tick, 4095).ShouldBeNull();
    }
}
