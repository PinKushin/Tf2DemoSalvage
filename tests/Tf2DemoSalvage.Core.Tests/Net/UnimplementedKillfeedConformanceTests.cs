using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// What a death actually carries, which is most of what a review tool would want to show.
/// </summary>
/// <remarks>
/// **Twelfth batch, and the cheapest user-visible work identified so far.** The entity batch already
/// records that game events are decoded here and never presented. This says what is being thrown
/// away in the one event that matters most.
///
/// <c>player_death</c> is not attacker-and-victim. <c>tf_hud_deathnotice.cpp</c> reads a dozen fields
/// off it: the assister, a fallback name for when the assister is not a player, the weapon, a custom
/// kill type, a flag word, the damage bits, and how many players a shot passed through. **A kill
/// feed built from attacker and victim alone is not a simplified kill feed — it is a different,
/// much less informative event.**
///
/// None of this needs rendering. A text trace could carry all of it today.
/// </remarks>
public sealed class UnimplementedKillfeedConformanceTests
{
    /// <summary>Where TF2's death constants are declared.</summary>
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    /// <summary>The client that consumes the event.</summary>
    private const string DeathNotice = "src/game/client/tf/tf_hud_deathnotice.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Killfeed_EveryDeathFlag_IsADistinctSingleBit()
    {
        // TF_DEATH_DOMINATION through TF_DEATH_AUSTRALIUM: eleven flags, 0x0001 to 0x0400, packed
        // into the event's "death_flags" integer.
        //
        // **A trap sits in the middle of them.** TF_DEATH_ANIMATION_TIME is declared as 2.0 with the
        // same prefix — a duration in seconds, not a flag. Anything that collects constants by prefix
        // and treats the result as a bit set picks up a value that is neither a power of two nor an
        // integer. A prefix is a naming convention, not a category, and this is the second time that
        // has mattered here after the "Econ" substring count.
        //
        // Derived rather than transcribed: each value must be a single bit, and no two may share one.
        // That fails if Valve adds a twelfth flag with a duplicate mask, which is the mistake this
        // shape of constant actually suffers from.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        List<KeyValuePair<string, int>> flags =
        [
            .. defs.Where(entry =>
                entry.Key.StartsWith("TF_DEATH_", System.StringComparison.Ordinal) &&
                entry.Key != "TF_DEATH_ANIMATION_TIME"),
        ];

        flags.Count.ShouldBe(11);

        int union = 0;

        foreach ((string name, int value) in flags)
        {
            // Exactly one bit set.
            (value & (value - 1)).ShouldBe(0, $"{name} is not a single bit");

            // Not already claimed by another flag.
            (union & value).ShouldBe(0, $"{name} reuses a bit");

            union |= value;
        }
    }

    [Test]
    public void Killfeed_ADeath_CarriesMoreThanAttackerAndVictim()
    {
        // The fields tf_hud_deathnotice.cpp reads off player_death. Pinned as a set because the
        // finding is the BREADTH: any one of them alone is unremarkable, and together they are the
        // difference between "X killed Y" and a kill feed someone would actually use to review a
        // match.
        //
        // playerpenetratecount is the one worth pointing at — a Sniper shot through two teammates is
        // recorded, and nothing else in the stream says so.
        string notice = SourceSdk.Text(DeathNotice).ShouldNotBeNull();

        foreach (string field in new[]
        {
            "userid", "attacker", "assister", "weapon", "customkill",
            "death_flags", "damagebits", "playerpenetratecount",
        })
        {
            notice.ShouldContain($"\"{field}\"");
        }

        Assert.Ignore(
            "player_death is decoded and nothing reads its detail. Eight fields the game's own kill " +
            "feed uses — including customkill, death_flags and playerpenetratecount — are present " +
            "and discarded. No rendering needed: a text trace could carry all of it.");
    }

    [Test]
    public void Killfeed_TheAssister_MayNotBeAPlayer()
    {
        // "assister_fallback" — a STRING beside the numeric assister id, used when the thing that
        // helped was not a player: a sentry gun, or an object with a name rather than a user id.
        //
        // **A parser that models the assister as a player id silently drops those assists**, because
        // the numeric field is empty and looks like "no assister". The information is in a
        // differently-typed field with a different name, which is exactly the shape that goes
        // unnoticed — nothing is malformed, an assist simply does not appear.
        string notice = SourceSdk.Text(DeathNotice).ShouldNotBeNull();

        notice.ShouldContain("assister_fallback");

        Assert.Ignore(
            "assister_fallback is not read. When the assist came from an object rather than a " +
            "player the numeric assister is empty and the name is in a separate string field — so " +
            "modelling the assister as a player id drops those assists without any sign of it.");
    }

    [Test]
    public void Killfeed_CustomKill_SaysHowNotWithWhat()
    {
        // TF_DMG_CUSTOM_NONE is 0, then HEADSHOT, BACKSTAB, BURNING, and a long tail.
        //
        // **Separate from the weapon, and not derivable from it.** A Sniper rifle kill is a headshot
        // or it is not; an Ambassador is the same weapon either way. The weapon name answers "with
        // what", this answers "how", and a kill feed showing only the weapon cannot distinguish the
        // two most interesting kinds of kill in the game.
        //
        // Pinned at the head of the enumeration because the tail changes with every update, while
        // NONE-HEADSHOT-BACKSTAB has been stable and is the part any implementation needs first.
        IReadOnlyDictionary<string, int> custom =
            SourceSdk.Enumerators(SharedDefs, "ETFDmgCustom");

        custom["TF_DMG_CUSTOM_NONE"].ShouldBe(0);
        custom["TF_DMG_CUSTOM_HEADSHOT"].ShouldBe(1);
        custom["TF_DMG_CUSTOM_BACKSTAB"].ShouldBe(2);

        Assert.Ignore(
            "customkill is not read, so a headshot and an ordinary rifle shot are indistinguishable " +
            "here. It says HOW rather than with what, and is not derivable from the weapon name.");
    }
}
