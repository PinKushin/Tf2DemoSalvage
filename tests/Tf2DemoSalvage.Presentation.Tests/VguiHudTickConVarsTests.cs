using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A game event and a user message hear the client ConVars their own tick read (B839): `_Host_RunFrame` (`engine.dll`
/// build 1007 FUN_1801a5eb0) runs `Cbuf_Execute` before each host tick's client step, whose `CL_ReadPackets` fires that
/// tick's events, so a frame that plays several ticks hears each tick's with that tick's values.
/// </summary>
public sealed class VguiHudTickConVarsTests
{
    /// <remarks>
    /// **The instrument is the handler's own sound**: the death notice and the chat emit inside their handlers, so what the
    /// viewport holds at that moment is what the handler read. Tick 1000 read `3` (a later tick's run set `9`); tick 1001
    /// read the frame's `9`, the control that the lookup is per tick and not per frame. After the frame the viewport holds
    /// the frame's own.
    /// </remarks>
    [TestCase(1000, 3f)]
    [TestCase(1001, 9f)]
    public void Think_AnEventAndAMessage_HearTheConVarsTheirTickRead(int tick, float expected)
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        float? notice = null;
        float? chat = null;
        hud.DeathNotice.SoundEmitter = _ => notice ??= HudViewport.ConVarsOf(hud.DeathNotice).GetFloat("hud_deathnotice_time");
        hud.Chat.SoundEmitter = _ => chat ??= HudViewport.ConVarsOf(hud.Chat).GetFloat("hud_saytext_time");
        HudConVars conVars = new(null, static _ => "9", static read => read < 1001 ? static _ => "3" : null);
        HudState state = new(
            true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)], ConVars: conVars);

        host.BeginFrame(640, 480);
        hud.Think(
            state,
            events: [Death(tick)],
            userMessages: [new SceneUserMessage(tick, SceneUserMessage.SayText, (byte[])[0, .. Encoding.UTF8.GetBytes("hi"), 0, 1])]);

        (notice, chat).ShouldBe((expected, expected));
        HudViewport.ConVarsOf(hud.Chat).GetFloat("hud_saytext_time").ShouldBe(9f, "the frame's own, put back after");
    }

    /// <remarks>
    /// **The state a handler is passed carries its tick's ConVars too** (B839 review 1, Fable, Sonnet): the chat filters a
    /// name change by `cl_chatfilters` read off the state it is handed (`CHudChat::GetFilterFlags`). Tick 1000 read 0, which
    /// filters it out; tick 1001 read the frame's 2, which lets it through. A replay after a seek hears the frame's 2
    /// (review 2, Fable).
    /// </remarks>
    [TestCase(1000, false, false)]
    [TestCase(1001, false, true)]
    [TestCase(1000, true, true)]
    public void Think_AFilteredMessage_IsFilteredByItsTicksConVars(int tick, bool reset, bool printed)
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        HudConVars conVars = new(null, static _ => "2", static read => read < 1001 ? static _ => "0" : null);
        HudState state = new(
            true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)], ConVars: conVars);
        byte[] nameChange = [1, 0, .. Encoding.UTF8.GetBytes("#TF_Name_Change"), 0, .. Encoding.UTF8.GetBytes("Renamed"), 0, 0, 0, 0];

        host.BeginFrame(640, 480);
        hud.Think(state, reset: reset, userMessages: [new SceneUserMessage(tick, SceneUserMessage.SayText2, nameChange)]);

        hud.Chat.History.Text.Contains("Name_Change", System.StringComparison.Ordinal).ShouldBe(printed);
    }

    /// <remarks>
    /// **A frame of events alone puts the frame's own ConVars back** (B839 review 1, Fable, Sonnet): what the HUD does after
    /// the events, the derived freeze-cam events and the paint, reads the frame's, not the last event's tick's.
    /// </remarks>
    [Test]
    public void Think_EventsAlone_PutTheFramesConVarsBack()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        float? notice = null;
        hud.DeathNotice.SoundEmitter = _ => notice ??= HudViewport.ConVarsOf(hud.DeathNotice).GetFloat("hud_deathnotice_time");
        HudConVars conVars = new(null, static _ => "9", static read => read < 1001 ? static _ => "3" : null);
        HudState state = new(
            true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)], ConVars: conVars);

        host.BeginFrame(640, 480);
        hud.Think(state, events: [Death(1000)]);

        notice.ShouldBe(3f, "the control: the event heard its tick's");
        HudViewport.ConVarsOf(hud.DeathNotice).GetFloat("hud_deathnotice_time").ShouldBe(9f);
    }

    /// <remarks>
    /// **A replay after a seek or a reverse step hears the frame's own ConVars** (B839 review 1, Fable, Sonnet): its events
    /// are read after every run the frame made, whatever their tick. The control: the same event without the reset hears
    /// its tick's.
    /// </remarks>
    [TestCase(true, 9f)]
    [TestCase(false, 3f)]
    public void Think_AReplayedEvent_HearsTheFramesConVars(bool reset, float expected)
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        float? notice = null;
        hud.DeathNotice.SoundEmitter = _ => notice ??= HudViewport.ConVarsOf(hud.DeathNotice).GetFloat("hud_deathnotice_time");
        HudConVars conVars = new(null, static _ => "9", static read => read < 1001 ? static _ => "3" : null);
        HudState state = new(
            true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)], ConVars: conVars);

        host.BeginFrame(640, 480);
        hud.Think(state, events: [Death(1000)], reset: reset);

        notice.ShouldBe(expected);
    }

    /// <summary>A domination by the recorder at a tick, which the death notice sounds (`PlayRivalrySounds`).</summary>
    private static HudGameEvent Death(int tick)
    {
        Dictionary<int, Core.Net.PlayerInfo> roster = new()
        {
            [1] = new Core.Net.PlayerInfo("Recorder", 11, string.Empty, 1, false, false),
            [4] = new Core.Net.PlayerInfo("Pyro", 14, string.Empty, 4, false, false),
        };
        ScenePlayer[] players = [new(1, 0f, 0f, 0f, 2, 125, 1), new(4, 0f, 0f, 0f, 2, 175, 7)];
        Dictionary<string, object?> values = new()
        {
            ["userid"] = 14, ["attacker"] = 11, ["assister"] = -1, ["weapon"] = "sniperrifle", ["death_flags"] = 0x0001,
            ["damagebits"] = 0,
        };

        return new HudGameEvent(
            new SceneGameEvent(tick, "player_death", values, roster), 10f, players, 1, new SceneGameRules(false, 0, false), "maps/cp_test.bsp", 0);
    }
}
