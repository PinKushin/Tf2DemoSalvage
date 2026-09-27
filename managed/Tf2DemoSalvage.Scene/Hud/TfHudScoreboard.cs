using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTFClientScoreBoardDialog` (`game/client/tf/vgui/tf_clientscoreboard.cpp`): the Tab scoreboard's red and blue player
/// lists, over `CClientScoreBoardDialog` (`game_controls/clientscoreboarddialog.cpp`).
/// </summary>
/// <remarks>
/// Wired to `+showscores`/TAB through <c>Tf2DemoSalvage.Presentation.VguiHud.Scoreboard</c> — see its remarks for how
/// this panel is shown and updated, since it is a viewport panel rather than a `CHudElement`.
///
/// **Not modelled:**
/// <list type="bullet">
/// <item><description>
/// Avatars, the medal column's images, killstreak icons, the dominating/nemesis icons, and the class and ping
/// images — every `COLUMN_IMAGE` column is declared with its own width so the layout matches Valve's, but
/// <see cref="VguiSectionedListPanel"/> itself draws nothing for an image cell (see its own remarks), and this
/// element does not have a `CTFClientScoreBoardDialog`'s image list to select an index from.
/// </description></item>
/// <item><description>
/// A player's kill streak (`m_Shared.m_nStreaks`) — off the weapon/attacker state this project does not carry
/// into <see cref="SceneScoreboardPlayer"/> — and dominations beyond the raw count (`ActiveDominations`); the
/// icon `GetActiveDominations` picks (`tf_clientscoreboard.cpp:1335-1372`) needs the image list above.
/// </description></item>
/// <item><description>
/// `MM_PlayerConnectionState_t` (looking-for-player, connecting, disconnected, :1302-1333, :1582-1619) — a
/// matchmaking notion a demo does not carry; every listed slot is treated as `connected == 2` here.
/// </description></item>
/// <item><description>
/// `IsFakePlayer` (bots) is not read off <see cref="SceneScoreboardPlayer"/> at all — see its own remarks — so
/// the bot ping icon branch (:1398-1409) is not modelled; ping is always a plain number.
/// </description></item>
/// <item><description>
/// `C_TFTeam::UpdateTeamName`'s party-leader and event-team names (`c_tf_team.cpp:118-156`) — they need
/// `HasPremadeParties`/`GetEventTeamStatus`, neither decoded, so such a match shows the localized name — and the
/// premade-party leader avatars.
/// </description></item>
/// <item><description>
/// `m_nExtraSpace` (:907), which widens the name column to fill whatever space avatars and a hidden scrollbar
/// leave at the dialog's actual screen width — this port's name column is always exactly `name_width`.
/// </description></item>
/// </list>
/// </remarks>
public sealed class TfClientScoreBoardDialog : VguiEditablePanel
{
    private const string ResFile = "resource/UI/Scoreboard.res";

    /// <summary>`TF_TEAM_RED` (tf_shareddefs.h).</summary>
    public const int TeamRed = SceneTeams.Red;

    /// <summary>`TF_TEAM_BLUE`.</summary>
    public const int TeamBlue = SceneTeams.Blu;

    /// <summary>`name_width` (`clientscoreboarddialog.h:113`).</summary>
    public const int NameWidthDefault = 136;

    /// <summary>`class_width` (`:114`).</summary>
    public const int ClassWidthDefault = 35;

    /// <summary>`score_width` (`:115`).</summary>
    public const int ScoreWidthDefault = 35;

    /// <summary>`ping_width` (`:117`).</summary>
    public const int PingWidthDefault = 23;

    /// <summary>`avatar_width` (`:112`).</summary>
    public const int AvatarWidthDefault = 18;

    /// <summary>`spacer` (`tf_clientscoreboard.cpp:152`).</summary>
    public const int SpacerWidthDefault = 5;

    /// <summary>`nemesis_width` (`:153`).</summary>
    public const int NemesisWidthDefault = 20;

    /// <summary>`medal_column_width` (`:155`).</summary>
    public const int MedalColumnWidthDefault = 15;

    /// <summary>`killstreak_width` (`:156`).</summary>
    public const int KillstreakWidthDefault = 20;

    /// <summary>`killstreak_image_width` (`:157`).</summary>
    public const int KillstreakImageWidthDefault = 20;

    /// <summary>`m_pPlayerListBlue`.</summary>
    public VguiSectionedListPanel PlayerListBlue { get; }

    /// <summary>`m_pPlayerListRed`.</summary>
    public VguiSectionedListPanel PlayerListRed { get; }

    /// <summary>`m_pRedTeamName` (`CExLabel( this, "RedTeamLabel", "" )`, :179).</summary>
    public TfExLabel RedTeamName { get; }

    /// <summary>`m_pBlueTeamName` (`CExLabel( this, "BlueTeamLabel", "" )`, :180).</summary>
    public TfExLabel BlueTeamName { get; }

    /// <summary>`CTFClientScoreBoardDialog( IViewPort *pViewPort )`: both lists made up front, so the `.res` finds them by name.</summary>
    /// <param name="parent">The parent.</param>
    public TfClientScoreBoardDialog(VguiPanel? parent)
        : base(parent, "Scoreboard")
    {
        PlayerListBlue = new VguiSectionedListPanel(this, "BluePlayerList");
        PlayerListRed = new VguiSectionedListPanel(this, "RedPlayerList");
        RedTeamName = new TfExLabel(this, "RedTeamLabel");
        BlueTeamName = new TfExLabel(this, "BlueTeamLabel");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFClientScoreBoardDialog";

    /// <summary>`ShowAvatars()` (`clientscoreboarddialog.h:50`): `IsPC()`, always true here.</summary>
    public static bool ShowAvatars => true;

    /// <summary>
    /// `m_hScoreFontDefault` (`ApplySchemeSettings`, :317): <c>pScheme->GetFont( "Default", true )</c> — always
    /// proportional, regardless of this dialog's own <see cref="VguiPanel.Proportional"/>.
    /// </summary>
    /// <remarks>Null until <see cref="ApplySchemeSettings"/> has run once; <see cref="UpdatePlayerList"/> leaves a
    /// row on its list's own default font rather than forcing a null one (see its own remarks).</remarks>
    public VguiFontAmalgam? ScoreFontDefault { get; set; }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:304): loads the `.res`, then re-inits both lists.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        LoadControlSettings(ResFile, context);

        InitPlayerList(PlayerListBlue);
        InitPlayerList(PlayerListRed);

        ScoreFontDefault = context.GetFont("Default", proportional: true);
    }

    /// <summary>`InitPlayerList` (:886-925): the section, its sort func, and every column in Valve's order.</summary>
    /// <param name="list">The list to init.</param>
    public static void InitPlayerList(VguiSectionedListPanel list)
    {
        ArgumentNullException.ThrowIfNull(list);

        list.RemoveAllSections();
        list.AddSection(0, "Players", TfPlayerSortFunc);
        list.SetSectionAlwaysVisible(0, true);
        list.SetSectionFgColor(0, (255, 255, 255, 255));

        list.AddColumnToSection(0, "medal", string.Empty, SectionedListColumn.ColumnImage | SectionedListColumn.ColumnCenter, MedalColumnWidthDefault);

        if (ShowAvatars)
        {
            list.AddColumnToSection(0, "avatar", string.Empty, SectionedListColumn.ColumnImage, AvatarWidthDefault);
            list.AddColumnToSection(0, "spacer", string.Empty, SectionedListColumn.None, SpacerWidthDefault);
        }

        list.AddColumnToSection(0, "name", "#TF_Scoreboard_Name", SectionedListColumn.None, NameWidthDefault);
        list.AddColumnToSection(0, "killstreak", string.Empty, SectionedListColumn.ColumnRight, KillstreakWidthDefault);
        list.AddColumnToSection(0, "killstreak_image", string.Empty, SectionedListColumn.ColumnImage, KillstreakImageWidthDefault);
        list.AddColumnToSection(0, "dominating", string.Empty, SectionedListColumn.ColumnImage | SectionedListColumn.ColumnCenter, NemesisWidthDefault);
        list.AddColumnToSection(0, "nemesis", string.Empty, SectionedListColumn.ColumnImage | SectionedListColumn.ColumnCenter, NemesisWidthDefault);
        list.AddColumnToSection(0, "score", "#TF_Scoreboard_Score", SectionedListColumn.ColumnRight, ScoreWidthDefault);
        list.AddColumnToSection(0, "class", string.Empty, SectionedListColumn.ColumnImage | SectionedListColumn.ColumnRight, ClassWidthDefault);

        // `tf_scoreboard_ping_as_text` is 0 by default (:919-924): the image branch, which here is
        // just the numeric fallback since no image list resolves an icon (see remarks).
        list.AddColumnToSection(0, "ping", string.Empty, SectionedListColumn.ColumnImage | SectionedListColumn.ColumnRight, PingWidthDefault);
    }

    /// <summary>`TFPlayerSortFunc` (:2186-2210): score descending, then fully-connected first, then player index descending.</summary>
    /// <param name="list">The list — read through, as Valve's callback does.</param>
    /// <param name="itemId1">The item being inserted.</param>
    /// <param name="itemId2">The item already placed.</param>
    /// <returns>Whether <paramref name="itemId1"/> sorts before <paramref name="itemId2"/>.</returns>
    public static bool TfPlayerSortFunc(VguiSectionedListPanel list, int itemId1, int itemId2)
    {
        ArgumentNullException.ThrowIfNull(list);

        IReadOnlyDictionary<string, string>? item1 = list.GetItemData(itemId1);
        IReadOnlyDictionary<string, string>? item2 = list.GetItemData(itemId2);

        int score1 = GetInt(item1, "score");
        int score2 = GetInt(item2, "score");

        if (score1 > score2)
        {
            return true;
        }

        if (score1 < score2)
        {
            return false;
        }

        int connected1 = GetInt(item1, "connected");
        int connected2 = GetInt(item2, "connected");

        if (connected1 != 2 || connected2 != 2)
        {
            return connected1 > connected2;
        }

        int playerIndex1 = GetInt(item1, "playerIndex");
        int playerIndex2 = GetInt(item2, "playerIndex");

        return playerIndex1 > playerIndex2;
    }

    /// <summary>
    /// `UpdateTeamInfo` (:980-1062): `redteamscore`/`blueteamscore`, `redteamname`/`blueteamname`, the pluralized
    /// `redteamplayercount`/`blueteamplayercount`, and the `m_pRedTeamName`/`m_pBlueTeamName` labels' tournament-mode
    /// visibility (:1029-1037). Party-leader avatars (:1038-1054) are not modelled — see remarks.
    /// </summary>
    /// <param name="state">The frame: its teams, rules and tournament ConVars.</param>
    public void UpdateTeamInfo(HudState state)
    {
        // `IsInTournamentMode()`: `mp_tournament.GetBool()` (teamplayroundbased_gamerules.cpp:3488).
        bool tournamentMode = state.ConVars.GetBool("mp_tournament");
        bool mannVsMachine = state.Rules.MannVsMachine;

        foreach (Core.Scene.SceneTeam team in state.Teams ?? [])
        {
            (string scoreVar, string countVar, string nameVar) = team.TeamNumber switch
            {
                TeamRed => ("redteamscore", "redteamplayercount", "redteamname"),
                TeamBlue => ("blueteamscore", "blueteamplayercount", "blueteamname"),
                _ => (string.Empty, string.Empty, string.Empty),
            };

            if (scoreVar.Length == 0)
            {
                continue;
            }

            int count = team.Players.Count;

            // `#TF_ScoreBoard_Player` for exactly one, else `#TF_ScoreBoard_Players` — both take `%s1` as the count.
            string? format = Find(count == 1 ? "#TF_ScoreBoard_Player" : "#TF_ScoreBoard_Players");

            SetDialogVariable(countVar, VguiLocalize.ConstructString(format, IdChars, count.ToString(CultureInfo.InvariantCulture)));
            SetDialogVariable(scoreVar, team.Score);
            SetDialogVariable(nameVar, LocalizedTeamName(team.TeamNumber, state));
        }

        bool showTournamentName = tournamentMode && !mannVsMachine;

        RedTeamName.Visible = showTournamentName;
        BlueTeamName.Visible = showTournamentName;
    }

    /// <summary>`C_TFTeam::Get_Localized_Name` after `UpdateTeamName` (c_tf_team.cpp:111).</summary>
    /// <remarks>
    /// **Not modelled:** in competitive or casual matchmaking, a premade party's leader or an event team names the team
    /// (:120-156); `HasPremadeParties`/`GetEventTeamStatus` are not decoded, so the name falls through to the localized one,
    /// as it does for a match with neither.
    /// </remarks>
    private string LocalizedTeamName(int teamNumber, HudState state)
    {
        if (state.ConVars.GetBool("mp_tournament") && teamNumber is TeamRed or TeamBlue && !state.Rules.IsCompetitiveMode)
        {
            // mp_tournament_blueteamname / mp_tournament_redteamname (tf_gamerules.cpp:782-783).
            string name = state.ConVars.GetString(teamNumber == TeamBlue ? "mp_tournament_blueteamname" : "mp_tournament_redteamname");

            if (name.Length > 0)
            {
                return name;
            }
        }

        return teamNumber switch
        {
            TeamBlue => Find("#TF_BlueTeam_Name") ?? "BLU",
            TeamRed when state.Rules.MannVsMachine => Find("#TF_Defenders") ?? "DEFENDERS",
            TeamRed => Find("#TF_RedTeam_Name") ?? "RED",
            _ => string.Empty,
        };
    }

    // `ConstructString_safe`'s destination buffer size (:1013) — how many characters a formatted string may reach.
    private const int IdChars = 1024;

    /// <summary>`g_pVGuiLocalize->Find`: a leading '#' is skipped when there is one.</summary>
    private string? Find(string token) =>
        HudViewport.Of(this)?.Context?.Localize?.Invoke(token.StartsWith('#') ? token[1..] : token);

    /// <summary>`UpdatePlayerList` (:1244-1641): rebuilds both lists from this tick's scoreboard slots.</summary>
    /// <param name="players">Every scoreboard slot this tick.</param>
    /// <param name="names">`GetPlayerName` by entity index — the roster.</param>
    /// <param name="localPlayerIndex">`GetLocalPlayerIndex()`.</param>
    public void UpdatePlayerList(IReadOnlyList<SceneScoreboardPlayer>? players, IReadOnlyDictionary<int, string>? names, int localPlayerIndex)
    {
        PlayerListBlue.ClearSelection();
        PlayerListRed.ClearSelection();
        PlayerListBlue.ClearItems();
        PlayerListRed.ClearItems();

        foreach (SceneScoreboardPlayer player in players ?? [])
        {
            if (!player.Connected && !player.Valid)
            {
                continue;
            }

            VguiSectionedListPanel? list = player.Team switch
            {
                TeamBlue => PlayerListBlue,
                TeamRed => PlayerListRed,
                _ => null,
            };

            if (list is null)
            {
                continue;
            }

            string name = names is not null && names.TryGetValue(player.EntityIndex, out string? found) ? found : string.Empty;

            Dictionary<string, string> data = new(StringComparer.Ordinal)
            {
                ["playerIndex"] = player.EntityIndex.ToString(CultureInfo.InvariantCulture),
                ["name"] = name,

                // GetTotalScore (:1377), the scoreboard's own "score" column — not GetPlayerScore.
                ["score"] = player.TotalScore.ToString(CultureInfo.InvariantCulture),

                // Every listed slot is treated as fully connected — see remarks.
                ["connected"] = "2",
                ["ping"] = player.Ping.ToString(CultureInfo.InvariantCulture),
                ["class"] = (player.PlayerClass ?? 0).ToString(CultureInfo.InvariantCulture),

                // Placeholders for the image columns this port does not draw (see remarks).
                ["medal"] = "0",
                ["killstreak"] = string.Empty,
                ["killstreak_image"] = "0",
                ["dominating"] = "0",
                ["nemesis"] = "0",
            };

            int itemId = list.AddItem(0, data);

            // `g_PR->GetTeamColor( nTeam )` (:1545): `COLOR_RED`/`COLOR_BLUE` (shareddefs.h:565), set by
            // `C_TFPlayerResource` (c_tf_playerresource.cpp:61) — then the dead-player colour (:1548-1566): Valve's own
            // literals, changed by team and by whether this is the local player.
            (byte, byte, byte, byte) colour = player.Team == TeamRed ? ((byte)255, (byte)64, (byte)64, (byte)255) : ((byte)153, (byte)204, (byte)255, (byte)255);

            if (!player.Alive)
            {
                colour = player.Team switch
                {
                    TeamRed => player.EntityIndex == localPlayerIndex ? ((byte)182, (byte)75, (byte)75, (byte)255) : ((byte)135, (byte)83, (byte)83, (byte)255),
                    _ => player.EntityIndex == localPlayerIndex ? ((byte)123, (byte)153, (byte)187, (byte)255) : ((byte)81, (byte)97, (byte)129, (byte)255),
                };
            }

            list.SetItemFgColor(itemId, colour);
            list.SetItemBgColor(itemId, (0, 0, 0, 80));

            if (ScoreFontDefault is { } font)
            {
                list.SetItemFont(itemId, font);
            }

            if (player.EntityIndex == localPlayerIndex)
            {
                list.SetSelectedItem(itemId);
            }
        }
    }

    private static int GetInt(IReadOnlyDictionary<string, string>? data, string key) =>
        data is not null && data.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;
}
