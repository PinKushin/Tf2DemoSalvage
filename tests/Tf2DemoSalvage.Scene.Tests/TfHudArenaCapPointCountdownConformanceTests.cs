using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudArenaCapPointCountdown::OnTick`'s announcer (game/client/tf/tf_hud_arena_capturepoint.cpp:137-191).</summary>
public sealed class TfHudArenaCapPointCountdownConformanceTests
{
    /// <remarks>
    /// A tick each second from 5.5 left down to the unlock: "5" to "1", one each, then "capture enabled" once — armed by "1",
    /// and not again on the next tick past it.
    /// </remarks>
    [Test]
    public void OnThink_CountingDownToTheUnlock_PlaysEachLineOnceThenCaptureEnabled()
    {
        List<string> played = [];
        HudViewport viewport = new() { SoundEmitter = played.Add };
        TfHudArenaCapPointCountdown countdown = new(viewport);

        foreach (float now in new[] { 4.5f, 5.5f, 6.5f, 7.5f, 8.5f, 9.5f, 10.5f, 11.5f })
        {
            viewport.SetState(Arena(now));
            countdown.Visible = true;
            countdown.Think();
        }

        played.ShouldBe([
            "Announcer.RoundBegins5Seconds", "Announcer.RoundBegins4Seconds", "Announcer.RoundBegins3Seconds",
            "Announcer.RoundBegins2Seconds", "Announcer.RoundBegins1Seconds", "Announcer.AM_CapEnabledRandom"]);
    }

    /// <remarks>
    /// `CControlPointCountdown::OnTick` (hud_controlpointicons.cpp:1851-1903) truncates rather than rounding up: 5.5 left is "5".
    /// </remarks>
    [Test]
    public void Update_APointCountingDownToItsUnlock_PlaysEachLineOnceThenCaptureEnabled()
    {
        List<string> played = [];
        HudViewport viewport = new() { SoundEmitter = played.Add };
        TfControlPointCountdown countdown = new(viewport, "CountdownLabel");

        foreach (float now in new[] { 4.5f, 5.5f, 6.5f, 7.5f, 8.5f, 9.5f, 10.5f })
        {
            countdown.Update(new HudState(true, true, 0, 125, true, CurTime: now, Team: 2, LocalIndex: 1, RoundState: 4), 10f);
        }

        played.ShouldBe([
            "Announcer.RoundBegins5Seconds", "Announcer.RoundBegins4Seconds", "Announcer.RoundBegins3Seconds",
            "Announcer.RoundBegins2Seconds", "Announcer.RoundBegins1Seconds", "Announcer.AM_CapEnabledRandom"]);
    }

    /// <remarks>A seek to three seconds left spends "5" and "4", as the ticks it skipped would have, and plays "3" then "2".</remarks>
    [Test]
    public void OnThink_ASeekIntoTheCount_PlaysOnlyTheLinesStillAhead()
    {
        List<string> played = [];
        HudViewport viewport = new() { SoundEmitter = played.Add };
        TfHudArenaCapPointCountdown countdown = new(viewport) { SeekLanded = true };

        foreach (float now in new[] { 7.5f, 7.6f, 8.5f })
        {
            viewport.SetState(Arena(now));
            countdown.Visible = true;
            countdown.Think();
        }

        played.ShouldBe(["Announcer.RoundBegins3Seconds", "Announcer.RoundBegins2Seconds"]);
    }

    [Test]
    public void Update_ASeekIntoAPointsCount_PlaysOnlyTheLinesStillAhead()
    {
        List<string> played = [];
        HudViewport viewport = new() { SoundEmitter = played.Add };
        TfControlPointCountdown countdown = new(viewport, "CountdownLabel");

        countdown.Update(new HudState(true, true, 0, 125, true, CurTime: 1f, Team: 2, LocalIndex: 1, RoundState: 4), 10f);
        countdown.SeekLanded = true;

        foreach (float now in new[] { 6.5f, 6.6f, 7.5f })
        {
            countdown.Update(new HudState(true, true, 0, 125, true, CurTime: now, Team: 2, LocalIndex: 1, RoundState: 4), 10f);
        }

        played.ShouldBe(["Announcer.RoundBegins3Seconds", "Announcer.RoundBegins2Seconds"]);
    }

    private static HudState Arena(float now) =>
        new(true, true, 0, 125, true, CurTime: now, Team: 2, LocalIndex: 1, RoundState: HudState.RoundStateStalemate,
            Rules: new SceneGameRules(false, 0, false) { Present = true, GameType = SceneGameRules.GameTypeArena, CapturePointEnableTime = 10f });
}
