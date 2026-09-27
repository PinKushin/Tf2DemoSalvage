using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Which game events a frame delivers: each once going forward, the last twelve seconds again after a seek.</summary>
/// <remarks>Events at ticks 100, 200, … 2000; one tick is 0.015 s, so twelve seconds is 800 ticks.</remarks>
public sealed class HudEventFeedTests
{
    private const float Interval = 0.015f;

    private static readonly IReadOnlyList<SceneGameEvent> Events =
        [.. Enumerable.Range(1, 20).Select(i => new SceneGameEvent(i * 100, "player_death", new Dictionary<string, object?>(), new Dictionary<int, PlayerInfo>()))];

    [TestCase("player_death")]
    [TestCase("teamplay_update_timer")]
    [TestCase("teamplay_timer_time_added")]
    public void ListensFor_EveryElementsEvents_AreResolved(string name) =>
        VguiHud.ListensFor.ShouldContain(name);

    [Test]
    public void Advance_PlayingForward_DeliversEachEventOnceAsItsTickIsReached()
    {
        HudEventFeed feed = new();

        feed.Advance(Events, Interval, 150);
        (bool reset, IReadOnlyList<SceneGameEvent> crossed) = feed.Advance(Events, Interval, 300);

        reset.ShouldBeFalse();
        crossed.Select(fired => fired.Tick).ShouldBe([200, 300]);
    }

    [Test]
    public void Advance_ASeekBackward_ResetsAndReplaysTheWindow()
    {
        HudEventFeed feed = new();

        feed.Advance(Events, Interval, 2000);
        (bool reset, IReadOnlyList<SceneGameEvent> crossed) = feed.Advance(Events, Interval, 1000);

        reset.ShouldBeTrue();
        crossed.Select(fired => fired.Tick).ShouldBe([300, 400, 500, 600, 700, 800, 900, 1000], "ticks 201 to 1000");
    }

    [Test]
    public void Advance_ASkipPastTheWindow_ResetsToo()
    {
        HudEventFeed feed = new();

        feed.Advance(Events, Interval, 100);

        feed.Advance(Events, Interval, 1000).Reset.ShouldBeTrue("900 ticks is past the 800 a notice can live");
    }
}
