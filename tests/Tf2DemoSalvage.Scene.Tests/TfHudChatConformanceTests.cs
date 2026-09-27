using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHudChat` over `CBaseHudChat` (game/client/tf/tf_hud_chat.cpp, hud_basechat.cpp): messages into the history.</summary>
/// <remarks>
/// The local player is 1, RED. Player 2 "Blue" is BLU. `TF_Chat_All` is TF's own: the name in the player's colour, then
/// the text in the normal one. The chat scheme's name colours are distinct so each range can be told apart.
/// </remarks>
public sealed class TfHudChatConformanceTests
{
    private const string BlueName = "10 20 30 255";
    private const string Yellow = "40 50 60 255";

    [Test]
    public void SayText2_APlayersChat_WritesTheLineWithTheNameInTheirTeamColour()
    {
        TfHudChat chat = Built();

        chat.HandleUserMessage(SayText2(2, true, "TF_Chat_All", "Blue", "hi"), State());

        chat.History.Text.ShouldBe("\nBlue :  hi");
        Painted(chat).ShouldBe([("Blue", "10 20 30 255"), (" :  hi", "40 50 60 255")]);
    }

    [Test]
    public void SayText2_AColourCodeInTheChatText_IsMadeNormal()
    {
        // `ReadChatTextString` turns a player's own colour codes into `COLOR_NORMAL`, which then starts a range of its own.
        TfHudChat chat = Built();

        chat.HandleUserMessage(SayText2(2, true, "TF_Chat_All", "Blue", "a\u0003b"), State());

        chat.History.Text.ShouldBe("\nBlue :  ab");
    }

    [Test]
    public void SayText_FromTheServer_IsInTheNormalColour()
    {
        TfHudChat chat = Built();

        chat.HandleUserMessage(Message(SceneUserMessage.SayText, [0], "server says", [1]), State());

        (chat.History.Text, Painted(chat).Single().Colour).ShouldBe(("\nserver says", Yellow));
    }

    [Test]
    public void TextMsg_ATalkMessage_LocalisesAndSubstitutes()
    {
        // HUD_PRINTTALK is 3; the string and up to four parameters, each looked up.
        TfHudChat chat = Built();

        chat.HandleUserMessage(Message(SceneUserMessage.TextMsg, [3], "#game_joined", "Blue", string.Empty, string.Empty, string.Empty), State());

        chat.History.Text.ShouldBe("\nBlue joined");
    }

    [Test]
    public void TextMsg_ACentrePrint_IsNotChat()
    {
        TfHudChat chat = Built();

        chat.HandleUserMessage(Message(SceneUserMessage.TextMsg, [4], "centre", string.Empty, string.Empty, string.Empty, string.Empty), State());

        chat.History.Text.ShouldBe(string.Empty);
    }

    [Test]
    public void ChatPrintf_AnEmptyMessage_WritesNothing()
    {
        TfHudChat chat = Built();

        chat.HandleUserMessage(SayText2(2, true, "\u0001", string.Empty, string.Empty), State());

        chat.History.Text.ShouldBe(string.Empty);
    }

    [Test]
    public void SayText2_WantsToChat_PlaysHudChatMessage()
    {
        TfHudChat chat = Built();
        List<string> sounds = [];

        chat.SoundEmitter = sounds.Add;
        chat.HandleUserMessage(SayText2(2, true, "TF_Chat_All", "Blue", "hi"), State());

        sounds.ShouldBe(["HudChat.Message"]);
    }

    [Test]
    public void SayText2_AServerString_PlaysNoSound()
    {
        // `wantsToChat` false: only the `ChatPrintf`/sound pair inside that branch runs (hud_basechat.cpp:858).
        TfHudChat chat = Built();
        List<string> sounds = [];

        chat.SoundEmitter = sounds.Add;
        chat.HandleUserMessage(Message(SceneUserMessage.SayText2, [2, 0], "#game_joined", "Blue", string.Empty, string.Empty, string.Empty), State());

        sounds.ShouldBeEmpty();
    }

    [Test]
    public void SayText_APlayersLine_PlaysHudChatMessage()
    {
        TfHudChat chat = Built();
        List<string> sounds = [];

        chat.SoundEmitter = sounds.Add;
        chat.HandleUserMessage(Message(SceneUserMessage.SayText, [2], "hi", [1]), State());

        sounds.ShouldBe(["HudChat.Message"]);
    }

    [Test]
    public void HltvChat_TheEvent_IsTheLocalPlayersSourceTvLine()
    {
        TfHudChat chat = Built();
        SceneGameEvent said = new(0, "hltv_chat", new Dictionary<string, object?> { ["text"] = "caster" }, new Dictionary<int, PlayerInfo>());

        chat.HandleGameEvent(said, State());

        chat.History.Text.ShouldBe("\n(SourceTV) caster");
    }

    [Test]
    public void InsertFade_AfterTheSaytextTime_TheLineFadesOut()
    {
        // `hud_saytext_time` 12, faded over the last `CHAT_HISTORY_IDLE_FADE_TIME` 2.5 of it, on the wall clock.
        TfHudChat chat = Built();

        chat.HandleUserMessage(SayText2(2, true, "TF_Chat_All", "Blue", "hi"), State());
        Clock = 100 + 13;

        Painted(chat).ShouldBeEmpty();
    }

    private static double Clock { get; set; } = 100;

    private static HudState State() =>
        new(true, true, 0, 125, true, CurTime: 5f, Team: 2, LocalIndex: 1,
            Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 125, 1), new ScenePlayer(2, 0f, 0f, 0f, 3, 125, 1)],
            Names: new Dictionary<int, string> { [1] = "Me", [2] = "Blue" });

    private static SceneUserMessage SayText2(int client, bool wantsToChat, params string[] strings) =>
        Message(SceneUserMessage.SayText2, [(byte)client, (byte)(wantsToChat ? 1 : 0)], strings);

    private static SceneUserMessage Message(int type, byte[] head, params string[] strings)
    {
        List<byte> body = [.. head];

        foreach (string text in strings)
        {
            body.AddRange(Encoding.UTF8.GetBytes(text));
            body.Add(0);
        }

        return new SceneUserMessage(0, type, body.ToArray());
    }

    private static SceneUserMessage Message(int type, byte[] head, string text, byte[] tail) =>
        new(0, type, (byte[])[.. head, .. Encoding.UTF8.GetBytes(text), 0, .. tail]);

    /// <summary>Each run the history draws, and the colour it is drawn in.</summary>
    private static List<(string Text, string Colour)> Painted(TfHudChat chat)
    {
        TextRecorder surface = new();

        chat.History.Think();
        chat.History.Paint(surface, null!);

        return [.. surface.Runs];
    }

    private static TfHudChat Built()
    {
        Clock = 100;

        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes($$"""
                Scheme
                {
                    Colors { }
                    BaseSettings { "TFColors.ChatTextBlue" "{{BlueName}}" "TFColors.ChatTextRed" "70 80 90 255" "TFColors.ChatTextYellow" "{{Yellow}}" }
                    Borders { }
                    Fonts { }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, string> strings = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["TF_Chat_All"] = "\u0003%s1\u0001 :  %s2",
            ["game_joined"] = "%s1 joined",
        };
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Localize = strings.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudChat chat = new(viewport) { Wide = 280, Tall = 120 };

        chat.PerformApplySchemeSettings(context);
        chat.History.Clock = () => Clock;
        chat.History.TextFont = new VguiFontAmalgam();
        chat.History.Surface = new TextRecorder();
        (chat.History.Wide, chat.History.Tall) = (260, 200);
        return chat;
    }
}
