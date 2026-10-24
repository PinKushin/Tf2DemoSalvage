using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The game events playback crossed since the last frame, as the HUD's listeners receive them.</summary>
/// <remarks>
/// Playing forward, each event fires once, in stream order, as its tick is reached. A seek — backward, or forward past
/// <see cref="Window"/> — is `demo_gototick`: the engine reads every packet to the new tick, rendering a frame per 99
/// (B504), so listeners hear each event. The viewer's skip runs the HUD through those frames itself (`MainForm.ReplaySkip`),
/// so this feed is reset only where no skip ran first — no HUD yet. Replaying the last <see cref="Window"/> seconds is then
/// the same picture, because no line outlives it (`hud_deathnotice_time` × 2, hud_basedeathnotice.cpp:923; `hud_saytext_time`).
/// **ponytail:** an event older than the window that a later one would have merged into (`UseExistingNotice`) is not
/// replayed; the merged line would already have expired, so only its count can differ.
/// </remarks>
public sealed class HudEventFeed
{
    private int? _lastTick;

    /// <summary>The viewer's settings, whose HUD feed times set how much a seek replays — the one owner of that span (D220).</summary>
    public WindowSettings Settings { get; set; } = new();

    /// <summary>The seconds replayed after a seek: the longest feed line's life (<see cref="WindowSettings.FeedSeconds"/>).</summary>
    public float Window => (float)Settings.FeedSeconds;

    /// <summary>The events to deliver this frame, and whether the HUD must be emptied first.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="tick">The tick now shown.</param>
    /// <returns>Whether this is a seek, and the events in order.</returns>
    /// <remarks>The chat's user messages ride the same window: `demo_gototick` reads their packets too.</remarks>
    public (bool Reset, IReadOnlyList<SceneGameEvent> Events, IReadOnlyList<SceneUserMessage> UserMessages) Advance(DemoTimeline timeline, int tick)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return Advance(timeline.GameEvents, timeline.UserMessages, Interval(timeline), tick);
    }

    /// <summary>The events to deliver this frame from a stream, and whether the HUD must be emptied first.</summary>
    /// <param name="events">Every event, in stream order.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="tick">The tick now shown.</param>
    /// <returns>Whether this is a seek, and the events in order.</returns>
    public (bool Reset, IReadOnlyList<SceneGameEvent> Events) Advance(IReadOnlyList<SceneGameEvent> events, float interval, int tick)
    {
        (bool reset, IReadOnlyList<SceneGameEvent> crossed, _) = Advance(events, [], interval, tick);

        return (reset, crossed);
    }

    /// <summary>The events and user messages to deliver this frame, and whether the HUD must be emptied first.</summary>
    /// <param name="events">Every event, in stream order.</param>
    /// <param name="messages">Every user message, in stream order.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="tick">The tick now shown.</param>
    /// <returns>Whether this is a seek, and the items crossed, each in stream order.</returns>
    /// <remarks>
    /// **Only the window is looked at** (B579): the engine hands each to the HUD once, on arrival
    /// (`usermessages.cpp:151-189`), so walking the whole match each frame is a cost it never pays. A tick-ordered list's
    /// window is found by search; one out of order is walked whole, the same items in the same order (<see cref="LandedWindow"/>).
    /// </remarks>
    public (bool Reset, IReadOnlyList<SceneGameEvent> Events, IReadOnlyList<SceneUserMessage> UserMessages) Advance(
        IReadOnlyList<SceneGameEvent> events, IReadOnlyList<SceneUserMessage> messages, float interval, int tick)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(messages);

        int windowTicks = WindowReach.FeedTicks(Settings, interval);
        bool reset = _lastTick is not { } last || tick < last || tick - last > windowTicks;
        int from = reset ? tick - windowTicks : _lastTick!.Value;

        _lastTick = tick;

        List<SceneGameEvent> crossed = Crossed(events, static fired => fired.Tick, ref _eventsOrderOf, ref _eventsInOrder, from, tick, out int eventsExamined);
        List<SceneUserMessage> arrived = Crossed(messages, static message => message.Tick, ref _messagesOrderOf, ref _messagesInOrder, from, tick, out int messagesExamined);

        Examined = eventsExamined + messagesExamined;

        return (reset, crossed, arrived);
    }

    /// <summary>How many events and user messages the last <see cref="Advance(DemoTimeline, int)"/> looked at.</summary>
    public int Examined { get; private set; }

    private object? _eventsOrderOf;
    private bool _eventsInOrder;
    private object? _messagesOrderOf;
    private bool _messagesInOrder;

    /// <summary>The items of a list in <c>(from, tick]</c>, in list order.</summary>
    private static List<T> Crossed<T>(
        IReadOnlyList<T> items, Func<T, int> tickOf, ref object? orderOf, ref bool inOrder, int from, int tick, out int examined)
    {
        (int first, int end) = LandedWindow.Landed(items, tickOf, ref orderOf, ref inOrder, from, tick);
        List<T> crossed = [];

        examined = end - first;

        for (int at = first; at < end; at++)
        {
            int itemTick = tickOf(items[at]);

            if (itemTick > from && itemTick <= tick)
            {
                crossed.Add(items[at]);
            }
        }

        return crossed;
    }

    /// <summary>Each event with the world its listener reads: the players and rules at its tick, and the local player.</summary>
    /// <param name="timeline">The demo.</param>
    /// <param name="events">The events.</param>
    /// <param name="mapName">The demo's map, such as `cp_process_final`.</param>
    /// <param name="hooks">The attribute hooks, or null where no install is open — then no vision is granted.</param>
    /// <returns>The events as the HUD receives them.</returns>
    /// <remarks>
    /// `GetLocalPlayerVisionFilterFlags` (c_tf_player.cpp:8028) is `CALL_ATTRIB_HOOK_INT( vision_opt_in_flags )` on the local
    /// player. **Not applied:** `tf_spectate_pyrovision`, 0 by default, and the Halloween and Rome arms, whose bits no death
    /// notice tests.
    /// </remarks>
    public static IReadOnlyList<HudGameEvent> Resolve(DemoTimeline timeline, IReadOnlyList<SceneGameEvent> events, string mapName, AttributeHooks? hooks)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(events);

        List<HudGameEvent> resolved = [];
        int local = timeline.RecorderEntityIndex ?? 0;

        foreach (SceneGameEvent fired in events)
        {
            if (!VguiHud.ListensFor.Contains(fired.Name))
            {
                continue;
            }

            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(fired.Tick);
            int vision = 0;

            foreach (ScenePlayer player in players)
            {
                if (player.EntityIndex == local && hooks is not null)
                {
                    vision = AttributeHooks.RoundFloatToInt(hooks.OnPlayer(player, "vision_opt_in_flags", 0f));
                }
            }

            resolved.Add(new HudGameEvent(
                fired, fired.Tick * Interval(timeline), players, local, timeline.RulesAt(fired.Tick), $"maps/{mapName}.bsp", vision));
        }

        return resolved;
    }

    /// <summary>The recording server's tick interval, or TF2's when the demo does not say.</summary>
    private static float Interval(DemoTimeline timeline) =>
        timeline.IntervalPerTick > 0f ? timeline.IntervalPerTick : (float)ScenePropTrack.Tf2TickInterval;
}
