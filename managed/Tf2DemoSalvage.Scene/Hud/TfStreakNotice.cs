using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFPlayerShared::ETFStreak` (tf_player_shared.h:272).</summary>
public enum TfStreakType
{
    /// <summary>`kTFStreak_Kills`.</summary>
    Kills = 0,

    /// <summary>`kTFStreak_Ducks`.</summary>
    Ducks = 2,

    /// <summary>`kTFStreak_Duck_levelup`.</summary>
    DuckLevelUp = 3,
}

/// <summary>`CTFStreakNotice` (game/client/tf/tf_hud_deathnotice.cpp:82): the kill and duck streak banner.</summary>
/// <remarks>
/// The death notice makes the one that hears streaks (`new CTFStreakNotice( "KillStreakNotice" )`, :677); it is not a
/// registered HUD element, so `CHud::Think` never asks its `ShouldDraw` and it shows and hides itself. Its clock is
/// `gpGlobals->realtime`, not the demo's. `cl_hud_killstreak_display_time` 3, `…_fontsize` 0 and `…_alpha` 120 are the
/// convar defaults (:53). A string's `\x01`–`\x03` mark where the text turns normal, team and second colour.
/// **One sound name for every streak type, and that is Valve's own dead code rather than a gap here**:
/// `pszSoundName` defaults to `"Game.KillStreak"` (:399) and every tier- and type-specific override after it —
/// `"Announcer.DuckStreak_Level…"`, `"Announcer.KillStreak_Level…"` — is commented out (:290–305, :450–472), so the
/// shipped client plays `Game.KillStreak` for a duck streak exactly as it does for a kill streak. Gated only on
/// `iLocalPlayerIndex == iKillerID` (:534), with no type test at all.
/// **ponytail:** made once, where the game makes a new one each time the death notice's scheme is applied and leaves the
/// last one fading; only a resolution change mid-banner differs.
/// </remarks>
public sealed class TfStreakNotice : VguiEditablePanel
{
    private const int StreakMin = 5;
    private const int StreakMinMannVsMachine = 20;
    private const int StreakMinDucks = 10;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const float FadeTime = 1.5f;

    private static readonly (byte, byte, byte, byte) Normal = (235, 226, 202, 255);
    private static readonly (byte, byte, byte, byte) Red = (255, 64, 64, 255);
    private static readonly (byte, byte, byte, byte) Blue = (153, 204, 255, 255);

    private readonly VguiEditablePanel _background;
    private readonly TfExLabel _label;
    private readonly UniformRandomStream _random = new();
    private float _lastMessageTime = -10f;
    private int _labelY;
    private int _screenWide = 640;
    private int _screenTall = 480;

    /// <summary>`CTFStreakNotice( pName )`: parented to the viewport, with its background and label.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfStreakNotice(VguiPanel viewport)
        : base(viewport, "KillStreakNotice")
    {
        _background = new VguiEditablePanel(this, "Background");
        _label = new TfExLabel(this, "SplashLabel");
    }

    /// <summary>`cl_hud_killstreak_display_time.GetFloat()`, as `IsCurrentStreakHigherPriority` reads it.</summary>
    private float DisplayTime => HudViewport.ConVarsOf(this).GetFloat("cl_hud_killstreak_display_time");

    /// <summary>`cl_hud_killstreak_display_time.GetInt()`, as `AddStreakMsg` and `Paint` read it.</summary>
    internal int DisplayTimeInt => HudViewport.ConVarsOf(this).GetInt("cl_hud_killstreak_display_time");

    /// <summary>`cl_hud_killstreak_display_fontsize.GetInt()`.</summary>
    private int FontSize => HudViewport.ConVarsOf(this).GetInt("cl_hud_killstreak_display_fontsize");

    /// <summary>`cl_hud_killstreak_display_alpha.GetInt()`.</summary>
    private int DisplayAlpha => HudViewport.ConVarsOf(this).GetInt("cl_hud_killstreak_display_alpha");

    /// <summary>`m_nCurrStreakCount`.</summary>
    public int CurrentStreakCount { get; private set; }

    /// <summary>`m_nCurrStreakType`.</summary>
    public TfStreakType CurrentStreakType { get; private set; }

    /// <summary>Plays a `game_sounds.txt` script — see <see cref="HudSoundEmitter"/>.</summary>
    public HudSoundEmitter? SoundEmitter { get; set; }

    /// <summary>The label's text, the colour marks included.</summary>
    public string Text => _label.Text;

    /// <summary>The label's colour changes.</summary>
    public IReadOnlyList<(int Index, (byte, byte, byte, byte) Color)> ColorChanges => _label.TextImage.ColorChanges;

    /// <summary>`MinStreakForType` (:59).</summary>
    /// <param name="type">The streak.</param>
    /// <param name="mannVsMachine">`IsMannVsMachineMode()`.</param>
    /// <returns>The shortest streak worth a banner.</returns>
    public static int MinStreakForType(TfStreakType type, bool mannVsMachine) => type switch
    {
        TfStreakType.Ducks => StreakMinDucks,
        TfStreakType.DuckLevelUp => 1,
        _ => mannVsMachine ? StreakMinMannVsMachine : StreakMin,
    };

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HudKillStreakNotice.res", context);
        _labelY = _label.Y;
        (_screenWide, _screenTall) = (context.ScreenWide, context.ScreenTall);
        (Wide, Tall) = (XRes(640), YRes(480));
    }

    /// <summary>`StreakUpdated` (:320).</summary>
    /// <param name="type">The streak.</param>
    /// <param name="player">Whose, by entity index.</param>
    /// <param name="streak">Its length.</param>
    /// <param name="increment">How much this kill added.</param>
    /// <param name="fired">The event, for names, teams and rules.</param>
    /// <param name="realTime">`gpGlobals->realtime`.</param>
    public void StreakUpdated(TfStreakType type, int player, int streak, int increment, HudGameEvent fired, float realTime)
    {
        ArgumentNullException.ThrowIfNull(fired);

        bool mannVsMachine = fired.Rules.MannVsMachine;
        int minimum = MinStreakForType(type, mannVsMachine);

        if (IsCurrentStreakHigherPriority(type, streak, realTime))
        {
            return;
        }

        int tier;

        if (type == TfStreakType.Ducks)
        {
            // "Notices at 15, 30, then increments of 50."
            if (streak >= 15 && streak - increment < 15)
            {
                (tier, streak) = (1, 15);
            }
            else if (streak >= 30 && streak - increment < 30)
            {
                (tier, streak) = (2, 30);
            }
            else if (streak > 50 && streak % 50 < increment)
            {
                tier = Math.Min(2 + (streak / 50), 5);
                streak -= streak % 50;
            }
            else
            {
                return;
            }
        }
        else if (type == TfStreakType.DuckLevelUp)
        {
            tier = 5;
        }
        else if (mannVsMachine)
        {
            if (streak % minimum != 0)
            {
                return;
            }

            tier = streak / minimum;
        }
        else
        {
            tier = streak switch
            {
                5 => 1,
                10 => 2,
                15 => 3,
                20 => 4,
                _ when streak % 10 is 0 or 5 => 5,
                _ => 0,
            };

            if (tier == 0)
            {
                return;
            }
        }

        CurrentStreakCount = streak;
        CurrentStreakType = type;

        ((byte, byte, byte, byte) custom, string key) = type switch
        {
            TfStreakType.DuckLevelUp => (((byte)255, (byte)215, (byte)0, (byte)255), "#Msg_DuckLevelup" + Math.Min(_random.RandomInt(1, 3), 3).ToString(CultureInfo.InvariantCulture)),
            TfStreakType.Ducks => (TierColor(tier), "#Msg_DuckStreak" + Math.Min(tier, 5).ToString(CultureInfo.InvariantCulture)),
            _ => (TierColor(tier), "#Msg_KillStreak" + Math.Min(tier, 5).ToString(CultureInfo.InvariantCulture)),
        };

        if (Find(key) is not { } format)
        {
            return;
        }

        string text = VguiLocalize.ConstructString(
            format, 256, Truncate(fired.PlayerName(player)), streak.ToString(CultureInfo.InvariantCulture));

        SetText(text, TeamColor(fired.Team(player)), custom);

        // "Play Local Sound" (:531): `Game.KillStreak` for every streak type — see the remarks above.
        if (player == fired.LocalPlayerIndex)
        {
            SoundEmitter?.Invoke("Game.KillStreak");
        }

        _lastMessageTime = realTime + (tier / 2.0f);
        Visible = true;
    }

    /// <summary>`StreakEnded` (:209).</summary>
    /// <param name="type">The streak.</param>
    /// <param name="killer">Who ended it.</param>
    /// <param name="victim">Whose it was.</param>
    /// <param name="streak">Its length.</param>
    /// <param name="fired">The event, for names and teams.</param>
    /// <param name="realTime">`gpGlobals->realtime`.</param>
    public void StreakEnded(TfStreakType type, int killer, int victim, int streak, HudGameEvent fired, float realTime)
    {
        ArgumentNullException.ThrowIfNull(fired);

        if (streak < 10 || IsCurrentStreakHigherPriority(type, streak, realTime))
        {
            return;
        }

        _lastMessageTime = realTime;

        bool self = killer == victim;
        string key = (type == TfStreakType.Ducks, self) switch
        {
            (true, true) => "#Msg_DuckStreakEndSelf",
            (true, false) => "#Msg_DuckStreakEnd",
            (false, true) => "#Msg_KillStreakEndSelf",
            (false, false) => "#Msg_KillStreakEnd",
        };

        if (Find(key) is not { } format)
        {
            return;
        }

        string killerName = Truncate(fired.PlayerName(killer));
        string count = streak.ToString(CultureInfo.InvariantCulture);
        string text = self
            ? VguiLocalize.ConstructString(format, 256, killerName, count)
            : VguiLocalize.ConstructString(format, 256, killerName, Truncate(fired.PlayerName(victim)), count);

        SetText(text, TeamColor(fired.Team(killer)), TeamColor(fired.Team(victim)));
        _lastMessageTime = realTime;
        Visible = true;
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:159): hidden past the display time, fading over its last 1.5 s, the label centred with its icon.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        HudState state = HudViewport.Of(this)?.State ?? default;
        // `clamp( cl_hud_killstreak_display_time.GetInt(), 1, 100 )` (tf_hud_deathnotice.cpp:161).
        int displayTime = Math.Clamp(DisplayTimeInt, 1, 100);

        if (_lastMessageTime + displayTime < state.RealTime)
        {
            Visible = false;
            CurrentStreakCount = 0;
            return;
        }

        float remaining = displayTime - (state.RealTime - _lastMessageTime);

        Visible = true;
        SetAnimationValue("alpha", remaining > FadeTime ? 255f : (float)(int)RemapValClamped(remaining, FadeTime, 0f, 255f, 0f));

        HudTexture? icon = HudViewport.Of(this)?.Icons?.GetIcon(
            CurrentStreakType is TfStreakType.Ducks or TfStreakType.DuckLevelUp ? "eotl_duck" : "leaderboard_streak");

        if (state.HasLocalPlayer && icon is not null)
        {
            // "Move labels down when in spectator"
            int yOffset = state.ObserverMode > Core.Scene.ObserverModes.FreezeCam ? YRes(40) : 0;
            (int wide, int tall) = _label.GetContentSize();

            _label.SizeToContents();
            (_label.X, _label.Y) = (XRes(320) - (wide / 2), _labelY + yOffset);
            (_background.Wide, _background.Tall) = (wide + (tall / 2), tall);
            (_background.X, _background.Y) = (XRes(315) - (wide / 2), _labelY + yOffset);

            int textWide = 0;

            if (StreakFont() is { } font)
            {
                foreach (char character in _label.Text)
                {
                    textWide += surface.GetCharacterWidth(font, character);
                }
            }

            icon.DrawSelf(surface, XRes(320) - (wide / 2) + textWide, _labelY + yOffset, tall, tall, (235, 226, 202, (byte)(int)GetFloat("alpha")));
        }
    }

    /// <summary>`IsCurrentStreakHigherPriority` (:550).</summary>
    private bool IsCurrentStreakHigherPriority(TfStreakType type, int streak, float realTime)
    {
        if (type == TfStreakType.DuckLevelUp || CurrentStreakCount == 0)
        {
            return false;
        }

        // "Ducks never override kills", "But kills always override ducks".
        if (CurrentStreakType == TfStreakType.Kills && type == TfStreakType.Ducks)
        {
            return true;
        }

        if (CurrentStreakType == TfStreakType.Ducks && type == TfStreakType.Kills)
        {
            return false;
        }

        float elapsed = realTime - _lastMessageTime;
        float minimumTime = Math.Max(DisplayTime / 3f, 1f);

        return streak < CurrentStreakCount && elapsed < minimumTime;
    }

    /// <summary>The label's font, text and colour stream: `\x01` normal, `\x02` the first colour, `\x03` the second.</summary>
    private void SetText(string text, (byte, byte, byte, byte) second, (byte, byte, byte, byte) third)
    {
        if (StreakFont() is { } font && !ReferenceEquals(_label.TextFont, font))
        {
            _label.TextFont = font;
        }

        _label.SetText(text, null);
        _label.TextImage.ClearColorChangeStream();

        byte alpha = (byte)DisplayAlpha;

        for (int index = 0; index < text.Length; index++)
        {
            (byte r, byte g, byte b, byte _) = text[index] switch
            {
                '\u0001' => Normal,
                '\u0002' => second,
                '\u0003' => third,
                _ => default,
            };

            if (text[index] is '\u0001' or '\u0002' or '\u0003')
            {
                _label.TextImage.AddColorChange((r, g, b, alpha), index);
            }
        }
    }

    /// <summary>`GetStreakFont` (:574): smallest bold, small bold at 1, medium-small bold at 2, proportional.</summary>
    private VguiFontAmalgam? StreakFont()
    {
        string name = FontSize switch
        {
            1 => "HudFontSmallBold",
            2 => "HudFontMediumSmallBold",
            _ => "HudFontSmallestBold",
        };

        return HudViewport.Of(this)?.Context?.GetFont(name, proportional: true);
    }

    private static (byte, byte, byte, byte) TierColor(int tier) => tier switch
    {
        1 => (112, 176, 74, 255),
        2 => (207, 106, 50, 255),
        3 => (134, 80, 172, 255),
        _ => (255, 215, 0, 255),
    };

    private static (byte, byte, byte, byte) TeamColor(int team) => team switch
    {
        TeamRed => Red,
        TeamBlue => Blue,
        _ => Normal,
    };

    /// <summary>`ConvertANSIToUnicode` into `wchar_t[MAX_PLAYER_NAME_LENGTH / 2]`: at most 15 characters.</summary>
    private static string Truncate(string name) => name[..Math.Min(name.Length, 15)];

    /// <summary>`RemapValClamped` (mathlib.h:619) for distinct `a` and `b` — its `A == B` arm never runs here.</summary>
    private static float RemapValClamped(float value, float a, float b, float c, float d) =>
        c + ((d - c) * Math.Clamp((value - a) / (b - a), 0f, 1f));

    private string? Find(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token[1..]);

    private int XRes(int x) => (int)(x * (_screenWide / 640.0));

    private int YRes(int y) => (int)(y * (_screenTall / 480.0));
}
