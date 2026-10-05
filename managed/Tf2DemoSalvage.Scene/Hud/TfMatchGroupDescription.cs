namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `IMatchGroupDescription` (tf_match_description.h:44), the client half the match HUD asks: the round sign's and the
/// doors' skin and logo, and the flags `CTFHudMatchStatus` branches on.
/// </summary>
/// <param name="RoundStartBanner">`BGetRoundStartBannerParameters`: skin and bodygroup for a round count, or null to show none.</param>
/// <param name="RoundDoor">`BGetRoundDoorParameters`: skin and logo bodygroup, or null to show none.</param>
/// <param name="UsesPostRoundDoors">`BUsesPostRoundDoors`.</param>
/// <param name="UseMatchSummaryStage">`BUseMatchSummaryStage`.</param>
/// <param name="UsesStickyRanks">`BUsesStickyRanks`.</param>
/// <param name="MatchStartSound">`GetMatchStartSound`.</param>
/// <param name="MatchType">`GetMatchType()`: `EMatchType_t` (tf_match_description.h:36-42).</param>
public sealed record TfMatchGroupDescription(
    System.Func<int, (int Skin, int BodyGroup)>? RoundStartBanner,
    (int Skin, int LogoBodyGroup)? RoundDoor,
    bool UsesPostRoundDoors,
    bool UseMatchSummaryStage,
    bool UsesStickyRanks,
    string? MatchStartSound,
    int MatchType)
{
    /// <summary>`k_eTFMatchGroup_Invalid`.</summary>
    public const int Invalid = -1;

    /// <summary>`MATCH_TYPE_MVM`.</summary>
    public const int MatchTypeMvm = 1;

    /// <summary>`MATCH_TYPE_COMPETITIVE`.</summary>
    public const int MatchTypeCompetitive = 2;

    /// <summary>`MATCH_TYPE_CASUAL`.</summary>
    public const int MatchTypeCasual = 3;

    // `CMvMMatchGroupDescription` (tf_match_description_mvm.cpp:18): no banner, no doors (:44-54), neither set of doors.
    private static readonly TfMatchGroupDescription MannVsMachine = new(null, null, false, false, false, null, MatchTypeMvm);

    // `CLadderMatchGroupDescription` (tf_match_description_comp.cpp:18). "The comp skins start at skin 8" (:96). The high
    // skill door skin needs the GC lobby's `initial_average_mm_rating` (:75), which a demo's client never has: its rating
    // is then the progression's start, below level 10's, so skin 0. `MATCH_TYPE_COMPETITIVE` (:49).
    private static readonly TfMatchGroupDescription Ladder = new(
        rounds => (8 + rounds, 1), (0, 0), true, true, true, "MatchMaking.RoundStart", MatchTypeCompetitive);

    // `CCasualMatchGroupDescription` (tf_match_description_casual.cpp:18), `MATCH_TYPE_CASUAL` (:54).
    private static readonly TfMatchGroupDescription Casual = new(
        rounds => (rounds, 0), (3, 1), true, false, false, "MatchMaking.RoundStartCasual", MatchTypeCasual);

    /// <summary>
    /// `CTFGameRules::IsCompetitiveMode` (tf_gamerules.cpp:2214): the current group's description is competitive or casual;
    /// false with none.
    /// </summary>
    /// <param name="matchGroup">`GetCurrentMatchGroup()`.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsCompetitiveMode(int matchGroup) =>
        For(matchGroup)?.MatchType is MatchTypeCompetitive or MatchTypeCasual;

    /// <summary>`GetMatchGroupDescription( eGroup )` (tf_match_description.cpp:22): the registered groups, else null.</summary>
    /// <param name="matchGroup">`GetCurrentMatchGroup()`.</param>
    /// <returns>The description, or null.</returns>
    public static TfMatchGroupDescription? For(int matchGroup) => matchGroup switch
    {
        0 or 1 => MannVsMachine, // k_eTFMatchGroup_MvM_Practice, MvM_MannUp
        2 => Ladder, // k_eTFMatchGroup_Ladder_6v6
        7 => Casual, // k_eTFMatchGroup_Casual_12v12

        // `CEventPlaceholderMatchGroupDescription` (:226): the ladder's, without the summary stage (:236).
        8 => Ladder with { UseMatchSummaryStage = false },
        _ => null,
    };
}
