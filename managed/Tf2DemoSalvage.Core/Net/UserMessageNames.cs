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
/// **Era caveat, now measured for the ids the corpus exercises.** This is the registration order
/// of one build, and ids are assigned by position, so a message inserted rather than appended
/// shifts everything after it — the same trap as the property-type renumbering in RISKS B18. The
/// 2009 SDK ships no TF2 game code, so the old table cannot be diffed from source; see
/// `DECISIONS.md` D28 for what the corpus says instead.
///
/// **The head of the table is stable and the tail is not, and both halves are measured.**
///
/// Up to id 28, histogramming type against body width across protocols 11, 14, 15, 16 and 24 puts
/// Geiger at 0, Train at 1, TextMsg at 5, ResetHUD at 6, ItemPickup at 8, Shake at 10, Fade at 11,
/// VGUIMenu at 12, Rumble at 13, Damage at 18 and PlayerStatsUpdate at 28 in every era, with
/// matching widths — 8-bit Geigers, 24-bit Rumbles, 80- and 88-bit VGUIMenus. Eighteen years and
/// no movement.
///
/// **Above 28 it shifts twice.** `CheapBreakModel` is a short and a coordinate vector, so its full
/// form is 85 bits, and that width is unmistakable. It appears at id **40** in the 2009 demo, id
/// **41** in the 2011 pair, and id **42** in every protocol-24 file. Corroborated by a second
/// disagreement at the same time: id 52 carries a 32-bit body in 2011 where protocol 24 puts a
/// 229-bit `SpawnFlyingBird` there. So two messages were inserted rather than appended between
/// 2009 and 2013, and every id after them moved.
///
/// That is why <see cref="Lookup"/> withholds names above 28 below protocol 24. Reporting
/// `PlayerShieldBlocked` for the 2009 demo's id 40 — which is what this table did until
/// 2026-08-11 — is a wrong name on a correctly decoded message, the exact failure this file's
/// header warns about.
///
/// **Two lessons, both general.** Check alignment before suspecting a layout: protocol 14's
/// Damage misdecode had a shifted id as its first suspect, and one histogram ruled it out and
/// pointed at the layout instead (RISKS B26). And a *width* is what makes alignment checkable at
/// all — a message whose length is distinctive acts as a fingerprint for its own id.
///
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

    /// <summary>The registered name for an id, or <c>null</c> if it cannot be named safely.</summary>
    /// <param name="userMessageType">The id read from the wire.</param>
    /// <param name="networkProtocol">The demo header's network protocol.</param>
    /// <returns>The name, or <c>null</c> to report the id by number.</returns>
    /// <remarks>
    /// **Names above <see cref="LastStableId"/> are withheld below protocol 24, because the table
    /// demonstrably shifts there.** See the type's remarks for the measurement. Reporting the id
    /// by number is the honest answer: this table describes a build those demos predate.
    /// </remarks>
    internal static string? Lookup(int userMessageType, int networkProtocol)
    {
        if (userMessageType < 0 || userMessageType >= Names.Length)
        {
            return null;
        }

        return userMessageType <= LastStableId || networkProtocol >= ShiftedTableProtocol
            ? Names[userMessageType]
            : null;
    }

    /// <summary>
    /// Highest id measured to mean the same thing at every protocol the corpus holds.
    /// </summary>
    /// <remarks>
    /// <c>PlayerStatsUpdate</c>, confirmed at protocols 11, 14, 15, 16 and 24 with matching widths.
    /// Everything below it agrees too. Above it, the table moves — see the type's remarks.
    /// </remarks>
    private const int LastStableId = 28;

    /// <summary>First protocol this table is known to describe exactly.</summary>
    /// <remarks>
    /// The transcription is from the 2013 SDK, and the 2013 demo is protocol 24. Whether the table
    /// also holds for protocols 17–23 is untested — no specimen exists — so this is a boundary at
    /// the edge of the evidence rather than in the middle of it.
    /// </remarks>
    private const int ShiftedTableProtocol = 24;
}
