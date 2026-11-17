using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `C_TFPlayer`'s overhead effects, `m_mapOverheadEffects`: which icons a player carries over his head and in what order
/// (B765).
/// </summary>
/// <remarks>
/// The engine, quoted where each test reads it:
/// - `OnDataChanged`, inside `if ( !IsHLTV() )` (`c_tf_player.cpp:4557`, `:4564-4587`): the IT icon when
///   `TFGameRules()->IsIT( this ) &amp;&amp; !IsLocalPlayer()`, the dueling icon, then the nemesis icon when neither of those
///   shows and `ShouldShowNemesisIcon()`.
/// - `ShouldShowNemesisIcon` (`:9376-9395`): not in a tournament that hides them, not over a custom model, and only for the
///   local player's nemesis who is connected, alive, not stealthed and not disguised.
/// - `ClientThink` (`:6109-6111`): `UpdateRuneIcon()` then `UpdatedMarkedForDeathEffect()`, neither for the local player.
/// - Entering dormancy removes them all (`:4317-4330`).
/// - `AddOverheadEffect` will not add a name twice (`:11570-11592`); `m_mapOverheadEffects` is a `CUtlMap`, walked by
///   node index (`FOR_EACH_MAP_FAST`, `:11636`), whose freed nodes are reused last-freed first.
/// - The dueling opponent comes only from Game Coordinator messages (`tf_consumables.cpp:1937-1946`), which a demo does not
///   carry, so the dueling icon is never shown here.
/// </remarks>
public sealed class OverheadEffectsConformanceTests
{
    private const int Red = SceneTeams.Red;
    private const int Blu = SceneTeams.Blu;

    [TestCase(Red, "particle_nemesis_red", TestName = "Step_ARedNemesisOfTheRecorder_CarriesTheRedIcon")]
    [TestCase(Blu, "particle_nemesis_blue", TestName = "Step_ABluNemesisOfTheRecorder_CarriesTheBlueIcon")]
    public void Step_TheRecordersNemesis_CarriesHisTeamsIcon(int team, string icon)
    {
        OverheadEffects effects = new();

        effects.Step(Nemesis() with { Team = team });

        effects.Names.ShouldBe([icon]);
    }

    /// <remarks>Each of `ShouldShowNemesisIcon`'s refusals, one at a time; the unrefused nemesis above is the control.</remarks>
    [TestCase("custom model", TestName = "Step_ANemesisWithACustomModel_CarriesNoIcon")]
    [TestCase("tournament", TestName = "Step_ANemesisInATournamentHidingThem_CarriesNoIcon")]
    [TestCase("dead", TestName = "Step_ADeadNemesis_CarriesNoIcon")]
    [TestCase("disconnected", TestName = "Step_ADisconnectedNemesis_CarriesNoIcon")]
    [TestCase("cloaked", TestName = "Step_ACloakedNemesis_CarriesNoIcon")]
    [TestCase("disguised", TestName = "Step_ADisguisedNemesis_CarriesNoIcon")]
    [TestCase("sourcetv", TestName = "Step_ANemesisInASourceTvDemo_CarriesNoIcon")]
    [TestCase("no team", TestName = "Step_ANemesisOnNoTeam_CarriesNoIcon")]
    [TestCase("it", TestName = "Step_ANemesisWhoIsIt_CarriesOnlyTheItIcon")]
    public void Step_ANemesisRefused_CarriesNoNemesisIcon(string refusal)
    {
        OverheadEffects effects = new();
        OverheadInputs nemesis = Nemesis();

        effects.Step(refusal switch
        {
            "custom model" => nemesis with { HasCustomModel = true },
            "tournament" => nemesis with { HideDominationIcons = true },
            "dead" => nemesis with { Alive = false },
            "disconnected" => nemesis with { Connected = false },
            "cloaked" => nemesis with { Conditions = Conditions(PlayerConditions.Stealthed) },
            "disguised" => nemesis with { Conditions = Conditions(PlayerConditions.Disguised) },
            "sourcetv" => nemesis with { SourceTv = true },
            "no team" => nemesis with { Team = null },
            _ => nemesis with { IsIt = true },
        });

        effects.Names.ShouldBe(refusal == "it" ? [OverheadEffects.ItIcon] : []);
    }

    /// <remarks>
    /// **Marked for death shows under any of three conditions, but not cloaked, disguised or on the local player**
    /// (`UpdatedMarkedForDeathEffect`, `:5173-5199`).
    /// </remarks>
    [TestCase(30, false, false, true, TestName = "Step_APlayerMarkedForDeath_CarriesTheMark")]
    [TestCase(48, false, false, true, TestName = "Step_APlayerSilentlyMarkedForDeath_CarriesTheMark")]
    [TestCase(119, false, false, true, TestName = "Step_APasstimePenalty_CarriesTheMark")]
    [TestCase(30, true, false, false, TestName = "Step_TheRecorderMarkedForDeath_CarriesNone")]
    [TestCase(30, false, true, false, TestName = "Step_ADisguisedSpyMarkedForDeath_CarriesNone")]
    public void Step_MarkedForDeath_IsUpdatedMarkedForDeathEffect(int condition, bool local, bool disguised, bool shown)
    {
        OverheadEffects effects = new();
        PlayerConditions conditions = disguised
            ? Conditions(condition, PlayerConditions.Disguised)
            : Conditions(condition);

        effects.Step(Plain() with { IsLocal = local, Conditions = conditions });

        effects.Names.ShouldBe(shown ? [OverheadEffects.MarkIcon] : []);
    }

    /// <remarks>
    /// **A rune shows its type in the team's colours, an enemy's disguise lending his** (`UpdateRuneIcon`, `:5205-5243`;
    /// `s_pszRuneIcons`, tf_shareddefs.cpp:1961-1993). Haste is `TF_COND_RUNE_HASTE`, 91; King is 109.
    /// </remarks>
    [TestCase(91, Red, false, "powerup_icon_haste_red", TestName = "Step_ARedHasteCarrier_CarriesTheRedHasteIcon")]
    [TestCase(109, Blu, false, "powerup_icon_king_blue", TestName = "Step_ABluKing_CarriesTheBlueKingIcon")]
    [TestCase(91, Red, true, "powerup_icon_haste_blue", TestName = "Step_AnEnemySpyDisguisedAsBlu_CarriesTheBlueIcon")]
    public void Step_ARuneCarrier_CarriesItsIcon(int condition, int team, bool disguisedEnemy, string icon)
    {
        OverheadEffects effects = new();

        effects.Step(Plain() with
        {
            Team = team,
            DisguiseTeam = Blu,
            IsEnemy = disguisedEnemy,
            Conditions = disguisedEnemy ? Conditions(condition, PlayerConditions.Disguised) : Conditions(condition),
        });

        effects.Names.ShouldBe([icon]);
    }

    /// <remarks>
    /// **The order is the map's node order, and a freed node is reused first.** Nemesis, then a mark (the think comes after
    /// the data); the nemesis drops, freeing node 0; a rune is added and takes node 0, ahead of the mark. Appended instead,
    /// it would come after.
    /// </remarks>
    [Test]
    public void Names_AFreedNode_IsReusedByTheNextEffect()
    {
        OverheadEffects effects = new();
        OverheadInputs marked = Nemesis() with { Conditions = Conditions(30) };

        effects.Step(marked);
        effects.Names.ShouldBe(["particle_nemesis_red", OverheadEffects.MarkIcon], "the control: data, then the think");

        effects.Step(marked with { NemesisOfLocal = false, Conditions = Conditions(30, 91) });

        effects.Names.ShouldBe(["powerup_icon_haste_red", OverheadEffects.MarkIcon]);
    }

    /// <remarks>
    /// **Entering dormancy removes every icon** (`C_TFPlayer::SetDormant`, `:4308-4330`), **and the think brings back the
    /// rune and the mark** on what he last held: a dormant player still thinks (client_thinklist.cpp:186-220). The nemesis
    /// icon, an `OnDataChanged` one, stays off with no update to bring it. The mark takes the node it last freed.
    /// </remarks>
    [Test]
    public void Dormant_ANemesisMarkedForDeath_KeepsOnlyTheMark()
    {
        OverheadEffects effects = new();
        OverheadInputs marked = Nemesis() with { Conditions = Conditions(30) };
        effects.Step(marked);

        effects.Dormant(marked);

        effects.Names.ShouldBe([OverheadEffects.MarkIcon]);
    }

    /// <remarks>
    /// **Only entering dormancy removes them**, not each tick of it: a second dormant tick leaves the mark in its node, so
    /// a rune that arrives meanwhile goes after it. Removed each tick, the mark would be re-added after the rune.
    /// </remarks>
    [Test]
    public void Dormant_TwoTicks_RemovesTheIconsOnce()
    {
        OverheadEffects effects = new();
        effects.Step(Plain() with { Conditions = Conditions(30) });
        effects.Dormant(Plain() with { Conditions = Conditions(30) });

        effects.Dormant(Plain() with { Conditions = Conditions(30, 91) });

        effects.Names.ShouldBe([OverheadEffects.MarkIcon, "powerup_icon_haste_red"]);
    }

    /// <remarks>**A list that did not change is the same list**, so a frame that shares its player's detail can.</remarks>
    [Test]
    public void Names_UnchangedAcrossSteps_IsTheSameInstance()
    {
        OverheadEffects effects = new();
        effects.Step(Nemesis());
        System.Collections.Generic.IReadOnlyList<string> first = effects.Names;

        effects.Step(Nemesis());

        effects.Names.ShouldBeSameAs(first);
    }

    /// <remarks>
    /// **Every icon this can carry fits a particle key's five bits** (`BurstEffects.OverheadIconKey`): IT, two nemesis
    /// icons, the mark and twelve runes in two colours, 28, each once.
    /// </remarks>
    [Test]
    public void Icons_EveryOneThisCanCarry_AreTwentyEightDistinctNames()
    {
        OverheadEffects.Icons.Distinct().Count().ShouldBe(28);
    }

    /// <remarks>
    /// **The nemesis bit is the other player's entity index** (`IsPlayerDominatingMe( m_pOuter-&gt;entindex() )`,
    /// tf_player_shared.cpp:7752): bit 5 set names entity 5, not 4 or 6, and nothing when no bits arrived.
    /// </remarks>
    [TestCase(5, true, TestName = "Dominates_TheEntityWhoseBitIsSet_IsTheNemesis")]
    [TestCase(4, false, TestName = "Dominates_TheEntityBelow_IsNot")]
    [TestCase(6, false, TestName = "Dominates_TheEntityAbove_IsNot")]
    public void Dominates_ReadsTheBitAtTheEntityIndex(int entity, bool nemesis)
    {
        OverheadEffects.Dominates(System.UInt128.One << 5, entity).ShouldBe(nemesis);
        OverheadEffects.Dominates(null, entity).ShouldBeFalse();
    }

    /// <summary>A RED player who dominates the recorder, with nothing refusing his icon.</summary>
    private static OverheadInputs Nemesis() => Plain() with { NemesisOfLocal = true };

    /// <summary>A living, connected RED player in a POV demo, nobody's nemesis.</summary>
    private static OverheadInputs Plain() => new()
    {
        Team = Red,
        Alive = true,
        Connected = true,
    };

    private static PlayerConditions Conditions(params int[] conditions)
    {
        uint[] words = new uint[5];

        foreach (int condition in conditions)
        {
            words[condition / 32] |= 1u << (condition % 32);
        }

        return new PlayerConditions((int)words[0], (int)words[1], (int)words[2], (int)words[3], (int)words[4]);
    }
}
