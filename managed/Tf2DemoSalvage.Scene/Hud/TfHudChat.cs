using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudChat` over `CBaseHudChat` (game/client/tf/tf_hud_chat.cpp, game/client/hud_basechat.cpp): the chat history.</summary>
/// <remarks>
/// A demo types nothing, so what is here is the history: `SayText`, `SayText2` and `TextMsg` (`HUD_PRINTTALK`) and
/// `hltv_chat` become lines through `ChatPrintf` (:1731) and `InsertAndColorizeText` (:1403), each range in its colour —
/// a name in its team's `TFColors.ChatTextRed`/`Blue`, the rest `TFColors.ChatTextYellow`, from the chat's own
/// `ChatScheme.res` — then faded after `hud_saytext_time` (12) over `CHAT_HISTORY_IDLE_FADE_TIME` (2.5) on the wall clock.
/// `Clear` stops message mode and never empties the history, so a seek replays lines over those already faded.
/// **Not modelled:** the input line, message mode and the filters button, all invisible while nobody types; voice subtitles;
/// party chat; `titles.txt` lookups in `SayText` and `TextMsg`; the profanity filter and Steam-ignored players; muted
/// players; `IsInTraining`'s filter; the freeze-cam screenshot; `COLOR_CUSTOM`, which nothing in TF sets; and the
/// `SourceScheme` of `COLOR_ACHIEVEMENT`, taken as the chat's own.
/// </remarks>
public sealed class TfHudChat : VguiEditablePanel, IHudElement
{
    private const float HistoryIdleFadeTime = 2.5f;
    private const float HistoryFadeTime = 0.25f;
    private const int HistoryAlpha = 127;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int ChatFilterNone = 0;
    private const int ChatFilterNameChange = 0x02;
    private const int ChatFilterPublicChat = 0x04;
    private const int ChatFilterTeamChange = 0x10;
    private const int HudPrintTalk = 3;

    private const char ColorNormal = (char)1;
    private const char ColorUseOldColors = (char)2;
    private const char ColorPlayerName = (char)3;
    private const char ColorLocation = (char)4;
    private const char ColorAchievement = (char)5;
    private const char ColorCustom = (char)6;
    private const char ColorHexCode = (char)7;
    private const char ColorHexCodeAlpha = (char)8;
    private const char ColorMax = (char)9;

    private static readonly (byte, byte, byte, byte) ColorGreen = (153, 255, 153, 255);
    private static readonly (byte, byte, byte, byte) ColorDarkGreen = (64, 255, 64, 255);
    private static readonly (byte, byte, byte, byte) ColorYellow = (255, 178, 0, 255);
    private static readonly (byte, byte, byte, byte) ColorGrey = (204, 204, 204, 255);
    private static readonly (byte, byte, byte, byte) ColorRed = (255, 63, 63, 255);
    private static readonly (byte, byte, byte, byte) ColorBlue = (153, 204, 255, 255);

    private VguiContext? _scheme;

    /// <summary>`CHudChat( "CHudChat" )`: the history (fades cleared), the invisible chat line, under the viewport at -30.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudChat(VguiPanel viewport)
        : base(viewport, "HudChat")
    {
        ZPos = -30;
        History = new TfHudChatHistory(this, "HudChatHistory");
        History.SetMaximumCharCount(127 * 100);
        ChatLine = new TfHudChatLine(this, "ChatLine1") { Visible = false };
    }

    /// <summary>`m_pChatHistory`.</summary>
    public VguiRichText History { get; }

    /// <summary>`m_ChatLine`: never shown; its font sizes the history.</summary>
    public VguiRichText ChatLine { get; }

    /// <summary>`cl_chatfilters`: 63, every filter on.</summary>
    public int FilterFlags { get; set; } = 63;

    /// <summary>`hud_saytext_time` (hud_basechat.cpp:37): how long a line stays before its idle fade.</summary>
    public float SayTextTime { get; set; } = 12f;

    /// <summary>Plays a `game_sounds.txt` script — see <see cref="HudSoundEmitter"/>.</summary>
    public HudSoundEmitter? SoundEmitter { get; set; }

    /// <summary>`m_flHistoryFadeTime`, set only by message mode, which a demo never enters.</summary>
    public float HistoryFadeTimeAt { get; set; }

    /// <inheritdoc/>
    public override string ClassName => "CHudChat";

    /// <inheritdoc/>
    public int HiddenBits => HudVisibility.HideChat;

    /// <summary>The events `Init` listens for.</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(["hltv_chat"], StringComparer.Ordinal);

    /// <inheritdoc/>
    /// <remarks>`BaseChat.res`, then a painted, bordered background of `DullWhite` at 127, and no history scroll bar.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _scheme = context;
        LoadControlSettings("resource/UI/BaseChat.res", context);
        base.ApplySchemeSettings(context);
        PaintBackgroundEnabled = true;
        PaintBorderEnabled = true;

        (byte red, byte green, byte blue, _) = context.Scheme.GetColor("DullWhite", BgColor);

        BgColor = (red, green, blue, HistoryAlpha);
        History.SetVerticalScrollbar(false);
    }

    /// <summary>`OnTick` (:1064): the history sized to the chat less two and a quarter line heights, then `FadeChatHistory`.</summary>
    protected override void OnThink()
    {
        HudState state = HudViewport.Of(this)?.State ?? default;

        if (ChatLine.TextFont is { } font && ChatLine.Surface is { } surface)
        {
            int fontHeight = surface.GetFontTall(font) + 2;

            History.Tall = (int)(Tall - (fontHeight * 2.25)) - History.Y;
        }

        // `FadeChatHistory` (:1291), without mouse input: the backgrounds at the fade's alpha — 0 outside message mode.
        int alpha = Math.Clamp((int)((HistoryFadeTimeAt - state.CurTime) / HistoryFadeTime * HistoryAlpha), 0, HistoryAlpha);

        History.BgColor = (0, 0, 0, (byte)alpha);
        BgColor = BgColor with { Alpha = (byte)alpha };
    }

    /// <summary>`MsgFunc_SayText`, `MsgFunc_SayText2` and `MsgFunc_TextMsg`, read from the body as the engine's `bf_read` does.</summary>
    /// <param name="message">The user message.</param>
    /// <param name="state">The game state when it arrived.</param>
    public void HandleUserMessage(SceneUserMessage message, HudState state)
    {
        ArgumentNullException.ThrowIfNull(message);

        BodyReader body = new(message.Body.Span);

        switch (message.Type)
        {
            case SceneUserMessage.SayText:
                SayText(ref body, state);
                break;
            case SceneUserMessage.SayText2:
                SayText2(ref body, state);
                break;
            case SceneUserMessage.TextMsg:
                TextMsg(ref body, state);
                break;
            default:
                break;
        }
    }

    /// <summary>`FireGameEvent` (:1857): `hltv_chat` as the local player's "(SourceTV)" line.</summary>
    /// <param name="fired">The event.</param>
    /// <param name="state">The game state.</param>
    public void HandleGameEvent(SceneGameEvent fired, HudState state)
    {
        ArgumentNullException.ThrowIfNull(fired);

        if (fired.Name == "hltv_chat" && state.HasLocalPlayer)
        {
            ChatPrintf(state.LocalIndex, ChatFilterNone, "(SourceTV) " + fired.GetString("text"), state);
        }
    }

    /// <summary>`MsgFunc_SayText` (:766): a player's raw text, or a server string printed as the console.</summary>
    private void SayText(ref BodyReader body, HudState state)
    {
        int client = body.ReadByte();
        string text = body.ReadString();

        if (body.ReadByte() != 0)
        {
            ChatPrintf(client, ChatFilterNone, text, state);
        }
        else
        {
            ChatPrintf(0, ChatFilterNone, text, state);
        }

        // `HudChat.Message`, unconditional on either branch (hud_basechat.cpp:793).
        SoundEmitter?.Invoke("HudChat.Message");
    }

    /// <summary>`MsgFunc_SayText2` (:812): the format looked up, the name and text with their colour codes normalised, joined.</summary>
    private void SayText2(ref BodyReader body, HudState state)
    {
        int client = body.ReadByte();
        bool wantsToChat = body.ReadByte() != 0;
        string untranslated = body.ReadString();
        string format = Find(untranslated) ?? untranslated;
        string name = ReadChatTextString(ref body);
        string text = ReadChatTextString(ref body);
        string parameter3 = StripEndNewline(ReadLocalized(ref body));
        string parameter4 = StripEndNewline(ReadLocalized(ref body));
        string line = VguiLocalize.ConstructString(format, 256, name, text, parameter3, parameter4).Replace('\r', '\n');

        if (wantsToChat)
        {
            // Another team's chat is public chat to the filter.
            int filter = client > 0 && TeamOf(state, client) != TeamOf(state, state.LocalIndex) ? ChatFilterPublicChat : ChatFilterNone;

            ChatPrintf(client, filter, line, state);

            // `HudChat.Message`, only on the "wants to chat" branch (hud_basechat.cpp:858).
            SoundEmitter?.Invoke("HudChat.Message");
        }
        else
        {
            ChatPrintf(client, FilterForString(untranslated), line, state);
        }
    }

    /// <summary>`MsgFunc_TextMsg` (:881): five strings looked up, constructed; `HUD_PRINTTALK` printed as the console.</summary>
    private void TextMsg(ref BodyReader body, HudState state)
    {
        int destination = body.ReadByte();
        string[] strings = new string[5];

        for (int i = 0; i < strings.Length; i++)
        {
            string raw = body.ReadString();

            strings[i] = Find(raw) ?? (i > 0 ? StripEndNewline(raw) : raw);
        }

        if (destination != HudPrintTalk)
        {
            return;
        }

        string line = VguiLocalize.ConstructString(strings[0], 256, strings[1], strings[2], strings[3], strings[4]);

        if (line.Length > 0 && line[^1] is not '\n' and not '\r')
        {
            line += "\n";
        }

        ChatPrintf(0, ChatFilterNone, line.Replace('\r', '\n'), state);
    }

    /// <summary>`ChatPrintf` (:1731).</summary>
    private void ChatPrintf(int playerIndex, int filter, string message, HudState state)
    {
        if (message.EndsWith('\n'))
        {
            message = message[..^1];
        }

        // Empty once leading newlines and colour codes go: nothing to print.
        int visible = 0;

        while (visible < message.Length && (message[visible] == '\n' || (message[visible] > 0 && message[visible] < ColorMax)))
        {
            visible++;
        }

        if (visible == message.Length)
        {
            return;
        }

        string text = message.TrimStart('\n');

        if (filter != ChatFilterNone && (filter & GetFilterFlags(state)) == 0)
        {
            return;
        }

        string name = playerIndex == 0 ? "Console" : state.Names?.GetValueOrDefault(playerIndex) ?? string.Empty;
        int nameStart = 0;
        int nameLength = 0;

        if (name.Length > 0 && text.IndexOf(name, StringComparison.Ordinal) is >= 0 and var found)
        {
            nameStart = found;
            nameLength = name.Length;
        }

        InsertAndColorizeText(text, playerIndex, nameStart, nameLength, state);
    }

    /// <summary>`CBaseHudChatLine::InsertAndColorizeText` (:1403): the text split into coloured ranges at its colour codes.</summary>
    private void InsertAndColorizeText(string text, int clientIndex, int nameStart, int nameLength, HudState state)
    {
        List<(int Start, int End, (byte, byte, byte, byte) Color, bool PreserveAlpha)> ranges = [];

        if (text[0] is ColorPlayerName or ColorLocation or ColorNormal or ColorAchievement or ColorCustom or ColorHexCode or ColorHexCodeAlpha)
        {
            int at = 0;

            while (at < text.Length)
            {
                (int Start, int End, (byte, byte, byte, byte) Color, bool PreserveAlpha)? range = null;
                int bytesIn = at;
                char code = text[at];

                if (code is ColorCustom or ColorPlayerName or ColorLocation or ColorAchievement or ColorNormal)
                {
                    range = (bytesIn + 1, text.Length, TextColorForClient(code, clientIndex, state), false);
                    at++;
                }
                else if (code is ColorHexCode or ColorHexCodeAlpha)
                {
                    bool readAlpha = code == ColorHexCodeAlpha;
                    int codeBytes = readAlpha ? 8 : 6;
                    int start = bytesIn + codeBytes + 1;

                    at++;

                    // "Not enough characters remaining for a hex code. Skip the rest of the string."
                    if (text.Length <= start)
                    {
                        break;
                    }

                    byte alpha = readAlpha ? Hex(text, at + 6) : (byte)255;

                    range = (start, text.Length, (Hex(text, at), Hex(text, at + 2), Hex(text, at + 4), alpha), readAlpha);
                    at += codeBytes;
                }
                else
                {
                    at++;
                }

                if (range is { } found)
                {
                    if (ranges.Count > 0)
                    {
                        ranges[^1] = ranges[^1] with { End = bytesIn };
                    }

                    ranges.Add(found);
                }
            }
        }

        if (ranges.Count == 0 && nameLength > 0 && text[0] == ColorUseOldColors)
        {
            ranges.Add((0, nameStart, TextColorForClient(ColorNormal, clientIndex, state), false));
            ranges.Add((nameStart, nameStart + nameLength, TextColorForClient(ColorPlayerName, clientIndex, state), false));
            ranges.Add((nameStart + nameLength, text.Length, TextColorForClient(ColorNormal, clientIndex, state), false));
        }

        if (ranges.Count == 0)
        {
            ranges.Add((0, text.Length, TextColorForClient(ColorNormal, clientIndex, state), false));
        }

        // A range starting on a colour code starts past it.
        for (int i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Start < text.Length && text[ranges[i].Start] > 0 && text[ranges[i].Start] < ColorMax)
            {
                ranges[i] = ranges[i] with { Start = ranges[i].Start + 1 };
            }
        }

        Colorize(text, ranges);
    }

    /// <summary>`CBaseHudChatLine::Colorize` (:1547), into the history: a new line, then each range in its colour, faded.</summary>
    private void Colorize(string text, List<(int Start, int End, (byte, byte, byte, byte) Color, bool PreserveAlpha)> ranges)
    {
        History.InsertString("\n");

        for (int i = 0; i < ranges.Count; i++)
        {
            (int start, int end, (byte Red, byte Green, byte Blue, byte Alpha) color, bool preserveAlpha) = ranges[i];

            // `len = end - start + 1`, copied less its terminator: nothing when the range is empty.
            if (end - start + 1 <= 1)
            {
                continue;
            }

            if (!preserveAlpha)
            {
                color = color with { Alpha = 255 };
            }

            History.InsertColorChange(color);
            History.InsertString(text[start..Math.Min(end, text.Length)]);
            History.InsertFade(SayTextTime, HistoryIdleFadeTime);

            if (i == ranges.Count - 1)
            {
                History.InsertFade(-1f, -1f);
            }
        }
    }

    /// <summary>`CHudChat::GetTextColorForClient` (tf_hud_chat.cpp): a code's colour, opaque.</summary>
    private (byte, byte, byte, byte) TextColorForClient(char code, int clientIndex, HudState state)
    {
        (byte red, byte green, byte blue, _) = code switch
        {
            ColorPlayerName => ClientColor(clientIndex, state),
            ColorLocation => ColorDarkGreen,
            ColorCustom => default,
            _ => Scheme()?.GetColor("TFColors.ChatTextYellow", BgColor) ?? ColorYellow,
        };

        return (red, green, blue, 255);
    }

    /// <summary>`CHudChat::GetClientColor`: the console green, else the team's chat colour, else grey.</summary>
    private (byte, byte, byte, byte) ClientColor(int clientIndex, HudState state)
    {
        if (clientIndex == 0)
        {
            return ColorGreen;
        }

        return TeamOf(state, clientIndex) switch
        {
            TeamRed => Scheme()?.GetColor("TFColors.ChatTextRed", ColorRed) ?? ColorRed,
            TeamBlue => Scheme()?.GetColor("TFColors.ChatTextBlue", ColorBlue) ?? ColorBlue,
            _ => ColorGrey,
        };
    }

    /// <summary>`CHudChat::GetFilterFlags`: `cl_chatfilters`, less team changes in arena.</summary>
    private int GetFilterFlags(HudState state) =>
        state.Rules.GameType == SceneGameRules.GameTypeArena ? FilterFlags & ~ChatFilterTeamChange : FilterFlags;

    /// <summary>`CHudChat::GetFilterForString`: a name change is filtered as one.</summary>
    private static int FilterForString(string text)
    {
        bool nameChange = text.Equals("#HL_Name_Change", StringComparison.OrdinalIgnoreCase)
            || text.Equals("#TF_Name_Change", StringComparison.OrdinalIgnoreCase);

        return nameChange ? ChatFilterNameChange : ChatFilterNone;
    }

    private static int TeamOf(HudState state, int index) => state.Player(index)?.Team ?? 0;

    private VguiScheme? Scheme() => (_scheme ?? HudViewport.Of(this)?.Context)?.Scheme;

    /// <summary>`g_pVGuiLocalize->Find`: a token's string, with or without its `#`.</summary>
    private string? Find(string token)
    {
        Func<string, string?>? localize = (_scheme ?? HudViewport.Of(this)?.Context)?.Localize;

        if (token.Length == 0 || localize is null)
        {
            return null;
        }

        return localize(token[0] == '#' ? token[1..] : token);
    }

    private string ReadLocalized(ref BodyReader body)
    {
        string raw = body.ReadString();

        return Find(raw) ?? raw;
    }

    /// <summary>`ReadChatTextString` (:162): end newline stripped, every colour code — a hex code with its digits — made normal.</summary>
    private static string ReadChatTextString(ref BodyReader body)
    {
        StringBuilder text = new(StripEndNewline(body.ReadString()));
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == 0 || text[i] >= ColorMax)
            {
                i++;
                continue;
            }

            // A hex code marks itself and its six or eight digits; anything else is one character.
            int marked = text[i] switch
            {
                ColorHexCode => 7,
                ColorHexCodeAlpha => 9,
                _ => 1,
            };
            int end = Math.Min(i + marked, text.Length);

            for (int k = i; k < end; k++)
            {
                text[k] = ColorNormal;
            }

            i = end;
        }

        return text.ToString();
    }

    private static string StripEndNewline(string text) => text.Length > 0 && text[^1] is '\n' or '\r' ? text[..^1] : text;

    private static byte Hex(string text, int at) => (byte)((Nibble(text[at]) << 4) | Nibble(text[at + 1]));

    /// <summary>`V_nibble`.</summary>
    private static int Nibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => '0',
    };

    /// <summary>`bf_read` over a user message body: bytes and null-terminated strings, as a user message's are byte-aligned.</summary>
    private ref struct BodyReader(ReadOnlySpan<byte> body)
    {
        private readonly ReadOnlySpan<byte> _body = body;
        private int _at;

        public int ReadByte() => _at < _body.Length ? _body[_at++] : 0;

        /// <summary>`ReadString`: up to the terminator, as UTF-8, which `ConvertANSIToUnicode` reads it as.</summary>
        public string ReadString()
        {
            int start = _at;

            while (_at < _body.Length && _body[_at] != 0)
            {
                _at++;
            }

            string text = Encoding.UTF8.GetString(_body[start.._at]);

            if (_at < _body.Length)
            {
                _at++;
            }

            return text;
        }
    }
}

/// <summary>`CHudChatHistory` (hud_basechat.cpp:587): the chat's `RichText`, in `ChatFont`, fully opaque.</summary>
/// <param name="parent">The chat.</param>
/// <param name="name">"HudChatHistory".</param>
public sealed class TfHudChatHistory(VguiPanel? parent, string? name) : VguiRichText(parent, name)
{
    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        SetFont(context.GetFont("ChatFont", proportional: true));
        SetAnimationValue("alpha", 255f);
    }
}

/// <summary>`CHudChatLine` (tf_hud_chat.cpp): the hidden line, whose `ChatFont` — not proportional — sizes the history.</summary>
/// <param name="parent">The chat.</param>
/// <param name="name">"ChatLine1".</param>
public sealed class TfHudChatLine(VguiPanel? parent, string? name) : VguiRichText(parent, name)
{
    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        BgColor = (0, 0, 0, 0);
        SetFgColor((0, 0, 0, 0));
        SetFont(context.GetFont("ChatFont", proportional: false));
    }
}
