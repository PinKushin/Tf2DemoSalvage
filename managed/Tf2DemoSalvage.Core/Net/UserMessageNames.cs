using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// TF2's user message names, in the order the game registers them.
/// </summary>
/// <remarks>
/// **This is the July 2026 client's table, verified against the binary.** A user message carries
/// no name on the wire — only an id, which is registration order — so a wrong table renames every
/// message in a trace without failing anything.
///
/// It was transcribed from `game/shared/tf/tf_usermessages.cpp` in the sdk2013 drop, and was
/// described here as "the 2013 table" until 2026-08-11, when the registration sequence was read
/// out of six shipped clients. It matches the **live 2026** client entry for entry, ids 0–78
/// ending at `BuiltObject`. The March 2013 client registers only 66 and does not contain
/// `RDTeamPointsChanged` anywhere — that name was inserted at id 51 later. So the SDK drop
/// describes a build years newer than its name, and the data here is right for modern demos and
/// wrong above id 50 for early protocol-24 ones. See `RISKS.md` B29.
///
/// **The table is not the whole id space.** A second registration block follows it — six Novint
/// Falcon haptics messages, `SPHapWeapEvent`, `HapDmg`, `HapPunch`, `HapSetDrag`, `HapSetConst`,
/// `HapMeleeContact` — which is why ids exactly four past the end of each era's table appear in
/// real demos. Those are `HapSetDrag`, and this project does not name them yet.
///
/// `SayText2` landing at 4 is the cross-check that the ordering is right: that constant was
/// proven against real chat in real demos long before this table existed.
///
/// **Era caveat, now measured against every era's binary.** This is the registration order of one
/// build, and ids are assigned by position, so a message inserted rather than appended shifts
/// everything after it — the same trap as the property-type renumbering in RISKS B18. The lengths
/// are: 29 entries in 2007 and 2008 (ending at `PlayerStatsUpdate`), 41 in 2009, 49 in 2011, 66 in
/// March 2013, 79 today.
///
/// **The head of the table is stable and the tail is not, and both halves are measured.**
///
/// Up to id 28, histogramming type against body width across protocols 11, 14, 15, 16 and 24 puts
/// Geiger at 0, Train at 1, TextMsg at 5, ResetHUD at 6, ItemPickup at 8, Shake at 10, Fade at 11,
/// VGUIMenu at 12, Rumble at 13, Damage at 18 and PlayerStatsUpdate at 28 in every era, with
/// matching widths — 8-bit Geigers, 24-bit Rumbles, 80- and 88-bit VGUIMenus. Eighteen years and
/// no movement, and the six binaries agree with the histogram exactly.
///
/// **Above 28 it grows.** `CheapBreakModel` is a short and a coordinate vector, so its full form
/// is 85 bits, and that width is unmistakable. It appears at id **40** in the 2009 demo, id **41**
/// in the 2011 pair, and id **42** in every protocol-24 file — all three now confirmed against the
/// registration order in those builds' own clients.
///
/// **The second disagreement was read wrong for a day, and the correction is the useful part.**
/// Ids 44, 52 and 69 carry 32-bit bodies in the 2009, 2011 and March 2013 demos, and were taken as
/// evidence of messages inserted mid-table. They are not: each sits exactly four past the end of
/// its own build's table (40, 48, 65), because a second registration block follows the game's and
/// `HapSetDrag` is its fourth entry. A consistent offset across three eras with three different
/// table lengths is a structure, not three coincidences — and nothing in the corpus could have
/// said so, because the extra block is not in the SDK at all.
///
/// That is why <see cref="Lookup"/> withholds names above 28 below protocol 24. Reporting
/// `PlayerShieldBlocked` for the 2009 demo's id 40 — which is what this table did until
/// 2026-08-11 — is a wrong name on a correctly decoded message, the exact failure this file's
/// header warns about.
///
/// **Three lessons, all general.** Check alignment before suspecting a layout: protocol 14's
/// Damage misdecode had a shifted id as its first suspect, and one histogram ruled it out and
/// pointed at the layout instead (RISKS B26). A *width* is what makes alignment checkable at
/// all — a message whose length is distinctive acts as a fingerprint for its own id. And the
/// registration table is not the id space: an id past the end of it is not evidence of a shift,
/// it is evidence of another table.
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

    /// <summary>
    /// The six Novint Falcon haptics messages, registered immediately after the game's table.
    /// </summary>
    /// <remarks>
    /// **This block is why ids past the end of the game table appear in ordinary demos.** The
    /// server sends these to every client regardless of whether anyone owns the device, so they
    /// are in normal recordings — `HapSetDrag`, the fourth, accounts for the ids that sat unnamed
    /// in the corpus for weeks at 44 (2009), 52 (2011) and 69 (March 2013), each exactly four past
    /// its own build's last game message. A drag value is one float, which is the 32-bit body
    /// measured on all three.
    ///
    /// Absent from the 2007 and 2008 clients entirely, which is exactly why those two eras are the
    /// only ones in the corpus with no unnamed ids at all.
    /// </remarks>
    private static readonly string[] Haptics =
        ["SPHapWeapEvent", "HapDmg", "HapPunch", "HapSetDrag", "HapSetConst", "HapMeleeContact"];

    // Stryker restore String

    /// <summary>Builds one era's table by removing what its build had not shipped and truncating.</summary>
    /// <param name="lastGameMessage">The last message that build registers; everything after is dropped.</param>
    /// <param name="absent">Names that build does not contain at all, wherever they appear.</param>
    /// <param name="haptics">Whether the haptics block follows the game table in that build.</param>
    /// <remarks>
    /// **Derived rather than transcribed five times, deliberately.** The eras share long prefixes,
    /// so five literal arrays would be ~250 lines of near-duplicate data that could drift apart
    /// silently. Expressing each era as a *difference* from the current table also states the
    /// finding directly: the shifts are insertions, not appends, and each removal below names the
    /// message a given build had not shipped yet.
    ///
    /// The lengths this must produce are the era fingerprints — 29, 41, 49, 79 game messages —
    /// and <c>UserMessageNamesTests</c> asserts them, because an off-by-one here renames every
    /// message above the mistake without failing anything else.
    /// </remarks>
    private static string[] Compose(string lastGameMessage, string[] absent, bool haptics)
    {
        List<string> names = new(Names.Length + Haptics.Length);
        foreach (string name in Names)
        {
            if (Array.IndexOf(absent, name) >= 0)
            {
                continue;
            }

            names.Add(name);
            if (string.Equals(name, lastGameMessage, StringComparison.Ordinal))
            {
                break;
            }
        }

        if (haptics)
        {
            names.AddRange(Haptics);
        }

        return [.. names];
    }

    /// <summary>2007 and 2008: 29 messages, no haptics block.</summary>
    private static readonly string[] Launch =
        Compose("PlayerStatsUpdate", [], haptics: false);

    /// <summary>2009, build 3862: 41 messages, then haptics at 41–46.</summary>
    private static readonly string[] Era2009 =
        Compose("CheapBreakModel", ["MapStatsUpdate", "TrainingObjective"], haptics: true);

    /// <summary>2011, build 4604: 49 messages, then haptics at 49–54.</summary>
    private static readonly string[] Era2011 =
        Compose("PlayerBonusPoints", ["MapStatsUpdate", "BreakModelRocketDud"], haptics: true);

    /// <summary>July 2026: all 79 messages, then haptics at 79–84.</summary>
    private static readonly string[] Current = Compose("BuiltObject", [], haptics: true);

    /// <summary>The registered name for an id in the era that recorded it, or <c>null</c>.</summary>
    /// <param name="userMessageType">The id read from the wire.</param>
    /// <param name="networkProtocol">The demo header's network protocol.</param>
    /// <returns>The name, or <c>null</c> to report the id by number.</returns>
    internal static string? Lookup(int userMessageType, int networkProtocol)
    {
        string[] table = TableFor(networkProtocol);
        return userMessageType < 0 || userMessageType >= table.Length
            ? null
            : table[userMessageType];
    }

    /// <summary>The table for a protocol, or the shared head where no build has been measured.</summary>
    /// <remarks>
    /// **Each arm is a client that was read, not a guess.** Protocols 12 and 13 fall to the launch
    /// table because 11 and 14 bracket them and agree; that is interpolation across an interval
    /// whose endpoints are identical, which is the only kind this project accepts.
    ///
    /// **Protocols 17–23 get the launch table, which names only the head every era shares.** No
    /// client and no demo survives from that window, and the two tables on either side disagree
    /// about id 40 — so naming it would be picking one at random. Reporting the number is the
    /// honest answer.
    ///
    /// **Protocol 24 is not one era, and this is the known gap (`RISKS.md` B29.)** It spans March
    /// 2013 to now. The March 2013 client registers 66 messages and has no `RDTeamPointsChanged`;
    /// the current one registers 79 with it inserted at id 51, so every id from 51 up means two
    /// different things under one protocol number. The current table is used because it is right
    /// for the overwhelming majority of protocol-24 demos and identical to the 2013 table below
    /// id 51. Distinguishing them needs evidence from the demo's *contents* rather than its
    /// header — the ids it actually carries — which is a decode-wide question, not one this
    /// function can answer from an id and a protocol.
    /// </remarks>
    private static string[] TableFor(int networkProtocol) => networkProtocol switch
    {
        >= 24 => Current,
        16 => Era2011,
        15 => Era2009,

        // 14 and below, and the unmeasured 17-23, share this arm because they need the same
        // answer for opposite reasons: 11 to 14 *is* the launch table, measured, while 17 to 23
        // gets it as the largest table nothing contradicts. Splitting them into two arms
        // returning the same array would only add a branch no test could distinguish.
        _ => Launch,
    };
}
