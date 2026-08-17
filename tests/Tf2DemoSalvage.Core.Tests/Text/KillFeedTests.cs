using System.Collections.Generic;

using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// One death, rendered the way a kill feed reads.
/// </summary>
/// <remarks>
/// **The summary already showed deaths; it showed ONE.** `player_death` fired 407 times in the
/// modern corpus demo and the sample cap printed the first. For a review tool the whole list is the
/// point — "what happened in this match" is a sequence of kills, and one of them is not a sequence.
///
/// The shape follows the game's own feed: **attacker [weapon] victim**, with the qualifiers that
/// change how it reads in parentheses after. Everything here comes from fields already decoded.
/// </remarks>
public sealed class KillFeedTests
{
    /// <summary>Builds a death event's fields, in the order the game sends them.</summary>
    private static List<KeyValuePair<string, object?>> Death(
        string attacker, string victim, string weapon, int customKill = 0, string assister = "") =>
    [
        new("userid", victim),
        new("attacker", attacker),
        new("weapon", weapon),
        new("customkill", (byte)customKill),
        new("assister", assister),
    ];

    [Test]
    public void AnOrdinaryKillReadsAsAttackerWeaponVictim()
    {
        KillFeed.Line(Death("scout", "medic", "scattergun"))
            .ShouldBe("scout [scattergun] medic");
    }

    [Test]
    public void AQualifiedKillNamesTheQualifier()
    {
        // customkill 1 is TF_DMG_CUSTOM_HEADSHOT, held against the SDK by
        // KillDescriptionConformanceTests.
        KillFeed.Line(Death("sniper", "heavy", "sniperrifle", customKill: 1))
            .ShouldBe("sniper [sniperrifle] heavy (headshot)");
    }

    [Test]
    public void AnAssistedKillNamesTheAssister()
    {
        KillFeed.Line(Death("soldier", "spy", "rocketlauncher", assister: "medic"))
            .ShouldBe("soldier [rocketlauncher] spy (assist medic)");
    }

    [Test]
    public void AQualifierAndAnAssistAreBothReported()
    {
        // The combining case. A kill feed that shows one and drops the other is wrong in a way
        // nobody notices, because each line still looks complete.
        KillFeed.Line(Death("spy", "engineer", "knife", customKill: 2, assister: "scout"))
            .ShouldBe("spy [knife] engineer (backstab, assist scout)");
    }

    [Test]
    public void ASuicideHasNoAttackerToName()
    {
        // The victim kills themselves, so attacker and userid are the same person. Rendered as the
        // game does — the victim alone — rather than "medic [world] medic", which reads as someone
        // else's kill.
        KillFeed.Line(Death("medic", "medic", "world")).ShouldBe("medic [world] (suicide)");
    }

    [Test]
    public void AnAssisterOfMinusOneIsNobody()
    {
        // **Found by reading the output, not by reasoning about the format.** 407 real kills
        // rendered, and a great many read "(assist -1)".
        //
        // -1 is the absent sentinel for a user id. The field is always present on the event, so
        // "has an assister" cannot be answered by presence — it has to be answered by value, and
        // the value that means nobody is not zero.
        //
        // Exactly the shape in memory as `sentinels-conflate-unknown-with-answer`: a number that
        // means "no answer" sitting in the same field as real answers, and reading as one.
        KillFeed.Line(Death("scout", "medic", "scattergun", assister: "-1"))
            .ShouldBe("scout [scattergun] medic");
    }

    [Test]
    public void AnAttackerOfZeroIsTheWorld()
    {
        // **The last unresolved line in 407 real kills**, and it was not a roster failure:
        //
        //   0 [worldspawn] Dojyaaaaaaan(699)
        //
        // User id 0 is not a player. TF2 assigns ids from 1 upward per connection — this demo's run
        // from 698 to 733 — and an attacker of 0 with weapon "worldspawn" is the map killing
        // someone: fall damage, a trigger, a pit.
        //
        // Rendered as a death rather than as a kill by a player named "0". The distinction matters
        // for a reviewer counting someone's kills.
        KillFeed.Line(Death("0", "demoman", "worldspawn"))
            .ShouldBe("demoman died [worldspawn]");
    }

    [Test]
    public void AMissingAttackerIsNotInvented()
    {
        // A death from the world - fall damage, a trigger - carries no attacker. Reported as the
        // victim dying rather than attributed to anyone, because attributing it would be a claim
        // the demo does not make.
        List<KeyValuePair<string, object?>> fields =
        [
            new("userid", "demoman"),
            new("weapon", "world"),
        ];

        KillFeed.Line(fields).ShouldBe("demoman died [world]");
    }
}
