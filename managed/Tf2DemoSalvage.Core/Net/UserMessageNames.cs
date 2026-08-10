namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// TF2's user message names, in the order the game registers them.
/// </summary>
/// <remarks>
/// **Generated from `game/shared/tf/tf_usermessages.cpp` in the TF2 SDK, not recalled.** A user
/// message carries no name on the wire — only an id, which is that file's registration order —
/// so a wrong table renames every message in a trace without failing anything.
///
/// `SayText2` landing at 4 is the cross-check that the ordering is right: that constant was
/// proven against real chat in real demos long before this table existed.
///
/// **Era caveat.** This is the registration order of one build. Ids are assigned by position, so
/// a message inserted rather than appended shifts everything after it — the same trap as the
/// property-type renumbering in RISKS B18. The 2009 SDK ships no TF2 game code, so the old table
/// cannot be diffed from source; see `DECISIONS.md` D28 for what the corpus says instead.
/// Anything past the end of this table is reported by number with no name.
/// </remarks>
internal static class UserMessageNames
{
    // Stryker disable String: this is transcribed data, not logic, and a per-name mutant can only
    // be killed by asserting that name back — 79 change-detectors that break on every SDK update
    // and catch nothing. They were 80 of the project's 147 survivors, over half the total, and
    // reading them buried the real findings.
    //
    // What can actually go wrong here is ALIGNMENT: a message inserted rather than appended
    // shifts every id after it. That is covered outside this region, by the first, last and
    // SayText2 anchors in NetMessageReaderTests, and by the bounds on Lookup below. A wrong name
    // at a correct index is only findable by diffing tf_usermessages.cpp, which no test can do.
    private static readonly string[] Names =
    [
        "Geiger",
        "Train",
        "HudText",
        "SayText",
        "SayText2",
        "TextMsg",
        "ResetHUD",
        "GameTitle",
        "ItemPickup",
        "ShowMenu",
        "Shake",
        "Fade",
        "VGUIMenu",
        "Rumble",
        "CloseCaption",
        "SendAudio",
        "VoiceMask",
        "RequestState",
        "Damage",
        "HintText",
        "KeyHintText",
        "HudMsg",
        "AmmoDenied",
        "AchievementEvent",
        "UpdateRadar",
        "VoiceSubtitle",
        "HudNotify",
        "HudNotifyCustom",
        "PlayerStatsUpdate",
        "MapStatsUpdate",
        "PlayerIgnited",
        "PlayerIgnitedInv",
        "HudArenaNotify",
        "UpdateAchievement",
        "TrainingMsg",
        "TrainingObjective",
        "DamageDodged",
        "PlayerJarated",
        "PlayerExtinguished",
        "PlayerJaratedFade",
        "PlayerShieldBlocked",
        "BreakModel",
        "CheapBreakModel",
        "BreakModel_Pumpkin",
        "BreakModelRocketDud",
        "CallVoteFailed",
        "VoteStart",
        "VotePass",
        "VoteFailed",
        "VoteSetup",
        "PlayerBonusPoints",
        "RDTeamPointsChanged",
        "SpawnFlyingBird",
        "PlayerGodRayEffect",
        "PlayerTeleportHomeEffect",
        "MVMStatsReset",
        "MVMPlayerEvent",
        "MVMResetPlayerStats",
        "MVMWaveFailed",
        "MVMAnnouncement",
        "MVMPlayerUpgradedEvent",
        "MVMVictory",
        "MVMWaveChange",
        "MVMLocalPlayerUpgradesClear",
        "MVMLocalPlayerUpgradesValue",
        "MVMResetPlayerWaveSpendingStats",
        "MVMLocalPlayerWaveSpendingValue",
        "MVMResetPlayerUpgradeSpending",
        "MVMServerKickTimeUpdate",
        "PlayerLoadoutUpdated",
        "PlayerTauntSoundLoopStart",
        "PlayerTauntSoundLoopEnd",
        "ForcePlayerViewAngles",
        "BonusDucks",
        "EOTLDuckEvent",
        "PlayerPickupWeapon",
        "QuestObjectiveCompleted",
        "SdkRequestEquipment",
        "BuiltObject",
    ];

    // Stryker restore String

    /// <summary>The registered name for an id, or <c>null</c> if it is past the table.</summary>
    /// <param name="userMessageType">The id read from the wire.</param>
    /// <returns>The name, or <c>null</c>.</returns>
    internal static string? Lookup(int userMessageType) =>
        userMessageType >= 0 && userMessageType < Names.Length
            ? Names[userMessageType]
            : null;
}
