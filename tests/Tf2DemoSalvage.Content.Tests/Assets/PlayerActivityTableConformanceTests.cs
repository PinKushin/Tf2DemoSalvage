using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The player's own activity tables — kart, competitive loser, loser, carrying a building — against the SDK (B437).
/// </summary>
/// <remarks>
/// **`CTFPlayerAnimState::TranslateActivity` asks the PLAYER before the weapon** (`tf_playeranimstate.cpp:124-153`):
/// `ActivityOverride` walks one of four tables chosen by the player's state (`:223-269`, tables `:157-221`), and only
/// then the held weapon's. Only the weapon's was ported, so a humiliated loser ran, stood and jumped like a winner,
/// and an engineer carrying a building like one with a wrench. Compared row for row, both directions, as
/// <c>WeaponActivityConformanceTests</c> does for the weapon tables.
/// </remarks>
public sealed class PlayerActivityTableConformanceTests
{
    private const string AnimState = "src/game/shared/tf/tf_playeranimstate.cpp";

    [TestCase(PlayerActivityOverride.KartState, "KARTSTATE")]
    [TestCase(PlayerActivityOverride.LoserState, "LOSERSTATE")]
    [TestCase(PlayerActivityOverride.CompetitiveLoserState, "COMPETITIVELOSERSTATE")]
    [TestCase(PlayerActivityOverride.BuildingDeployed, "BUILDINGDEPLOYED")]
    public void PlayerActivityTable_EachTable_MatchesTheSdkBothWays(PlayerActivityOverride table, string sdkName)
    {
        if (SourceSdk.Text(AnimState) is not { } source)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        Dictionary<string, Dictionary<string, string>> sdk = Parse(source);
        sdk.ShouldContainKey(sdkName, "the control: the parse must find the table");
        sdk[sdkName].Count.ShouldBeGreaterThan(0);

        IReadOnlyDictionary<string, string> ours = PlayerActivityTable.For(table);

        foreach ((string from, string to) in sdk[sdkName])
        {
            ours.ContainsKey(from).ShouldBeTrue($"{sdkName} maps {from} in the SDK and not here");
            ours[from].ShouldBe(to);
        }

        foreach ((string from, string to) in ours)
        {
            sdk[sdkName].ContainsKey(from).ShouldBeTrue($"{sdkName} maps {from} here and not in the SDK");
            sdk[sdkName][from].ShouldBe(to);
        }
    }

    [Test]
    public void Translate_TheLoser_RunsAndStandsAsTheLoserAndTheWeaponKeepsTheRest()
    {
        // The player's table first, then the weapon's (:127-131): ACT_MP_RUN becomes ACT_MP_RUN_LOSERSTATE, which no
        // weapon table rewrites, so it reaches the model as that; a reload, which the loser table leaves alone, is the
        // weapon's.
        PlayerActivityTable.Translate("ACT_MP_RUN", "PRIMARY", PlayerActivityOverride.LoserState, competitiveWinnerClass: null)
            .ShouldBe("ACT_MP_RUN_LOSERSTATE");
        PlayerActivityTable.Translate("ACT_MP_JUMP_LAND", "PRIMARY", PlayerActivityOverride.LoserState, competitiveWinnerClass: null)
            .ShouldBe("ACT_MP_JUMP_LAND_LOSERSTATE");
        PlayerActivityTable.Translate("ACT_MP_RELOAD_STAND", "PRIMARY", PlayerActivityOverride.LoserState, competitiveWinnerClass: null)
            .ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY");

        // The control: no override, the weapon's table alone.
        PlayerActivityTable.Translate("ACT_MP_RUN", "PRIMARY", PlayerActivityOverride.None, competitiveWinnerClass: null)
            .ShouldBe("ACT_MP_RUN_PRIMARY");
    }

    [Test]
    public void Translate_ACompetitiveWinner_StandsInTheWinnersPoseOnlyWhereTheEngineSaysSo()
    {
        // :142-151 — after the weapon: STAND_PRIMARY for anyone, STAND_MELEE for a spy, STAND_SECONDARY for a demoman.
        PlayerActivityTable.Translate("ACT_MP_STAND_IDLE", "PRIMARY", PlayerActivityOverride.None, competitiveWinnerClass: 3)
            .ShouldBe("ACT_MP_COMPETITIVE_WINNERSTATE");
        PlayerActivityTable.Translate("ACT_MP_STAND_IDLE", "MELEE", PlayerActivityOverride.None, competitiveWinnerClass: 8)
            .ShouldBe("ACT_MP_COMPETITIVE_WINNERSTATE");
        PlayerActivityTable.Translate("ACT_MP_STAND_IDLE", "MELEE", PlayerActivityOverride.None, competitiveWinnerClass: 3)
            .ShouldBe("ACT_MP_STAND_MELEE", "only a spy's melee stand");
        PlayerActivityTable.Translate("ACT_MP_STAND_IDLE", "SECONDARY", PlayerActivityOverride.None, competitiveWinnerClass: 4)
            .ShouldBe("ACT_MP_COMPETITIVE_WINNERSTATE");
        PlayerActivityTable.Translate("ACT_MP_RUN", "PRIMARY", PlayerActivityOverride.None, competitiveWinnerClass: 3)
            .ShouldBe("ACT_MP_RUN_PRIMARY", "a run is not a stand");
    }

    /// <remarks>
    /// **The item's `animation_replacement` sits between the weapon and the winner** (`:135-139`): it is keyed on the
    /// activity the weapon's table left, so a replacement of the bare activity never matches a weapon that rewrote it.
    /// </remarks>
    [Test]
    public void Translate_AnItemReplacement_AppliesToTheWeaponsAnswerBeforeTheWinner()
    {
        Dictionary<string, string> item = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ACT_MP_RELOAD_STAND_PRIMARY"] = "ACT_MP_RELOAD_STAND_PRIMARY3",
            ["ACT_MP_RELOAD_STAND"] = "ACT_MP_RELOAD_STAND_SECONDARY2",
            ["ACT_MP_STAND_PRIMARY"] = "ACT_MP_STAND_SECONDARY",
        };

        PlayerActivityTable.Translate("ACT_MP_RELOAD_STAND", "PRIMARY", PlayerActivityOverride.None, null, item)
            .ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY3");
        PlayerActivityTable.Translate("ACT_MP_STAND_IDLE", "PRIMARY", PlayerActivityOverride.None, competitiveWinnerClass: 3, item)
            .ShouldBe("ACT_MP_STAND_SECONDARY", "the item turned the stand away from the winner's");
        PlayerActivityTable.Translate("ACT_MP_RELOAD_STAND", "PRIMARY", PlayerActivityOverride.None, null)
            .ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY", "the control: no item");
    }

    [Test]
    public void TranslateActivity_TheEngine_AsksThePlayerThenTheWeaponThenTheWinner()
    {
        if (SourceSdk.Text(AnimState) is not { } source)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        source.ShouldMatch(
            @"(?s)Activity\s+CTFPlayerAnimState::TranslateActivity\(\s*Activity\s+actDesired\s*\)\s*\{.{0,120}?" +
            @"translateActivity\s*=\s*ActivityOverride\(\s*translateActivity\s*,\s*NULL\s*\);.{0,200}?" +
            @"pWeapon->ActivityOverride\(\s*translateActivity\s*,\s*NULL\s*\);.{0,600}?TF_COND_COMPETITIVE_WINNER");

        // And ActivityOverride's own precedence: kart, then the competitive loser, then the loser, then carrying.
        source.ShouldMatch(
            @"(?s)TF_COND_HALLOWEEN_KART.{0,200}?s_acttableKartState.{0,200}?TF_COND_COMPETITIVE_LOSER.{0,200}?" +
            @"s_acttableCompetitiveLoserState.{0,100}?IsLoser\(\).{0,200}?s_acttableLoserState.{0,100}?IsCarryingObject\(\)");
    }

    /// <summary>Reads every <c>acttable_t s_acttableX[]</c> out of the file, first row winning.</summary>
    private static Dictionary<string, Dictionary<string, string>> Parse(string source)
    {
        Dictionary<string, Dictionary<string, string>> tables = new(StringComparer.Ordinal);

        foreach (Match table in Regex.Matches(
            source,
            @"acttable_t\s+s_acttable(?<name>[A-Za-z0-9]+)\s*\[\]\s*=\s*\{(?<body>.*?)\n\};",
            RegexOptions.Singleline))
        {
            Dictionary<string, string> rows = new(StringComparer.Ordinal);

            foreach (Match row in Regex.Matches(
                table.Groups["body"].Value,
                @"\{\s*(?<from>ACT_[A-Za-z0-9_]+)\s*,\s*(?<to>ACT_[A-Za-z0-9_]+)\s*,"))
            {
                _ = rows.TryAdd(row.Groups["from"].Value, row.Groups["to"].Value);
            }

            tables[table.Groups["name"].Value.ToUpperInvariant()] = rows;
        }

        return tables;
    }
}
