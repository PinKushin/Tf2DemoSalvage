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
/// **Not wired into the viewer's key bindings or `MainForm` yet** — `+showscores` and this dialog's own visibility are
/// left for later; this element only builds and updates its two lists.
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
/// `UpdateTeamInfo`'s team score and name (`C_TFTeam::Get_Score`/`Get_Localized_Name`, :980-1062) — this project
/// has not decoded a `CTFTeam` entity's own score; only the player counts, which come straight off
/// <see cref="SceneScoreboardPlayer"/>, are set.
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

    /// <summary>`CTFClientScoreBoardDialog( IViewPort *pViewPort )`: both lists made up front, so the `.res` finds them by name.</summary>
    /// <param name="parent">The parent.</param>
    public TfClientScoreBoardDialog(VguiPanel? parent)
        : base(parent, "Scoreboard")
    {
        PlayerListBlue = new VguiSectionedListPanel(this, "BluePlayerList");
        PlayerListRed = new VguiSectionedListPanel(this, "RedPlayerList");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFClientScoreBoardDialog";

    /// <summary>`ShowAvatars()` (`clientscoreboarddialog.h:50`): `IsPC()`, always true here.</summary>
    public static bool ShowAvatars => true;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:304): loads the `.res`, then re-inits both lists.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        LoadControlSettings(ResFile, context);

        InitPlayerList(PlayerListBlue);
        InitPlayerList(PlayerListRed);
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

    /// <summary>`UpdateTeamInfo` (:980-1062): here, only the player count each list would show — see remarks.</summary>
    /// <param name="players">Every scoreboard slot this tick.</param>
    /// <returns>Blue and red player counts.</returns>
    public static (int Blue, int Red) UpdateTeamInfo(IReadOnlyList<SceneScoreboardPlayer>? players)
    {
        int blue = 0;
        int red = 0;

        foreach (SceneScoreboardPlayer player in players ?? [])
        {
            if (!player.Connected && !player.Valid)
            {
                continue;
            }

            switch (player.Team)
            {
                case TeamBlue:
                    blue++;
                    break;
                case TeamRed:
                    red++;
                    break;
                default:
                    break;
            }
        }

        return (blue, red);
    }

    /// <summary>`UpdatePlayerList` (:1244-1641): rebuilds both lists from this tick's scoreboard slots.</summary>
    /// <param name="players">Every scoreboard slot this tick.</param>
    /// <param name="names">`GetPlayerName` by entity index — the roster.</param>
    /// <param name="localPlayerIndex">`GetLocalPlayerIndex()`.</param>
    public void UpdatePlayerList(IReadOnlyList<SceneScoreboardPlayer>? players, IReadOnlyDictionary<int, string>? names, int localPlayerIndex)
    {
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

            // The dead-player colour (:1541-1566): Valve's own literals, changed by team and by
            // whether this is the local player.
            if (!player.Alive)
            {
                (byte, byte, byte, byte) dead = player.Team switch
                {
                    TeamRed => player.EntityIndex == localPlayerIndex ? ((byte)182, (byte)75, (byte)75, (byte)255) : ((byte)135, (byte)83, (byte)83, (byte)255),
                    _ => player.EntityIndex == localPlayerIndex ? ((byte)123, (byte)153, (byte)187, (byte)255) : ((byte)81, (byte)97, (byte)129, (byte)255),
                };

                list.SetItemFgColor(itemId, dead);
            }
        }
    }

    private static int GetInt(IReadOnlyDictionary<string, string>? data, string key) =>
        data is not null && data.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : 0;
}
