using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Player condition and class state, now that TF2's own definitions turn out to be citable.
/// </summary>
/// <remarks>
/// **Tenth batch, and it only exists because of the ninth's correction.** Player conditions were
/// previously unspecifiable here on the belief that TF2's game code is closed. It is not, and
/// <c>tf_shareddefs.h</c> enumerates every condition with its value.
///
/// **A condition is the single most informative thing about a player that this project discards.**
/// Übercharged, cloaked, disguised, burning, taunting, crit-boosted, marked for death — all of it is
/// in the demo, on the player entity, as a bit set. A review tool that shows movement and not
/// conditions is showing the least interesting half of what happened.
///
/// The trap is in how they are transmitted, and it is the kind that produces a partial answer rather
/// than an error. See <see cref="PlayerState_Conditions_SpanFiveSeparatelyNamedWords"/>.
/// </remarks>
public sealed class UnimplementedPlayerStateConformanceTests
{
    /// <summary>Where TF2's shared enumerations live.</summary>
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    /// <summary>Where the player's networked table is declared.</summary>
    private const string PlayerShared = "src/game/shared/tf/tf_player_shared.cpp";

    /// <summary>Bits in each networked condition word.</summary>
    private const int BitsPerWord = 32;

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void PlayerState_Conditions_SpanFiveSeparatelyNamedWords()
    {
        // tf_player_shared.cpp:359,419,424,425,438 — RecvPropInt for m_nPlayerCond, then
        // m_nPlayerCondEx, Ex2, Ex3 and Ex4. Five 32-bit integers, 160 bits, holding a bit set whose
        // highest member is TF_COND_LAST.
        //
        // **The failure mode is a partial answer that looks complete.** A parser that finds
        // m_nPlayerCond — the obvious name, the one without a suffix — reads conditions 0 to 31 and
        // silently misses every one above. Those are not obscure: the low word predates most of TF2's
        // history, so a modern demo puts a great deal in Ex through Ex4.
        //
        // Nothing on the wire says these five belong together. They are five ordinary integer
        // properties with unrelated-looking names, declared hundreds of lines apart in the same
        // table, and only the enumeration's size reveals that one word cannot be enough.
        string shared = SourceSdk.Text(PlayerShared).ShouldNotBeNull();

        foreach (string word in new[]
        {
            "m_nPlayerCond", "m_nPlayerCondEx", "m_nPlayerCondEx2",
            "m_nPlayerCondEx3", "m_nPlayerCondEx4",
        })
        {
            shared.ShouldContain($"RecvPropInt( RECVINFO( {word} ) )");
        }

        // The arithmetic that makes five words necessary, derived rather than asserted: the highest
        // condition has to fit, and four words would not hold it. Checking both directions means the
        // test notices Valve adding a sixth word as well as the count being wrong today.
        IReadOnlyDictionary<string, int> conditions =
            SourceSdk.Enumerators(SharedDefs, "ETFCond");

        int last = conditions["TF_COND_LAST"];

        last.ShouldBeGreaterThan(4 * BitsPerWord);
        last.ShouldBeLessThanOrEqualTo(5 * BitsPerWord);

        Assert.Ignore(
            $"player conditions are not decoded. {last} of them, spread across five networked words " +
            "(m_nPlayerCond and Ex through Ex4) — reading only the unsuffixed one gives conditions " +
            "0 to 31 and silently drops the rest, which is most of the modern ones.");
    }

    [Test]
    public void PlayerState_TheClassIndexOrder_IsNotTheOrderPlayersSee()
    {
        // tf_shareddefs.h:205-221. The networked order is SCOUT, SNIPER, SOLDIER, DEMOMAN, MEDIC,
        // HEAVYWEAPONS, PYRO, SPY, ENGINEER — which is not the class-selection order players know,
        // and not alphabetical, and not the order the HUD or the scoreboard uses.
        //
        // **A wrong assumption here mislabels every player rather than failing.** Rendering a
        // scoreboard from a remembered order puts Sniper where Soldier belongs; nothing about the
        // output looks broken, it is just consistently wrong about who was playing what.
        //
        // Pinned at the two indices where the difference first bites, derived from the enumeration
        // rather than typed.
        IReadOnlyDictionary<string, int> classes = SourceSdk.Enumerators(SharedDefs, "ETFClass");

        classes["TF_CLASS_SCOUT"].ShouldBe(1);
        classes["TF_CLASS_SNIPER"].ShouldBe(2);
        classes["TF_CLASS_SOLDIER"].ShouldBe(3);

        // TF_FIRST_NORMAL_CLASS is defined in terms of TF_CLASS_UNDEFINED rather than as a literal,
        // which is exactly the alias-and-arithmetic shape the SDK reader was extended to follow.
        classes["TF_CLASS_UNDEFINED"].ShouldBe(0);
    }

    [Test]
    public void PlayerState_Civilian_IsAPlayableIndexNoOneCanPlay()
    {
        // tf_shareddefs.h:218 — TF_CLASS_CIVILIAN, marked in a comment as TF_LAST_NORMAL_CLASS, so
        // it sits INSIDE the normal class range rather than after it.
        //
        // **This is a vestigial slot, and the kind of thing worth recording about Valve's code
        // rather than about the wire.** Civilian is a Team Fortress Classic class that TF2 never
        // shipped, and the index survives because removing it would renumber everything after it.
        //
        // The practical consequence is small and sharp: any table sized by "number of playable
        // classes" is one too long, and any loop over the normal range hits a class with no player
        // model. Worth knowing before writing either.
        IReadOnlyDictionary<string, int> classes = SourceSdk.Enumerators(SharedDefs, "ETFClass");

        classes.ShouldContainKey("TF_CLASS_CIVILIAN");
        classes["TF_CLASS_CIVILIAN"].ShouldBeLessThan(classes["TF_CLASS_COUNT_ALL"]);
    }

    [Test]
    public void PlayerState_WhoAppliedACondition_IsNetworkedSeparately()
    {
        // tf_player_shared.cpp:436 — RecvPropUtlVectorDataTable( m_ConditionData, TF_COND_LAST,
        // DT_TFPlayerConditionSource ). A second, parallel structure: one entry per condition,
        // carrying its source.
        //
        // **The bits say a player is übercharged; this says which Medic did it.** For a review tool
        // that distinction is most of the value — "who was being healed by whom" is not recoverable
        // from the condition bits alone, and it is sent.
        //
        // Recorded separately from the bit set because implementing one gains nothing from the
        // other: the bits are five integers, this is a networked vector of sub-tables.
        string shared = SourceSdk.Text(PlayerShared).ShouldNotBeNull();

        shared.ShouldContain("RecvPropUtlVectorDataTable( m_ConditionData, TF_COND_LAST");

        Assert.Ignore(
            "condition SOURCES are not decoded. m_ConditionData carries one entry per condition " +
            "naming who applied it (tf_player_shared.cpp:436) — which Medic is übering whom is sent " +
            "and is not recoverable from the condition bits.");
    }
}
