using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A frame `demo_gototick` renders while skipping: the HUD hears its messages, and nothing is shown (B504).</summary>
public sealed class VguiHudSkipFrameTests
{
    /// <remarks>
    /// `ProcessGameEvent` (engine.dll FUN_1801f9510) and the user-message hooks have no skip test, so the chat's
    /// `HudChat.Message` is emitted — and deals — during a skip; the frame's paint is the viewer's to leave out.
    /// </remarks>
    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void Frame_ASayTextOnASkipFrame_EmitsTheChatSoundAndPaintsOnlyWhenAsked(bool paint, int drawsAnything)
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        List<string> sounds = [];
        hud.Chat.SoundEmitter = sounds.Add;
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)]);

        host.BeginFrame(640, 480);
        hud.Frame(
            state,
            userMessages: [new SceneUserMessage(10, SceneUserMessage.SayText, (byte[])[0,.. Encoding.UTF8.GetBytes("hi"), 0, 1])],
            paint: paint);

        (string.Join(',', sounds), host.List.Quads.Count > 0 ? 1 : 0).ShouldBe(("HudChat.Message", drawsAnything));
    }

    /// <remarks>
    /// **Finding 105 B5.7: no sound from the half a HUD fork would move.** The same say-text through the split: `Think`
    /// emits the chat's sound and draws nothing; `Paint` draws the line and emits nothing, so a paint on a worker cannot
    /// reach `PlayHudSound`. The quads are the control that the paint drew what the think made.
    /// </remarks>
    [Test]
    public void Paint_AfterAThinkThatEmittedTheChatSound_DrawsTheLineAndEmitsNothing()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        List<string> sounds = [];
        hud.Chat.SoundEmitter = sounds.Add;
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)]);

        host.BeginFrame(640, 480);
        hud.Think(state, userMessages: [new SceneUserMessage(10, SceneUserMessage.SayText, (byte[])[0, .. Encoding.UTF8.GetBytes("hi"), 0, 1])])
            .ShouldBeTrue();
        (string.Join(',', sounds), host.List.Quads.Count).ShouldBe(("HudChat.Message", 0), "the think emitted and drew nothing");

        hud.Paint(state, reset: false);

        (sounds.Count, host.List.Quads.Count > 0).ShouldBe((1, true));
    }

    /// <remarks>
    /// **B5.7 for every emitter the HUD wires** — the chat, the death notices and the streak banner they own, the match
    /// status, the item meters: each sound comes from an event or an update, which `Think` runs, never from a paint. Fed a
    /// domination and a penetration and a fifth kill by the recorder (`PlayRivalrySounds`, `Game.PenetrationKill`,
    /// `CTFStreakNotice::StreakUpdated`) and the casual countdown at ten (`ShowMatchStartDoors`), then painted: everything
    /// heard is heard in the think, and the paint adds nothing. The think's sounds are the control that the feed was live.
    /// **Two emitters this fixture cannot fire, held by reading instead**: the streak banner's one call
    /// (`TfStreakNotice.cs:212`, in `StreakUpdated`, reached from the death event) returns before it on an install with no
    /// `#Msg_KillStreak` string, which this empty one is; and `TfHudItemEffectMeter.Update`, the meters' one emitter, is called
    /// only from `Think`'s `ItemEffectMeters.Update`.
    /// </remarks>
    [Test]
    public void Paint_AfterAThinkThatFiredEveryEmitter_EmitsNothing()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        List<string> heard = [];
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)]);

        hud.Chat.SoundEmitter = heard.Add;
        hud.DeathNotice.SoundEmitter = heard.Add;
        hud.MatchStatus.SoundEmitter = heard.Add;
        hud.ItemEffectMeters.SoundEmitter = heard.Add;

        host.BeginFrame(640, 480);
        hud.Think(
            state,
            events:
            [
                Event("player_death", new()
                {
                    ["userid"] = 14, ["attacker"] = 11, ["assister"] = -1, ["weapon"] = "sniperrifle", ["death_flags"] = 0x0001,
                    ["damagebits"] = 0, ["kill_streak_total"] = 5, ["kill_streak_victim"] = 0, ["playerpenetratecount"] = 1,
                }),
                Event("restart_timer_time", new() { ["time"] = 10 }),
            ],
            userMessages: [new SceneUserMessage(10, SceneUserMessage.SayText, (byte[])[0, .. Encoding.UTF8.GetBytes("hi"), 0, 1])])
            .ShouldBeTrue();

        List<string> thought = [.. heard];

        hud.Paint(state, reset: false);

        thought.ShouldBe(
            ["Game.PenetrationKill", "Game.Domination", "MatchMaking.RoundStartCasual", "HudChat.Message"],
            "the control: the death notices, the match status and the chat each fired in the think");
        heard.ShouldBe(thought, "the paint emits nothing");
    }

    private static HudGameEvent Event(string name, Dictionary<string, object?> values)
    {
        Dictionary<int, Core.Net.PlayerInfo> roster = new()
        {
            [1] = new Core.Net.PlayerInfo("Recorder", 11, string.Empty, 1, false, false),
            [4] = new Core.Net.PlayerInfo("Pyro", 14, string.Empty, 4, false, false),
        };
        ScenePlayer[] players = [new(1, 0f, 0f, 0f, 2, 125, 1), new(4, 0f, 0f, 0f, 2, 175, 7)];

        return new HudGameEvent(new SceneGameEvent(1000, name, values, roster), 10f, players, 1, new SceneGameRules(false, 0, false) { MatchGroup = 7 }, "maps/cp_test.bsp", 0);
    }
}
