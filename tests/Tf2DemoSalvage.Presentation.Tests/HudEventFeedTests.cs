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

    /// <remarks>
    /// **The span is the settings' (`WindowReach.FeedTicks`), the one the timeline's reach uses**: a 9 s kill feed lives
    /// 18 s for the local player, 1,200 ticks, so a seek back to 2,000 replays from tick 801 — events 900 to 2,000.
    /// </remarks>
    [Test]
    public void Advance_ASeekWithALongerKillFeed_ReplaysTheLongerSpan()
    {
        HudEventFeed feed = new() { Settings = new WindowSettings(DeathNoticeSeconds: 9d) };

        feed.Advance(Events, Interval, 100);
        (bool reset, IReadOnlyList<SceneGameEvent> crossed) = feed.Advance(Events, Interval, 2000);

        reset.ShouldBeTrue();
        crossed.Select(fired => fired.Tick).ShouldBe([.. Enumerable.Range(9, 12).Select(i => i * 100)]);
    }

    [Test]
    public void Advance_ASkipPastTheWindow_ResetsToo()
    {
        HudEventFeed feed = new();

        feed.Advance(Events, Interval, 100);

        feed.Advance(Events, Interval, 1000).Reset.ShouldBeTrue("900 ticks is past the 800 a notice can live");
    }

    /// <remarks>
    /// **B579**: the engine hands each event to the HUD once, on arrival (`usermessages.cpp:151-189`), so a frame looks at
    /// only what it crossed. Going 150 → 300 crosses events 200 and 300 and messages 200 and 300; the old full walk looked
    /// at all forty.
    /// </remarks>
    [Test]
    public void Advance_AnOrderedTimeline_ExaminesOnlyTheCrossedItems()
    {
        HudEventFeed feed = new();
        IReadOnlyList<SceneUserMessage> messages = [.. Events.Select(fired => Message(fired.Tick))];

        feed.Advance(Events, messages, Interval, 150);
        feed.Advance(Events, messages, Interval, 300);

        feed.Examined.ShouldBe(4);
    }

    [Test]
    public void Advance_AnUnorderedTimeline_ExaminesEveryItem()
    {
        HudEventFeed feed = new();
        IReadOnlyList<SceneGameEvent> unordered = [.. Events.Reverse()];

        feed.Advance(unordered, [], Interval, 150);
        feed.Advance(unordered, [], Interval, 300).Events.Select(fired => fired.Tick).ShouldBe([300, 200], "stream order");

        feed.Examined.ShouldBe(20);
    }

    /// <remarks>
    /// The control is the walk this replaced, run beside the feed over one random sequence of ticks: steps forward, seeks
    /// back, skips past the window, repeats of the same tick and frames on the window's exact edge. The timeline has
    /// duplicate ticks; the third case is out of order and must still match, walked whole.
    /// </remarks>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Advance_ARandomPlayback_MatchesTheFullWalk(bool duplicates, bool shuffled)
    {
        // A fixed linear congruential sequence, so a failure names a reproducible case.
        uint state = 97;
        int Next(int below)
        {
            state = (state * 1664525u) + 1013904223u;

            return (int)(state >> 8) % below;
        }

        List<int> ticks = [.. Enumerable.Range(0, 300).Select(_ => Next(3000))];

        if (duplicates)
        {
            ticks.AddRange(ticks.Take(100));
        }

        ticks.Sort();

        if (shuffled)
        {
            ticks = [.. ticks.OrderBy(_ => Next(1 << 20))];
        }

        IReadOnlyList<SceneGameEvent> events = [.. ticks.Select((tick, at) => Event(tick, at))];
        IReadOnlyList<SceneUserMessage> messages = [.. ticks.Select(Message)];
        HudEventFeed feed = new();
        int? last = null;
        int windowTicks = WindowReach.FeedTicks(feed.Settings, Interval);
        int shown = 0;

        for (int frame = 0; frame < 2000; frame++)
        {
            shown = (frame % 50) switch
            {
                0 => Next(3200),
                1 => shown,
                2 => shown + windowTicks,
                3 => shown + windowTicks + 1,
                _ => shown + Next(9),
            };

            bool reset = last is not { } previous || shown < previous || shown - previous > windowTicks;
            int from = reset ? shown - windowTicks : last!.Value;
            last = shown;

            (bool actualReset, IReadOnlyList<SceneGameEvent> actualEvents, IReadOnlyList<SceneUserMessage> actualMessages) =
                feed.Advance(events, messages, Interval, shown);

            actualReset.ShouldBe(reset, $"frame {frame}");
            actualEvents.ShouldBe([.. events.Where(fired => fired.Tick > from && fired.Tick <= shown)], $"frame {frame}");
            actualMessages.ShouldBe([.. messages.Where(message => message.Tick > from && message.Tick <= shown)], $"frame {frame}");
        }
    }

    private static SceneGameEvent Event(int tick, int id) =>
        new(tick, "player_death", new Dictionary<string, object?> { ["id"] = id }, new Dictionary<int, PlayerInfo>());

    private static SceneUserMessage Message(int tick) => new(tick, 4, new byte[] { (byte)tick });
}
