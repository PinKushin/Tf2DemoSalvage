using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>One game event as a HUD listener handles it, with what it asks of the world while it does.</summary>
/// <param name="Event">The event, with the `userinfo` table as it stood.</param>
/// <param name="CurTime">`gpGlobals->curtime` when it fired: its tick's time.</param>
/// <param name="Players">The players at its tick — `UTIL_PlayerByIndex`, and `g_PR->GetTeam` through their teams.</param>
/// <param name="LocalPlayerIndex">`GetLocalPlayerIndex()`, or 0 with no local player.</param>
/// <param name="Rules">The game rules at its tick.</param>
/// <param name="MapName">`engine->GetLevelName()`: `maps/NAME.bsp`.</param>
/// <param name="LocalVisionFlags">`GetLocalPlayerVisionFilterFlags()`.</param>
public sealed record HudGameEvent(
    SceneGameEvent Event,
    float CurTime,
    IReadOnlyList<ScenePlayer> Players,
    int LocalPlayerIndex,
    SceneGameRules Rules,
    string MapName,
    int LocalVisionFlags)
{
    /// <summary>`gpGlobals->realtime` when it was handled — the frame's wall clock, which the streak banner runs on.</summary>
    public float RealTime { get; init; }

    /// <summary>`MAX_PLAYERS` for TF (shareddefs.h:255).</summary>
    public const int MaxPlayers = 101;

    /// <summary>`UTIL_PlayerByIndex`: the player entity in that slot, or null.</summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The player.</returns>
    public ScenePlayer? Player(int index)
    {
        foreach (ScenePlayer player in Players)
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>`C_PlayerResource::GetTeam` (c_playerresource.cpp:172): 0 outside 1–101.</summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The team.</returns>
    /// <remarks>**Interpolated:** the resource's `m_iTeam` is read as the player entity's team, which the server copies into it.</remarks>
    public int Team(int index) => index is < 1 or > MaxPlayers ? 0 : Player(index)?.Team ?? 0;

    /// <summary>`C_PlayerResource::GetPlayerName` (c_playerresource.cpp:145): `ERRORNAME` outside 1–101, `unconnected` for an empty slot.</summary>
    /// <param name="index">The entity index.</param>
    /// <returns>The name.</returns>
    public string PlayerName(int index)
    {
        if (index is < 1 or > MaxPlayers)
        {
            return "ERRORNAME";
        }

        return Event.Roster.TryGetValue(index, out Core.Net.PlayerInfo player) ? player.Name : "unconnected";
    }

    /// <summary>`GetLocalPlayerTeam()`: the local player's team, 0 with none.</summary>
    public int LocalPlayerTeam => Player(LocalPlayerIndex)?.Team ?? 0;
}

/// <summary>`DeathNoticeItem` (hud_basedeathnotice.h): one line of the feed.</summary>
public sealed class DeathNoticeItem
{
    /// <summary>`Killer.szName`.</summary>
    public string KillerName { get; set; } = string.Empty;

    /// <summary>`Killer.iTeam`.</summary>
    public int KillerTeam { get; set; }

    /// <summary>`Victim.szName`.</summary>
    public string VictimName { get; set; } = string.Empty;

    /// <summary>`Victim.iTeam`.</summary>
    public int VictimTeam { get; set; }

    /// <summary>`szIcon`.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>`wzInfoText`.</summary>
    public string InfoText { get; set; } = string.Empty;

    /// <summary>`wzInfoTextEnd`.</summary>
    public string InfoTextEnd { get; set; } = string.Empty;

    /// <summary>`wzPreKillerText`.</summary>
    public string PreKillerText { get; set; } = string.Empty;

    /// <summary>`iconDeath`.</summary>
    public HudTexture? IconDeath { get; set; }

    /// <summary>`iconCritDeath`.</summary>
    public HudTexture? IconCritDeath { get; set; }

    /// <summary>`iconPreKillerName`.</summary>
    public HudTexture? IconPreKillerName { get; set; }

    /// <summary>`iconPostKillerName`.</summary>
    public HudTexture? IconPostKillerName { get; set; }

    /// <summary>`iconPostVictimName`.</summary>
    public HudTexture? IconPostVictimName { get; set; }

    /// <summary>`bSelfInflicted`.</summary>
    public bool SelfInflicted { get; set; }

    /// <summary>`bLocalPlayerInvolved`.</summary>
    public bool LocalPlayerInvolved { get; set; }

    /// <summary>`bCrit`.</summary>
    public bool Crit { get; set; }

    /// <summary>`flCreationTime`.</summary>
    public float CreationTime { get; set; }

    /// <summary>`iWeaponID`.</summary>
    public int WeaponId { get; set; } = -1;

    /// <summary>`iKillerID` — a user id for a death, a player index for a special score.</summary>
    public int KillerId { get; set; } = -1;

    /// <summary>`iVictimID`.</summary>
    public int VictimId { get; set; } = -1;

    /// <summary>`iCount`.</summary>
    public int Count { get; set; }

    /// <summary>`bSpecialScore`.</summary>
    public bool SpecialScore { get; set; }
}

/// <summary>`CTFHudDeathNotice` over `CHudBaseDeathNotice` (game/client/tf/tf_hud_deathnotice.cpp, hud_basedeathnotice.cpp): the kill feed.</summary>
/// <remarks>
/// Every rule is the two files' own; each method names its source. The strings are the engine's buffers: a name is 64 bytes
/// of UTF-8, an info text 32 characters, an icon name 32 bytes.
/// </remarks>
public sealed class TfHudDeathNotice : VguiPanel, IHudElement
{
    // tf_shareddefs.h:1806–1816.
    private const int DeathDomination = 0x0001;
    private const int DeathAssisterDomination = 0x0002;
    private const int DeathRevenge = 0x0004;
    private const int DeathAssisterRevenge = 0x0008;
    private const int DeathFeignDeath = 0x0020;
    private const int DeathPurgatory = 0x0100;
    private const int DeathMiniboss = 0x0200;
    private const int DeathAustralium = 0x0400;

    // DMG_CRITICAL is DMG_ACID (tf_shareddefs.h:1164); the rest are shareddefs.h's.
    private const int DamageCritical = 1 << 20;
    private const int DamageFall = 1 << 5;
    private const int DamageVehicle = 1 << 4;
    private const int DamageNerveGas = 1 << 16;

    // shareddefs.h / tf_shareddefs.h teams.
    private const int TeamUnassigned = 0;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int TeamHalloween = 5;
    private const int TeamPveInvaders = TeamBlue;
    private const int FirstGameTeam = 2;
    private const int TeamCount = 2;

    // TF_FLAGEVENT_* (tf_shareddefs.h:880).
    private const int FlagPickup = 1;
    private const int FlagCapture = 2;
    private const int FlagDefend = 3;

    // HALLOWEEN_SCENARIO_* (tf_gamerules.h:1245).
    private const int ScenarioLakeside = 3;
    private const int ScenarioHightower = 4;
    private const int ScenarioDoomsday = 5;

    // TF_VISION_FILTER_PYRO (shareddefs.h:977).
    private const int VisionPyro = 1;

    private const int NameBytes = 64;
    private const int IconBytes = 32;
    private const int InfoChars = 32;
    private const int CornerCoordinates = 10;

    private static readonly string[] LocalizedObjectNames = ["#TF_Object_Dispenser", "#TF_Object_Tele", "#TF_Object_Sentry", "#TF_object_Sapper"];

    private static readonly string[] RuneIcons =
    [
        "mannpower_strength", "mannpower_haste", "mannpower_regen", "mannpower_resist", "mannpower_vamp", "mannpower_reflect",
        "mannpower_precision", "mannpower_agility", "mannpower_fist", "mannpower_king", "mannpower_plague", "mannpower_supernova",
    ];

    private readonly List<DeathNoticeItem> _notices = [];
    private readonly (float X, float Y)[] _cornerCoordinates = new (float, float)[CornerCoordinates];
    private HudTexture? _iconDomination;
    private HudTexture? _iconKillStreak;
    private HudTexture? _iconKillStreakDNeg;
    private HudTexture? _iconDuckStreak;
    private HudTexture? _iconDuckStreakDNeg;
    private int _screenWide = 640;
    private int _screenTall = 480;

    /// <summary>`CHudBaseDeathNotice( pElementName )`: "HudDeathNotice", parented to the viewport, with its panel variables.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudDeathNotice(VguiPanel viewport)
        : base(viewport, "HudDeathNotice")
    {
        DeclareAnimationVar("LineHeight", VguiPanelVarType.ProportionalFloat, "16");
        DeclareAnimationVar("LineSpacing", VguiPanelVarType.ProportionalFloat, "4");
        DeclareAnimationVar("CornerRadius", VguiPanelVarType.ProportionalFloat, "3");
        DeclareAnimationVar("MaxDeathNotices", VguiPanelVarType.Real, "4");
        DeclareAnimationVar("RightJustify", VguiPanelVarType.Bool, "1");
        DeclareAnimationVar("TextFont", VguiPanelVarType.Font, "Default");
        DeclareAnimationVar("IconColor", VguiPanelVarType.Color, "255 80 0 255");
        DeclareAnimationVar("BaseBackgroundColor", VguiPanelVarType.Color, "46 43 42 220");
        DeclareAnimationVar("LocalBackgroundColor", VguiPanelVarType.Color, "245 229 196 200");
        DeclareAnimationVar("KillStreakBackgroundColor", VguiPanelVarType.Color, "224 223 219 200");
        DeclareAnimationVar("TeamBlue", VguiPanelVarType.Color, "153 204 255 255");
        DeclareAnimationVar("TeamRed", VguiPanelVarType.Color, "255 64 64 255");
        DeclareAnimationVar("PurpleText", VguiPanelVarType.Color, "134 80 172 255");
        DeclareAnimationVar("GreenText", VguiPanelVarType.Color, "112 176 74 255");
        DeclareAnimationVar("LocalPlayerColor", VguiPanelVarType.Color, "65 65 65 255");
    }

    /// <summary>`hud_deathnotice_time`, default 6 (hud_basedeathnotice.cpp:31).</summary>
    public float NoticeTime { get; set; } = 6f;

    /// <summary>Plays a `game_sounds.txt` script for this feed and the streak banner it owns — see <see cref="HudSoundEmitter"/>.</summary>
    public HudSoundEmitter? SoundEmitter
    {
        get => _soundEmitter;
        set
        {
            _soundEmitter = value;

            if (Streak is { } streak)
            {
                streak.SoundEmitter = value;
            }
        }
    }

    private HudSoundEmitter? _soundEmitter;

    /// <summary>`m_DeathNotices`, oldest first.</summary>
    public IReadOnlyList<DeathNoticeItem> Notices => _notices;

    /// <inheritdoc/>
    public int HiddenBits => 0;

    /// <summary>The events `Init` listens for — the base's (hud_basedeathnotice.cpp:64), then TF's (tf_hud_deathnotice.cpp:651).</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "player_death", "object_destroyed", "teamplay_point_captured", "teamplay_capture_blocked", "teamplay_flag_event",
        "rd_robot_killed", "special_score", "team_leader_killed", "fish_notice", "fish_notice__arm", "duck_xp_level_up",
        "slap_notice", "pass_get", "pass_ball_stolen", "pass_score", "pass_pass_caught", "pass_ball_blocked",
    };

    /// <summary>`CTFHudDeathNotice::ShouldDraw`: always — the base's `m_DeathNotices.Count()` test is overridden away.</summary>
    /// <param name="state">Unread.</param>
    /// <returns>True.</returns>
    public bool ShouldDraw(HudState state) => true;

    /// <summary>`VidInit`: the feed emptied.</summary>
    public void Clear() => _notices.Clear();

    /// <inheritdoc/>
    /// <remarks>`CTFHudDeathNotice::ApplySchemeSettings`: the base's, no background, the corners; then the fixed icons.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySchemeSettings(context);
        PaintBackgroundEnabled = false;
        (_screenWide, _screenTall) = (context.ScreenWide, context.ScreenTall);
        CalcRoundedCorners();

        HudTextures? icons = HudViewport.Of(this)?.Icons;

        _iconDomination = icons?.GetIcon("leaderboard_dominated");
        _iconKillStreak = icons?.GetIcon("leaderboard_streak");
        _iconKillStreakDNeg = icons?.GetIcon("leaderboard_streak_dneg");
        _iconDuckStreak = icons?.GetIcon("eotl_duck");
        _iconDuckStreakDNeg = icons?.GetIcon("eotl_duck_dneg");

        // `m_pStreakNotice = new CTFStreakNotice( "KillStreakNotice" )`, a sibling on the viewport.
        if (Streak is null && Parent is { } viewport)
        {
            Streak = new TfStreakNotice(viewport) { SoundEmitter = SoundEmitter };
        }
    }

    /// <summary>`m_pStreakNotice`, made at the first scheme pass.</summary>
    public TfStreakNotice? Streak { get; private set; }

    /// <summary>`CTFHudDeathNotice::FireGameEvent` (tf_hud_deathnotice.cpp:758), then the base's (hud_basedeathnotice.cpp:399).</summary>
    /// <param name="fired">The event and the world it fired in.</param>
    public void HandleGameEvent(HudGameEvent fired)
    {
        ArgumentNullException.ThrowIfNull(fired);

        SceneGameEvent e = fired.Event;

        // `duck_xp_level_up` goes to the streak notice alone.
        if (e.Name == "duck_xp_level_up")
        {
            AddStreakMsg(fired, TfStreakType.DuckLevelUp, fired.LocalPlayerIndex, e.GetInt("level"), 1);
            return;
        }

        if (NoticeTime == 0f)
        {
            return;
        }

        int local = fired.LocalPlayerIndex;
        bool playerDeath = EventIsPlayerDeath(e.Name);
        bool objectDeath = e.Name == "object_destroyed";
        bool specialScore = e.Name == "special_score";
        bool teamLeaderKilled = false;
        bool feignDeath = (e.GetInt("death_flags") & DeathFeignDeath) != 0;

        if (playerDeath)
        {
            if (!ShouldShowDeathNotice(fired))
            {
                return;
            }

            if (feignDeath)
            {
                // "Only display fake death messages to the enemy team."
                int victimIndex = e.PlayerForUserId(e.GetInt("userid"));

                if ((fired.Player(victimIndex) is { } victim && fired.Player(local) is { } localPlayer
                    && !AreTeamsEnemies(localPlayer.Team ?? 0, victim.Team ?? 0)) || local == victimIndex)
                {
                    return;
                }
            }
        }

        int index = playerDeath || specialScore ? UseExistingNotice(e) : -1;

        if (index == -1)
        {
            _notices.Add(new DeathNoticeItem { CreationTime = fired.CurTime });
            index = _notices.Count - 1;
        }

        if (playerDeath || objectDeath)
        {
            if (!PlayerOrObjectDeath(fired, index))
            {
                return;
            }
        }
        else if (e.Name == "teamplay_point_captured")
        {
            PointCaptured(fired, index);
        }
        else if (e.Name == "teamplay_capture_blocked")
        {
            DeathNoticeItem msg = _notices[index];

            msg.VictimName = ControlPointName(e);
            msg.InfoText = Copy(Find("#Msg_Defended"));

            int blocker = e.GetInt("blocker");

            msg.KillerName = NameBuffer(fired.PlayerName(blocker));
            msg.KillerTeam = fired.Team(blocker);
            msg.LocalPlayerInvolved |= local == blocker;
        }
        else if (e.Name == "teamplay_flag_event")
        {
            if (!FlagEvent(fired, index))
            {
                return;
            }
        }
        else if (specialScore)
        {
            DeathNoticeItem msg = _notices[index];
            int scorer = e.GetInt("player");

            msg.KillerName = NameBuffer(scorer > 0 ? fired.PlayerName(scorer) : string.Empty);
            msg.KillerTeam = scorer > 0 ? fired.Team(scorer) : 0;
            msg.LocalPlayerInvolved = scorer == local;
            msg.KillerId = scorer;
            msg.Crit = false;
            msg.IconCritDeath = null;
            msg.SpecialScore = true;
            msg.Count++;
            msg.InfoText = VguiLocalize.ConstructString(Find("#SpecialScore_Count"), InfoChars, msg.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (e.Name == "team_leader_killed")
        {
            DeathNoticeItem msg = _notices[index];
            int killer = e.GetInt("killer");
            int victim = e.GetInt("victim");

            msg.KillerName = NameBuffer(killer > 0 ? fired.PlayerName(killer) : string.Empty);
            msg.KillerTeam = killer > 0 ? fired.Team(killer) : 0;
            msg.VictimName = NameBuffer(victim > 0 ? fired.PlayerName(victim) : string.Empty);
            msg.VictimTeam = victim > 0 ? fired.Team(victim) : 0;
            msg.LocalPlayerInvolved = killer == local || victim == local;
            msg.KillerId = killer;
            msg.VictimId = victim;
            msg.Crit = false;
            msg.IconCritDeath = null;

            if (Find("#TeamLeader_Kill") is { } text)
            {
                msg.InfoText = Copy(text);
            }

            teamLeaderKilled = true;
        }

        OnGameEvent(fired, index);

        if (!specialScore && !teamLeaderKilled && _notices[index].IconDeath is null)
        {
            // "Try and find the death identifier in the icon list", else the skull.
            DeathNoticeItem msg = _notices[index];

            msg.IconDeath = GetIcon(msg.Icon, msg.LocalPlayerInvolved) ?? GetIcon("d_skull_tf", msg.LocalPlayerInvolved);
        }
    }

    /// <summary>`RetireExpiredDeathNotices` (hud_basedeathnotice.cpp:339).</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void RetireExpiredDeathNotices(float curTime)
    {
        for (int i = _notices.Count - 1; i >= 0; i--)
        {
            if (curTime > ExpiryTime(_notices[i]))
            {
                _notices.RemoveAt(i);
            }
        }

        int max = (int)GetFloat("MaxDeathNotices");

        if (_notices.Count <= 0 || _notices.Count <= max)
        {
            return;
        }

        // "First, remove any notices not involving the local player", never the newest.
        int count = _notices.Count;
        int needToRemove = count - max;

        for (int i = 0; i < count - 1 && needToRemove > 0; i++)
        {
            if (!_notices[i].LocalPlayerInvolved)
            {
                _notices.RemoveAt(i);
                count--;
                needToRemove--;
            }
        }

        needToRemove = _notices.Count - max;

        for (int i = 0; i < needToRemove; i++)
        {
            _notices.RemoveAt(0);
        }
    }

    /// <inheritdoc/>
    /// <remarks>`CHudBaseDeathNotice::Paint` (hud_basedeathnotice.cpp:129).</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(context);

        RetireExpiredDeathNotices(HudViewport.Of(this)?.State.CurTime ?? 0f);

        int yStart = YRes(16);
        VguiFontAmalgam? font = GetFont("TextFont");

        if (font is not null)
        {
            surface.DrawSetTextFont(font);
        }

        int xMargin = XRes(10);
        int xSpacing = StringWidth(surface, font, " ");
        int lineTall = (int)GetFloat("LineHeight");
        float lineSpacing = GetFloat("LineSpacing");
        int textTall = font is null ? 0 : surface.GetFontTall(font);
        (byte, byte, byte, byte) iconColor = GetColor("IconColor");

        for (int i = 0; i < _notices.Count; i++)
        {
            DeathNoticeItem msg = _notices[i];
            string victim = msg.VictimName;
            string killer = msg.KillerName;

            int victimTextWide = StringWidth(surface, font, victim) + xSpacing;
            int infoTextWide = msg.InfoText.Length > 0 ? StringWidth(surface, font, msg.InfoText) + xSpacing : 0;
            int infoEndTextWide = msg.InfoTextEnd.Length > 0 ? StringWidth(surface, font, msg.InfoTextEnd) + xSpacing : 0;
            int killerTextWide = killer.Length > 0 ? StringWidth(surface, font, killer) + xSpacing : 0;
            int preKillerTextWide = msg.PreKillerText.Length > 0 ? StringWidth(surface, font, msg.PreKillerText) - xSpacing : 0;
            int deathInfoOffset = 0;
            int victimTextOffset = 0;

            (int iconWide, int iconTall, int iconActualWide) = Scaled(surface, msg.IconDeath, lineTall, xSpacing);
            (int preWide, int preTall, int preActualWide) = Scaled(surface, msg.IconPreKillerName, lineTall, 0);
            (int postWide, int postTall, int postActualWide) = Scaled(surface, msg.IconPostKillerName, lineTall, 0);
            (int postVictimWide, int postVictimTall, int postVictimActualWide) = Scaled(surface, msg.IconPostVictimName, lineTall, 0);

            int totalWide = killerTextWide + iconWide + victimTextWide + infoTextWide + infoEndTextWide + (xMargin * 2);
            totalWide += preWide + postWide + preKillerTextWide + postVictimWide;

            int y = (int)(yStart + ((lineTall + lineSpacing) * i));
            int yText = y + ((lineTall - textTall) / 2);
            int yIcon = y + ((lineTall - iconTall) / 2);
            int x = GetBool("RightJustify") ? Wide - totalWide : 0;

            // "draw a background panel for the message"
            surface.DrawSetTexture(null);
            surface.DrawSetColor(GetBackgroundColor(msg));
            surface.DrawTexturedPolygon(BackgroundPolygon(x, y + 1, x + totalWide, y + lineTall - 1));

            x += xMargin;

            if (msg.IconPreKillerName is { } preKiller)
            {
                preKiller.DrawSelf(surface, x, y + ((lineTall - preTall) / 2), preActualWide, preTall, iconColor);
                x += preWide + xSpacing;
            }

            if (killer.Length > 0)
            {
                DrawText(surface, x, yText, font, GetTeamColor(msg.KillerTeam, msg.LocalPlayerInvolved), killer);
                x += killerTextWide;
            }

            if (msg.PreKillerText.Length > 0)
            {
                x += xSpacing;
                DrawText(surface, x + deathInfoOffset, yText, font, GetInfoTextColor(msg), msg.PreKillerText);
                x += preKillerTextWide;
            }

            if (msg.IconPostKillerName is { } postKiller)
            {
                postKiller.DrawSelf(surface, x, y + ((lineTall - postTall) / 2), postActualWide, postTall, iconColor);
                x += postWide + xSpacing;
            }

            // "Draw glow behind weapon icon to show it was a crit death"
            if (msg.Crit && msg.IconCritDeath is { } crit)
            {
                crit.DrawSelf(surface, x, yIcon, iconActualWide, iconTall, iconColor);
            }

            if (msg.IconDeath is { } icon)
            {
                icon.DrawSelf(surface, x, yIcon, iconActualWide, iconTall, iconColor);
                x += iconWide;
            }

            if (msg.InfoText.Length > 0)
            {
                if (msg.SelfInflicted)
                {
                    deathInfoOffset += victimTextWide;
                    victimTextOffset -= infoTextWide;
                }

                DrawText(surface, x + deathInfoOffset, yText, font, GetInfoTextColor(msg), msg.InfoText);
                x += infoTextWide;
            }

            DrawText(surface, x + victimTextOffset, yText, font, GetTeamColor(msg.VictimTeam, msg.LocalPlayerInvolved), victim);
            x += victimTextWide;

            if (msg.IconPostVictimName is { } postVictim)
            {
                postVictim.DrawSelf(surface, x, y + ((lineTall - postVictimTall) / 2), postVictimActualWide, postVictimTall, iconColor);

                // `iconPostkillerWide`, as written — the post-KILLER icon's width advances past the post-VICTIM one.
                x += postWide + xSpacing;
            }

            if (msg.InfoTextEnd.Length > 0)
            {
                DrawText(surface, x, yText, font, GetInfoTextColor(msg), msg.InfoTextEnd);
            }
        }
    }

    /// <summary>`CTFHudDeathNotice::EventIsPlayerDeath` (tf_hud_deathnotice.cpp:774).</summary>
    private static bool EventIsPlayerDeath(string name) => name is "fish_notice" or "fish_notice__arm" or "slap_notice" or "player_death";

    /// <summary>`BAreTeamsEnemies` (tf_shareddefs.h:108): two different game teams.</summary>
    private static bool AreTeamsEnemies(int team, int other) => team >= FirstGameTeam && other >= FirstGameTeam && team != other;

    /// <summary>`Q_strncpy` into a 64-byte name: at most 63 bytes of UTF-8, read back as `ConvertANSIToUnicode` reads it.</summary>
    private static string NameBuffer(string text) => Bytes(text, NameBytes);

    private static string Bytes(string text, int buffer)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        return bytes.Length < buffer ? text : Encoding.UTF8.GetString(bytes, 0, buffer - 1);
    }

    /// <summary>`V_wcsncpy` into a 32-character buffer.</summary>
    private static string Copy(string? text) => text is null ? string.Empty : text[..Math.Min(text.Length, InfoChars - 1)];

    /// <summary>`UTIL_ComputeStringWidth` (cdll_util.cpp:768): the characters' widths summed, unkerned.</summary>
    private static int StringWidth(IVguiSurface surface, VguiFontAmalgam? font, string text)
    {
        if (font is null)
        {
            return 0;
        }

        float pixels = 0f;

        foreach (char character in text)
        {
            pixels += surface.GetCharacterWidth(font, character);
        }

        return (int)MathF.Ceiling(pixels);
    }

    /// <summary>An icon's width, height and drawn width at a line's height less `YRES(2)`: `EffectiveWidth( 1.0f )` scaled, int by float.</summary>
    private (int Wide, int Tall, int ActualWide) Scaled(IVguiSurface surface, HudTexture? icon, int lineTall, int spacing)
    {
        if (icon is null)
        {
            return (0, 0, 0);
        }

        int actualWide = icon.EffectiveWidth(surface, 1f);
        int wide = actualWide + spacing;
        int tall = icon.EffectiveHeight(1f);
        float scale = (float)(lineTall - YRes(2)) / tall;

        return ((int)(wide * scale), (int)(tall * scale), (int)(actualWide * scale));
    }

    private static void DrawText(IVguiSurface surface, int x, int y, VguiFontAmalgam? font, (byte, byte, byte, byte) color, string text)
    {
        surface.DrawSetTextPos(x, y);
        surface.DrawSetTextColor(color);

        if (font is null)
        {
            return;
        }

        // "reset the font, draw icon can change it"
        surface.DrawSetTextFont(font);
        surface.DrawPrintText(text, VguiFontDrawType.NonAdditive);
    }

    /// <summary>`XRES`: `x × ( ScreenWidth() / 640.0 )`, truncated.</summary>
    private int XRes(int x) => (int)(x * (_screenWide / 640.0));

    /// <summary>`YRES`.</summary>
    private int YRes(int y) => (int)(y * (_screenTall / 480.0));

    /// <summary>`CalcRoundedCorners` (hud_basedeathnotice.cpp:877): a quarter circle of the corner radius in ten points.</summary>
    private void CalcRoundedCorners()
    {
        float radius = GetFloat("CornerRadius");

        for (int i = 0; i < CornerCoordinates; i++)
        {
            double angle = (float)i / (CornerCoordinates - 1) * (Math.PI / 2);

            _cornerCoordinates[i] = ((float)(radius * (1 - Math.Cos(angle))), (float)(radius * (1 - Math.Sin(angle))));
        }
    }

    /// <summary>`GetBackgroundPolygonVerts` (hud_basedeathnotice.cpp:856): the four corners, clockwise from the top left.</summary>
    private VguiVertex[] BackgroundPolygon(int x0, int y0, int x1, int y1)
    {
        VguiVertex[] vertices = new VguiVertex[CornerCoordinates * 4];

        for (int i = 0; i < CornerCoordinates; i++)
        {
            int j = CornerCoordinates - 1 - i;

            vertices[i] = new VguiVertex(x0 + _cornerCoordinates[i].X, y0 + _cornerCoordinates[i].Y, 0f, 0f);
            vertices[i + CornerCoordinates] = new VguiVertex(x1 - _cornerCoordinates[j].X, y0 + _cornerCoordinates[j].Y, 0f, 0f);
            vertices[i + (CornerCoordinates * 2)] = new VguiVertex(x1 - _cornerCoordinates[i].X, y1 - _cornerCoordinates[i].Y, 0f, 0f);
            vertices[i + (CornerCoordinates * 3)] = new VguiVertex(x0 + _cornerCoordinates[j].X, y1 - _cornerCoordinates[j].Y, 0f, 0f);
        }

        return vertices;
    }

    /// <summary>`DeathNoticeItem::GetExpiryTime`: twice as long when the local player is involved.</summary>
    private float ExpiryTime(DeathNoticeItem item) => item.CreationTime + (item.LocalPlayerInvolved ? NoticeTime * 2 : NoticeTime);

    /// <summary>`CTFHudDeathNotice::GetTeamColor` (tf_hud_deathnotice.cpp:1581).</summary>
    private (byte, byte, byte, byte) GetTeamColor(int team, bool localPlayerInvolved)
    {
        SceneGameRules rules = HudViewport.Of(this)?.State.Rules ?? default;

        return team switch
        {
            TeamBlue => GetColor("TeamBlue"),
            TeamRed => GetColor("TeamRed"),
            TeamUnassigned => localPlayerInvolved ? GetColor("LocalPlayerColor") : ((byte)255, (byte)255, (byte)255, (byte)255),
            TeamHalloween => rules.HalloweenScenario is ScenarioLakeside or ScenarioHightower ? GetColor("GreenText") : GetColor("PurpleText"),
            _ => (255, 255, 255, 255),
        };
    }

    /// <summary>`CTFHudDeathNotice::GetInfoTextColor`.</summary>
    private (byte, byte, byte, byte) GetInfoTextColor(DeathNoticeItem item) =>
        item.LocalPlayerInvolved ? GetColor("LocalPlayerColor") : ((byte)255, (byte)255, (byte)255, (byte)255);

    /// <summary>`CTFHudDeathNotice::GetBackgroundColor`.</summary>
    private (byte, byte, byte, byte) GetBackgroundColor(DeathNoticeItem item) =>
        item.LocalPlayerInvolved ? GetColor("LocalBackgroundColor") : GetColor("BaseBackgroundColor");

    /// <summary>`CHudBaseDeathNotice::GetIcon` (hud_basedeathnotice.cpp:891): `d_` becomes `dneg_` when inverted, falling back to the standard icon.</summary>
    private HudTexture? GetIcon(string name, bool inverted)
    {
        HudTextures? icons = HudViewport.Of(this)?.Icons;

        if (icons is null)
        {
            return null;
        }

        if (inverted && name.StartsWith("d_", StringComparison.Ordinal) && icons.GetIcon("dneg_" + name[2..]) is { } negative)
        {
            return negative;
        }

        return icons.GetIcon(name);
    }

    /// <summary>`g_pVGuiLocalize->Find`, the `#` stripped.</summary>
    private string? Find(string token) =>
        HudViewport.Of(this)?.Context?.Localize?.Invoke(token.StartsWith('#') ? token[1..] : token);

    /// <summary>`CTFHudDeathNotice::ShouldShowDeathNotice` (tf_hud_deathnotice.cpp:693).</summary>
    private static bool ShouldShowDeathNotice(HudGameEvent fired)
    {
        SceneGameEvent e = fired.Event;

        if (e.GetInt("silent_kill") != 0)
        {
            // "Don't show a kill event for the team of the silent kill victim."
            int victimIndex = e.PlayerForUserId(e.GetInt("userid"));

            if (fired.Player(victimIndex) is { } victim && (victim.Team ?? 0) == fired.LocalPlayerTeam && victimIndex != fired.LocalPlayerIndex)
            {
                return false;
            }
        }

        if (fired.Rules.MannVsMachine && (e.GetInt("death_flags") & DeathMiniboss) == 0)
        {
            int local = fired.LocalPlayerIndex;

            if (local != e.PlayerForUserId(e.GetInt("attacker")) && local != e.PlayerForUserId(e.GetInt("assister"))
                && fired.Player(e.PlayerForUserId(e.GetInt("userid"))) is { Team: TeamPveInvaders })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>`CTFHudDeathNotice::UseExistingNotice` (tf_hud_deathnotice.cpp:1638), then the base's for `special_score`.</summary>
    private int UseExistingNotice(SceneGameEvent e)
    {
        int target = e.GetInt("weaponid");

        if (TfWeaponIds.Stacking.Contains(target))
        {
            for (int i = 0; i < _notices.Count; i++)
            {
                DeathNoticeItem msg = _notices[i];

                if (msg.WeaponId == target && msg.KillerId == e.GetInt("attacker") && msg.VictimId == e.GetInt("userid"))
                {
                    return i;
                }
            }
        }

        if (e.Name == "special_score")
        {
            int scorer = e.GetInt("player");

            for (int i = 0; i < _notices.Count; i++)
            {
                if (_notices[i].SpecialScore && _notices[i].KillerId == scorer)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>The base's player and object branch (hud_basedeathnotice.cpp:458); false when the notice was removed.</summary>
    private bool PlayerOrObjectDeath(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;
        int local = fired.LocalPlayerIndex;
        int victim = e.PlayerForUserId(e.GetInt("userid"));
        int killer = e.PlayerForUserId(e.GetInt("attacker"));
        string killedWith = e.GetString("weapon");

        // "for now, no death notices of map placed objects"
        if (e.Name == "object_destroyed" && victim == 0)
        {
            _notices.RemoveAt(index);
            return false;
        }

        string killerName = killer > 0 ? fired.PlayerName(killer) : string.Empty;
        string victimName = fired.PlayerName(victim);
        bool localInvolved = local == killer || local == victim;
        DeathNoticeItem msg = _notices[index];

        if ((e.GetInt("death_flags") & DeathAustralium) != 0)
        {
            msg.Crit = true;
            msg.IconCritDeath = GetIcon("d_australium", localInvolved);
        }
        else if ((e.GetInt("damagebits") & DamageCritical) != 0)
        {
            msg.Crit = true;
            msg.IconCritDeath = GetIcon("d_crit", localInvolved);
        }
        else
        {
            msg.Crit = false;
            msg.IconCritDeath = null;
        }

        msg.LocalPlayerInvolved = localInvolved;
        msg.KillerTeam = killer > 0 ? fired.Team(killer) : 0;
        msg.VictimTeam = fired.Team(victim);
        msg.KillerName = NameBuffer(killerName);
        msg.VictimName = NameBuffer(victimName);

        if (killedWith.Length > 0)
        {
            msg.Icon = Bytes("d_" + killedWith, IconBytes);
        }

        if (killer == 0 || killer == victim)
        {
            msg.SelfInflicted = true;
            msg.KillerName = string.Empty;

            if ((e.GetInt("death_flags") & DeathPurgatory) != 0)
            {
                msg.Icon = "d_purgatory";
            }
            else if ((e.GetInt("damagebits") & DamageFall) != 0)
            {
                msg.InfoText = Copy(Find("#DeathMsg_Fall"));
            }
            else if ((e.GetInt("damagebits") & DamageVehicle) != 0 || string.Equals(msg.Icon, "d_tracktrain", StringComparison.OrdinalIgnoreCase))
            {
                msg.Icon = string.Equals(System.IO.Path.GetFileNameWithoutExtension(fired.MapName), "pd_galleria", StringComparison.OrdinalIgnoreCase)
                    ? "d_resurfacer"
                    : "d_vehicle";
            }
        }

        msg.WeaponId = e.GetInt("weaponid");
        msg.KillerId = e.GetInt("attacker");
        msg.VictimId = e.GetInt("userid");

        return true;
    }

    /// <summary>`teamplay_point_captured` (hud_basedeathnotice.cpp:595): the cappers joined with ", ", the first one's team.</summary>
    private void PointCaptured(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;
        DeathNoticeItem msg = _notices[index];
        string cappers = e.GetString("cappers");
        StringBuilder names = new();

        msg.VictimName = ControlPointName(e);

        for (int i = 0; i < cappers.Length; i++)
        {
            int player = cappers[i];

            if (i == 0)
            {
                msg.KillerTeam = fired.Team(player);
                msg.VictimTeam = TeamUnassigned;
            }
            else
            {
                names.Append(", ");
            }

            names.Append(fired.PlayerName(player));
            msg.LocalPlayerInvolved |= fired.LocalPlayerIndex == player;
        }

        msg.KillerName = NameBuffer(Bytes(names.ToString(), 256));
        msg.InfoText = Copy(Find(cappers.Length > 1 ? "#Msg_Captured_Multiple" : "#Msg_Captured"));
    }

    /// <summary>`teamplay_flag_event` (hud_basedeathnotice.cpp:651); false when the notice was removed.</summary>
    private bool FlagEvent(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;

        // "don't handle any flag events for death notices while in player destruction mode"
        if (fired.Rules.PlayerDestruction)
        {
            _notices.RemoveAt(index);
            return false;
        }

        int type = e.GetInt("eventtype");
        bool mannVsMachine = fired.Rules.MannVsMachine;
        bool doomsday = fired.Rules.HalloweenScenario == ScenarioDoomsday;

        string? key = type switch
        {
            _ when mannVsMachine && type != FlagDefend => null,
            FlagPickup => doomsday ? "#Msg_PickedUpFlagHalloween2014" : "#Msg_PickedUpFlag",
            FlagCapture => doomsday ? "#Msg_CapturedFlagHalloween2014" : "#Msg_CapturedFlag",
            FlagDefend when mannVsMachine => "#Msg_DefendedBomb",
            FlagDefend => doomsday ? "#Msg_DefendedFlagHalloween2014" : "#Msg_DefendedFlag",
            _ => null,
        };

        if (key is null)
        {
            _notices.RemoveAt(index);
            return false;
        }

        DeathNoticeItem msg = _notices[index];
        int player = e.GetInt("player");

        msg.InfoText = Copy(Find(key));
        msg.KillerName = NameBuffer(fired.PlayerName(player));
        msg.KillerTeam = fired.Team(player);
        msg.LocalPlayerInvolved |= fired.LocalPlayerIndex == player;

        return true;
    }

    /// <summary>`GetLocalizedControlPointName` (hud_basedeathnotice.cpp:815): the `cpname` localised, else as written.</summary>
    private string ControlPointName(SceneGameEvent e)
    {
        string name = e.GetString("cpname", "Unnamed Control Point");

        return NameBuffer(Find(name) ?? name);
    }

    /// <summary>`CTFHudDeathNotice::OnGameEvent` (tf_hud_deathnotice.cpp:788).</summary>
    private void OnGameEvent(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;

        if (e.Name is "player_death" or "object_destroyed")
        {
            DeathOrDestruction(fired, index);
        }
        else if (e.Name is "teamplay_point_captured" or "teamplay_capture_blocked" or "teamplay_flag_event")
        {
            bool defense = e.Name == "teamplay_capture_blocked" || (e.Name == "teamplay_flag_event" && e.GetInt("eventtype") == FlagDefend);
            DeathNoticeItem msg = _notices[index];

            if (msg.KillerTeam is >= FirstGameTeam and < FirstGameTeam + TeamCount)
            {
                string[] icons = defense ? ["d_reddefend", "d_bluedefend"] : ["d_redcapture", "d_bluecapture"];

                msg.Icon = icons[msg.KillerTeam - FirstGameTeam];
            }
        }
        else if (e.Name is "fish_notice" or "fish_notice__arm" or "slap_notice")
        {
            Humiliation(fired, index);
        }
        else if (e.Name == "rd_robot_killed")
        {
            DeathNoticeItem msg = _notices[index];
            int killer = e.PlayerForUserId(e.GetInt("attacker"));
            int killerTeam = fired.Team(killer);

            msg.LocalPlayerInvolved |= fired.LocalPlayerIndex == killer;
            msg.KillerTeam = killerTeam;
            msg.KillerName = NameBuffer(fired.PlayerName(killer));
            msg.VictimName = killerTeam == TeamRed ? "BLUE ROBOT" : "RED ROBOT";
            msg.VictimTeam = killerTeam == TeamRed ? TeamBlue : TeamRed;
            msg.Icon = Bytes("d_" + e.GetString("weapon"), IconBytes);
        }
        else
        {
            Passtime(fired, index);
        }
    }

    /// <summary>The death and destruction branch of `OnGameEvent` (tf_hud_deathnotice.cpp:794–1256).</summary>
    private void DeathOrDestruction(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;
        bool objectDestroyed = e.Name == "object_destroyed";
        bool pyroVision = (fired.LocalVisionFlags & VisionPyro) == VisionPyro;
        int custom = e.GetInt("customkill");
        int local = fired.LocalPlayerIndex;
        int killerIndex = e.PlayerForUserId(e.GetInt("attacker"));
        int victimIndex = e.PlayerForUserId(e.GetInt("userid"));
        int assisterIndex = e.PlayerForUserId(e.GetInt("assister"));
        string? assisterName = assisterIndex > 0 ? fired.PlayerName(assisterIndex) : null;
        bool firstIsAssister = false;

        // "If we don't have a real assister … and we're in crazy pyrovision mode", the fallback's first byte says how to read it.
        if (assisterName is null && pyroVision && (objectDestroyed || killerIndex != victimIndex)
            && e.GetString("assister_fallback") is { Length: > 0 } fallback)
        {
            int hack = fallback[0];
            string rest = fallback[1..];

            (assisterName, firstIsAssister) = hack switch
            {
                PyroHackLocalizationString => (NameBuffer(Find(rest) ?? string.Empty), false),
                PyroHackLocalizationStringFirst => (NameBuffer(Find(rest) ?? string.Empty), true),
                PyroHackCustomName => (rest, false),
                PyroHackCustomNameFirst => (rest, true),
                _ => (null, false),
            };
        }

        bool multipleKillers = false;

        if (assisterName is not null)
        {
            DeathNoticeItem msg = _notices[index];
            (string first, string second) = firstIsAssister ? (assisterName, msg.KillerName) : (msg.KillerName, assisterName);

            msg.KillerName = NameBuffer(Bytes($"{first} + {second}", NameBytes));
            msg.LocalPlayerInvolved |= local == assisterIndex;
            multipleKillers = true;
        }

        int penetrations = e.Values.ContainsKey("playerpenetratecount") ? e.GetInt("playerpenetratecount") : 0;

        // "This happens too frequently in Coop/TD" (tf_hud_deathnotice.cpp:887): forced off in MvM. Before the rivalry
        // sounds, as :894 is.
        if (penetrations > 0 && !fired.Rules.MannVsMachine)
        {
            SoundEmitter?.Invoke("Game.PenetrationKill");
        }

        int deathFlags = e.GetInt("death_flags");

        if (!objectDestroyed)
        {
            // "WARNING: AddAdditionalMsg will grow … the m_DeathNotices array"
            string dominating = pyroVision ? "#Msg_Dominating_What" : "#Msg_Dominating";
            string revenge = pyroVision ? "#Msg_Revenge_What" : "#Msg_Revenge";

            if ((deathFlags & DeathDomination) != 0)
            {
                AddAdditionalMsg(fired, killerIndex, victimIndex, dominating);
                PlayRivalrySounds(fired, killerIndex, victimIndex, domination: true);
            }

            if ((deathFlags & DeathAssisterDomination) != 0 && assisterIndex > 0)
            {
                AddAdditionalMsg(fired, assisterIndex, victimIndex, dominating);
                PlayRivalrySounds(fired, assisterIndex, victimIndex, domination: true);
            }

            if ((deathFlags & DeathRevenge) != 0)
            {
                AddAdditionalMsg(fired, killerIndex, victimIndex, revenge);
                PlayRivalrySounds(fired, killerIndex, victimIndex, domination: false);
            }

            if ((deathFlags & DeathAssisterRevenge) != 0 && assisterIndex > 0)
            {
                AddAdditionalMsg(fired, assisterIndex, victimIndex, revenge);
                PlayRivalrySounds(fired, assisterIndex, victimIndex, domination: false);
            }
        }
        else
        {
            ObjectName(e.GetInt("objecttype"), _notices[index]);
        }

        DeathNoticeItem item = _notices[index];

        CustomKill(fired, item, custom, penetrations, multipleKillers);

        if ((e.GetInt("damagebits") & DamageNerveGas) != 0)
        {
            item.Icon = "d_saw_kill";
        }

        int killStreakWeapon = e.GetInt("kill_streak_wep");
        int duckStreakTotal = e.GetInt("duck_streak_total");
        int ducksThisKill = e.GetInt("ducks_streaked");

        // Mannpower runes: red reads the plain file, blue the negative one.
        if (fired.Player(killerIndex) is { } killer && RuneIcon(killer) is { } killerRune)
        {
            item.IconPreKillerName = HudViewport.Of(this)?.Icons?.GetIcon((killer.Team == TeamRed ? "d_" : "dneg_") + killerRune);
        }

        if (fired.Player(victimIndex) is { } victim && RuneIcon(victim) is { } victimRune)
        {
            item.IconPostVictimName = HudViewport.Of(this)?.Icons?.GetIcon((victim.Team == TeamRed ? "d_" : "dneg_") + victimRune);
        }

        string count;

        if (killStreakWeapon > 0)
        {
            count = killStreakWeapon.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item.PreKillerText = VguiLocalize.ConstructString(Find("#Kill_Streak"), InfoChars, count);
            item.IconPostKillerName = item.LocalPlayerInvolved ? _iconKillStreakDNeg : _iconKillStreak;
        }
        else if (duckStreakTotal > 0 && ducksThisKill != 0)
        {
            count = duckStreakTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item.PreKillerText = VguiLocalize.ConstructString(Find("#Duck_Streak"), InfoChars, count);
            item.IconPostKillerName = item.LocalPlayerInvolved ? _iconDuckStreakDNeg : _iconDuckStreak;
        }

        // "Check to see if we want a extra notification" (tf_hud_deathnotice.cpp:1219), kills then ducks.
        bool assister = fired.Player(assisterIndex) is not null;
        bool victimPresent = fired.Player(victimIndex) is not null;
        int killStreakAssist = e.GetInt("kill_streak_assist");
        int killStreakVictim = e.GetInt("kill_streak_victim");

        AddStreakMsg(fired, TfStreakType.Kills, killerIndex, e.GetInt("kill_streak_total"), 1);

        if (assister && killStreakAssist > 1)
        {
            AddStreakMsg(fired, TfStreakType.Kills, assisterIndex, killStreakAssist, 1);
        }

        if (victimPresent && killStreakVictim > 2)
        {
            AddStreakEndedMsg(fired, TfStreakType.Kills, killerIndex, victimIndex, killStreakVictim);
        }

        int duckStreakAssist = e.GetInt("duck_streak_assist");
        int duckStreakVictim = e.GetInt("duck_streak_victim");

        AddStreakMsg(fired, TfStreakType.Ducks, killerIndex, duckStreakTotal, ducksThisKill);

        if (assister && duckStreakAssist > 0 && ducksThisKill != 0)
        {
            AddStreakMsg(fired, TfStreakType.Ducks, assisterIndex, duckStreakAssist, ducksThisKill);
        }

        if (victimPresent && duckStreakVictim > 2)
        {
            AddStreakEndedMsg(fired, TfStreakType.Ducks, killerIndex, victimIndex, duckStreakVictim);
        }
    }

    /// <summary>`AddStreakMsg` (tf_hud_deathnotice.cpp:1547): past the type's minimum and with a display time, to the banner.</summary>
    private void AddStreakMsg(HudGameEvent fired, TfStreakType type, int player, int streak, int increment)
    {
        if (Streak is not { } banner || streak < TfStreakNotice.MinStreakForType(type, fired.Rules.MannVsMachine) || (int)banner.DisplayTime <= 0)
        {
            return;
        }

        banner.StreakUpdated(type, player, streak, increment, fired, fired.RealTime);
    }

    /// <summary>`AddStreakEndedMsg` (tf_hud_deathnotice.cpp:1563).</summary>
    private void AddStreakEndedMsg(HudGameEvent fired, TfStreakType type, int killer, int victim, int streak)
    {
        if (Streak is not { } banner || streak < TfStreakNotice.MinStreakForType(type, fired.Rules.MannVsMachine) || (int)banner.DisplayTime <= 0)
        {
            return;
        }

        banner.StreakEnded(type, killer, victim, streak, fired, fired.RealTime);
    }

    // EHorriblePyroVisionHack's first-byte values (tf_shareddefs.h:1867).
    private const int PyroHackCustomName = 'a';
    private const int PyroHackLocalizationString = 'b';
    private const int PyroHackCustomNameFirst = 'c';
    private const int PyroHackLocalizationStringFirst = 'd';

    /// <summary>The object's localised name, with its owner in brackets when it has one (tf_hud_deathnotice.cpp:935).</summary>
    private void ObjectName(int objectType, DeathNoticeItem msg)
    {
        if (objectType < 0 || objectType >= LocalizedObjectNames.Length)
        {
            return;
        }

        string token = LocalizedObjectNames[objectType];
        string name = Bytes(Find(token) ?? token, 32);

        msg.VictimName = msg.VictimName.Length > 0 ? NameBuffer(Bytes($"{name} ({msg.VictimName})", NameBytes)) : NameBuffer(name);
    }

    /// <summary>The `customkill` switch (tf_hud_deathnotice.cpp:975).</summary>
    private void CustomKill(HudGameEvent fired, DeathNoticeItem msg, int custom, int penetrations, bool multipleKillers)
    {
        SceneGameEvent e = fired.Event;
        bool fromUser = e.GetInt("attacker") == e.GetInt("userid");

        switch (custom)
        {
            case TfCustomKills.Backstab:
                msg.Icon = msg.Icon == "d_sharp_dresser" ? "d_sharp_dresser_backstab" : "d_backstab";
                break;
            case TfCustomKills.HeadshotDecapitation:
            case TfCustomKills.Headshot:
                msg.Icon = e.GetString("weapon") switch
                {
                    "ambassador" => "d_ambassador_headshot",
                    "huntsman" => "d_huntsman_headshot",
                    _ => penetrations > 0 ? "d_headshot_player_penetration" : "d_headshot",
                };
                break;
            case TfCustomKills.Burning when fromUser:
                // "suicide by fire"
                (msg.Icon, msg.InfoText) = ("d_firedeath", string.Empty);
                break;
            case TfCustomKills.BurningArrow:
                (msg.Icon, msg.InfoText) = ("d_huntsman_burning", string.Empty);
                break;
            case TfCustomKills.FlyingBurn:
                (msg.Icon, msg.InfoText) = ("d_huntsman_flyingburn", string.Empty);
                break;
            case TfCustomKills.PumpkinBomb:
                (msg.Icon, msg.InfoText) = ("d_pumpkindeath", string.Empty);
                break;
            case TfCustomKills.Suicide:
            {
                // "assisted suicide (suicide w/recent damage, kill awarded to damager)"
                string key = (fromUser, multipleKillers) switch
                {
                    (true, _) => "#DeathMsg_Suicide",
                    (false, true) => "#DeathMsg_AssistedSuicide_Multiple",
                    (false, false) => "#DeathMsg_AssistedSuicide",
                };

                if (Find(key) is { } text)
                {
                    msg.InfoText = Copy(text);
                }

                break;
            }

            case TfCustomKills.Croc:
                if (e.GetInt("attacker") != 0 && !fromUser && Find("#DeathMsg_AssistedSuicide") is { } assisted)
                {
                    msg.InfoText = Copy(assisted);
                }

                break;
            case TfCustomKills.EyeballRocket:
                BossName(msg, "#TF_HALLOWEEN_EYEBALL_BOSS_DEATHCAM_NAME");
                break;
            case TfCustomKills.MerasmusZap:
            case TfCustomKills.MerasmusGrenade:
            case TfCustomKills.MerasmusDecapitation:
                BossName(msg, "#TF_HALLOWEEN_MERASMUS_DEATHCAM_NAME");
                break;
            case TfCustomKills.SpellSkeleton:
                BossName(msg, fired.MapName == "maps/koth_slime.bsp" ? "#koth_slime_salmann" : "#TF_HALLOWEEN_SKELETON_DEATHCAM_NAME");
                break;
            case TfCustomKills.Kart:
                (msg.Icon, msg.InfoText) = ("d_bumper_kart", string.Empty);
                break;
            case TfCustomKills.GiantHammer:
                (msg.Icon, msg.InfoText) = ("d_necro_smasher", string.Empty);
                break;
            case TfCustomKills.KrampusMelee:
                Krampus(msg, "d_krampus_melee");
                break;
            case TfCustomKills.KrampusRanged:
                Krampus(msg, "d_krampus_ranged");
                break;
        }
    }

    /// <summary>A Halloween boss named as the killer, purple or green, when the killer had no team.</summary>
    private void BossName(DeathNoticeItem msg, string token)
    {
        if (msg.KillerTeam == TeamUnassigned && Find(token) is { } name)
        {
            msg.KillerName = NameBuffer(Bytes(name, 32));
            msg.KillerTeam = TeamHalloween;
        }
    }

    private void Krampus(DeathNoticeItem msg, string icon)
    {
        if (Find("#koth_krampus_boss") is { } name)
        {
            msg.KillerName = NameBuffer(Bytes(name, 32));
            msg.KillerTeam = TeamHalloween;
        }

        (msg.Icon, msg.InfoText) = (icon, string.Empty);
    }

    /// <summary>`GetMannPowerIcon`'s name for the rune a player carries — the first rune condition set — or null.</summary>
    private static string? RuneIcon(ScenePlayer player)
    {
        PlayerConditions conditions = player.Conditions;

        for (int rune = 0; rune < TfConditions.Runes.Length; rune++)
        {
            if (conditions.Has(TfConditions.Runes[rune]))
            {
                return RuneIcons[rune];
            }
        }

        return null;
    }

    /// <summary>Fish, arm and slap (tf_hud_deathnotice.cpp:1279).</summary>
    private void Humiliation(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;
        DeathNoticeItem msg = _notices[index];
        int custom = e.GetInt("customkill");

        if (custom == TfCustomKills.FishKill || (e.GetInt("death_flags") & DeathFeignDeath) != 0 || custom == TfCustomKills.SlapKill)
        {
            string key = e.Name switch
            {
                "fish_notice__arm" => "#Humiliation_Kill_Arm",
                "slap_notice" => "#Humiliation_Kill_Slap",
                _ => "#Humiliation_Kill",
            };

            msg.InfoText = VguiLocalize.ConstructString(Find(key), InfoChars);
        }
        else
        {
            msg.Count++;
            msg.InfoText = VguiLocalize.ConstructString(
                Find("#Humiliation_Count"), InfoChars, msg.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        int assister = e.PlayerForUserId(e.GetInt("assister"));

        if (assister > 0)
        {
            msg.KillerName = NameBuffer(Bytes($"{msg.KillerName} + {fired.PlayerName(assister)}", NameBytes));
        }
    }

    /// <summary>The PASS Time branches (tf_hud_deathnotice.cpp:1364–1520).</summary>
    private void Passtime(HudGameEvent fired, int index)
    {
        SceneGameEvent e = fired.Event;
        DeathNoticeItem msg = _notices[index];
        int local = fired.LocalPlayerIndex;

        switch (e.Name)
        {
            case "pass_get":
            {
                int owner = e.GetInt("owner");

                msg.InfoText = Copy(Find("#Msg_PasstimeBallGet"));
                (msg.KillerName, msg.KillerTeam) = (NameBuffer(fired.PlayerName(owner)), fired.Team(owner));
                msg.LocalPlayerInvolved |= local == owner;
                msg.Icon = "d_passtime_pass";
                break;
            }

            case "pass_ball_stolen":
            {
                int attacker = e.GetInt("attacker");
                int victim = e.GetInt("victim");

                (msg.KillerName, msg.KillerTeam) = (NameBuffer(fired.PlayerName(attacker)), fired.Team(attacker));
                (msg.VictimName, msg.VictimTeam) = (NameBuffer(fired.PlayerName(victim)), fired.Team(victim));
                msg.InfoText = Copy(Find("#Msg_PasstimeSteal"));
                msg.LocalPlayerInvolved = local == attacker || local == victim;
                msg.Icon = "d_passtime_steal";
                break;
            }

            case "pass_score":
            {
                int scorer = e.GetInt("scorer");
                int points = e.GetInt("points");

                msg.InfoText = points > 1
                    ? VguiLocalize.ConstructString(Find("#Msg_PasstimeScoreCount"), InfoChars, points.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : Copy(Find("#Msg_PasstimeScore"));
                (msg.KillerName, msg.KillerTeam) = (NameBuffer(fired.PlayerName(scorer)), fired.Team(scorer));
                msg.LocalPlayerInvolved |= local == scorer;
                msg.Icon = msg.KillerTeam == TeamRed ? "d_passtime_score_red" : "d_passtime_score_blue";
                break;
            }

            case "pass_pass_caught":
            {
                int passer = e.GetInt("passer");
                int catcher = e.GetInt("catcher");
                int passerTeam = fired.Team(passer);
                int catcherTeam = fired.Team(catcher);
                bool pass = passerTeam == catcherTeam;
                (int killer, int killerTeam, int victim, int victimTeam) = pass
                    ? (passer, passerTeam, catcher, catcherTeam)
                    : (catcher, catcherTeam, passer, passerTeam);

                msg.Icon = pass ? "d_passtime_pass" : "d_passtime_intercept";
                (msg.KillerName, msg.KillerTeam) = (NameBuffer(fired.PlayerName(killer)), killerTeam);
                (msg.VictimName, msg.VictimTeam) = (NameBuffer(fired.PlayerName(victim)), victimTeam);
                msg.InfoText = Copy(Find(pass ? "#Msg_PasstimePassComplete" : "#Msg_PasstimeInterception"));
                msg.LocalPlayerInvolved = local == catcher || local == passer;
                break;
            }

            case "pass_ball_blocked":
            {
                int blocker = e.GetInt("blocker");
                int owner = e.GetInt("owner");

                (msg.KillerName, msg.KillerTeam) = (NameBuffer(fired.PlayerName(blocker)), fired.Team(blocker));
                (msg.VictimName, msg.VictimTeam) = (NameBuffer(fired.PlayerName(owner)), fired.Team(owner));
                msg.InfoText = Copy(Find("#Msg_PasstimeBlock"));
                msg.LocalPlayerInvolved = local == blocker || local == owner;
                msg.Icon = "d_ball";
                break;
            }
        }
    }

    /// <summary>`CTFHudDeathNotice::PlayRivalrySounds` (tf_hud_deathnotice.cpp:724): only when the recorder is a side of it.</summary>
    /// <param name="fired">The event, for the local player's index.</param>
    /// <param name="killer">The domination's or revenge's killer (or its assister, entity index).</param>
    /// <param name="victim">Its victim.</param>
    /// <param name="domination">True for a domination, false for a revenge.</param>
    private void PlayRivalrySounds(HudGameEvent fired, int killer, int victim, bool domination)
    {
        int local = fired.LocalPlayerIndex;

        if (killer != local && victim != local)
        {
            return;
        }

        string sound;

        if (!domination)
        {
            sound = "Game.Revenge";
        }
        else if (killer == local)
        {
            sound = "Game.Domination";
        }
        else
        {
            sound = "Game.Nemesis";
        }

        SoundEmitter?.Invoke(sound);
    }

    /// <summary>`AddAdditionalMsg` (tf_hud_deathnotice.cpp:1526): a domination or revenge line after the kill.</summary>
    private void AddAdditionalMsg(HudGameEvent fired, int killer, int victim, string key)
    {
        DeathNoticeItem msg = new()
        {
            CreationTime = fired.CurTime,
            KillerName = NameBuffer(fired.PlayerName(killer)),
            KillerTeam = fired.Team(killer),
            VictimName = NameBuffer(fired.PlayerName(victim)),
            VictimTeam = fired.Team(victim),
            IconDeath = _iconDomination,
            LocalPlayerInvolved = fired.LocalPlayerIndex == victim || fired.LocalPlayerIndex == killer,
        };

        if (Find(key) is { } text)
        {
            msg.InfoText = Copy(text);
        }

        _notices.Add(msg);
    }
}

/// <summary>The `ETFDmgCustom` values the kill feed switches on (tf_shareddefs.h:1179), pinned by a conformance test.</summary>
public static class TfCustomKills
{
    /// <summary>`TF_DMG_CUSTOM_HEADSHOT`.</summary>
    public const int Headshot = 1;

    /// <summary>`TF_DMG_CUSTOM_BACKSTAB`.</summary>
    public const int Backstab = 2;

    /// <summary>`TF_DMG_CUSTOM_BURNING`.</summary>
    public const int Burning = 3;

    /// <summary>`TF_DMG_CUSTOM_SUICIDE`.</summary>
    public const int Suicide = 6;

    /// <summary>`TF_DMG_CUSTOM_BURNING_ARROW`.</summary>
    public const int BurningArrow = 17;

    /// <summary>`TF_DMG_CUSTOM_FLYINGBURN`.</summary>
    public const int FlyingBurn = 18;

    /// <summary>`TF_DMG_CUSTOM_PUMPKIN_BOMB`.</summary>
    public const int PumpkinBomb = 19;

    /// <summary>`TF_DMG_CUSTOM_FISH_KILL`.</summary>
    public const int FishKill = 39;

    /// <summary>`TF_DMG_CUSTOM_EYEBALL_ROCKET`.</summary>
    public const int EyeballRocket = 50;

    /// <summary>`TF_DMG_CUSTOM_HEADSHOT_DECAPITATION`.</summary>
    public const int HeadshotDecapitation = 51;

    /// <summary>`TF_DMG_CUSTOM_MERASMUS_GRENADE`.</summary>
    public const int MerasmusGrenade = 58;

    /// <summary>`TF_DMG_CUSTOM_MERASMUS_ZAP`.</summary>
    public const int MerasmusZap = 59;

    /// <summary>`TF_DMG_CUSTOM_MERASMUS_DECAPITATION`.</summary>
    public const int MerasmusDecapitation = 60;

    /// <summary>`TF_DMG_CUSTOM_SPELL_SKELETON`.</summary>
    public const int SpellSkeleton = 66;

    /// <summary>`TF_DMG_CUSTOM_KART`.</summary>
    public const int Kart = 75;

    /// <summary>`TF_DMG_CUSTOM_GIANT_HAMMER`.</summary>
    public const int GiantHammer = 76;

    /// <summary>`TF_DMG_CUSTOM_SLAP_KILL`.</summary>
    public const int SlapKill = 80;

    /// <summary>`TF_DMG_CUSTOM_CROC`.</summary>
    public const int Croc = 81;

    /// <summary>`TF_DMG_CUSTOM_KRAMPUS_MELEE`.</summary>
    public const int KrampusMelee = 84;

    /// <summary>`TF_DMG_CUSTOM_KRAMPUS_RANGED`.</summary>
    public const int KrampusRanged = 85;
}

/// <summary>The `ETFWeaponType` values whose kills stack into one notice (tf_hud_deathnotice.cpp:1643), pinned by a conformance test.</summary>
public static class TfWeaponIds
{
    /// <summary>`TF_WEAPON_BAT_FISH`.</summary>
    public const int BatFish = 72;

    /// <summary>`TF_WEAPON_THROWABLE`.</summary>
    public const int Throwable = 92;

    /// <summary>`TF_WEAPON_GRENADE_THROWABLE`.</summary>
    public const int GrenadeThrowable = 93;

    /// <summary>`TF_WEAPON_SLAP`.</summary>
    public const int Slap = 106;

    /// <summary>The four.</summary>
    public static IReadOnlySet<int> Stacking { get; } = new HashSet<int> { BatFish, Slap, Throwable, GrenadeThrowable };
}

/// <summary>The `ETFCond` rune conditions in `RuneTypes_t` order (tf_shareddefs.h:2659), pinned by a conformance test.</summary>
public static class TfConditions
{
    /// <summary>`TF_COND_RUNE_STRENGTH` … `TF_COND_RUNE_SUPERNOVA`.</summary>
    public static int[] Runes { get; } = [90, 91, 92, 93, 94, 95, 96, 97, 103, 109, 110, 111];
}
