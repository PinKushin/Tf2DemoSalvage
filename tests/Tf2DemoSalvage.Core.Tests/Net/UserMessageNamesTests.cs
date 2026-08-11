using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests the per-era user message name tables against the registration order in the binaries.
/// </summary>
/// <remarks>
/// A user message carries no name on the wire — only an id, which is the game's registration
/// order. On 2026-08-11 that order was read out of six shipped clients spanning 2007 to July
/// 2026, so these are exact predictions rather than inferences: every value below was observed
/// in a `usermessages->Register` call sequence.
///
/// Two structural facts drive the whole file. **The table grows**, so its length identifies an
/// era — 29 entries in 2007 and 2008, 41 in 2009, 49 in 2011, 79 today. And **the table is not
/// the whole id space**: a second block of six Novint Falcon haptics messages is registered
/// after it in every build from 2009 on, which is where the corpus's long-unnamed ids came from.
/// </remarks>
public sealed class UserMessageNamesTests
{
    private const int Launch = 11;
    private const int Y2008 = 14;
    private const int Y2009 = 15;
    private const int Y2011 = 16;
    private const int Modern = 24;

    [Fact]
    public void TheStableHeadIsNamedAtEveryProtocol()
    {
        // Confirmed at protocols 11, 14, 15, 16 and 24 with matching body widths, and then
        // confirmed a second time against all six binaries, which agree entry for entry.
        UserMessageNames.Lookup(0, Y2009).ShouldBe("Geiger");
        UserMessageNames.Lookup(13, Y2009).ShouldBe("Rumble");
        UserMessageNames.Lookup(18, Y2009).ShouldBe("Damage");
        UserMessageNames.Lookup(28, Y2009).ShouldBe("PlayerStatsUpdate");
    }

    [Fact]
    public void TheLaunchTableEndsAtPlayerStatsUpdate()
    {
        // The 2007 server and client both register exactly 29 messages, ending here. That the
        // number matches the previously-derived "last stable id" is a genuine agreement: one
        // came from histogramming demo bodies, the other from Valve's code.
        UserMessageNames.Lookup(28, Launch).ShouldBe("PlayerStatsUpdate");
        UserMessageNames.Lookup(29, Launch).ShouldBeNull();
        UserMessageNames.Lookup(29, Y2008).ShouldBeNull();

        // No haptics block before 2009, so nothing lives above the table at all. This is the
        // control for the haptics assertions below: they must not fire in an era without it.
        UserMessageNames.Lookup(32, Launch).ShouldBeNull();
    }

    [Fact]
    public void TheTailIsNamedFromTheEraThatRecordedIt()
    {
        // The four ids that were unnamed until the binaries were read. CheapBreakModel moves
        // because messages were inserted before it; its 85-bit body identified it long before
        // its id could be confirmed.
        UserMessageNames.Lookup(40, Y2009).ShouldBe("CheapBreakModel");
        UserMessageNames.Lookup(41, Y2011).ShouldBe("CheapBreakModel");
        UserMessageNames.Lookup(42, Modern).ShouldBe("CheapBreakModel");
    }

    [Fact]
    public void TheHapticsBlockFollowsTheGameTable()
    {
        // The finding that resolved three ids at once. Each sits exactly four past the end of
        // its own era's table - 40, 48, 78 - because HapSetDrag is the fourth of six haptics
        // messages registered after the game's list. One float of drag, hence the 32-bit bodies
        // measured on all three.
        UserMessageNames.Lookup(41, Y2009).ShouldBe("SPHapWeapEvent");
        UserMessageNames.Lookup(44, Y2009).ShouldBe("HapSetDrag");
        UserMessageNames.Lookup(52, Y2011).ShouldBe("HapSetDrag");
        UserMessageNames.Lookup(82, Modern).ShouldBe("HapSetDrag");

        // The last of the block, and the end of the id space in each era.
        UserMessageNames.Lookup(46, Y2009).ShouldBe("HapMeleeContact");
        UserMessageNames.Lookup(47, Y2009).ShouldBeNull();
    }

    [Fact]
    public void EachEraOmitsWhatItsBuildHadNotShippedYet()
    {
        // MapStatsUpdate is absent from the 2009 and 2011 clients, so id 29 is PlayerIgnited in
        // both and MapStatsUpdate in the modern table. This is the mechanism behind every shift
        // in the file: an insertion, not an append.
        UserMessageNames.Lookup(29, Y2009).ShouldBe("PlayerIgnited");
        UserMessageNames.Lookup(29, Y2011).ShouldBe("PlayerIgnited");
        UserMessageNames.Lookup(29, Modern).ShouldBe("MapStatsUpdate");

        // TrainingObjective exists in 2011 but not 2009; BreakModelRocketDud in neither.
        UserMessageNames.Lookup(34, Y2011).ShouldBe("TrainingObjective");
        UserMessageNames.Lookup(34, Y2009).ShouldBe("DamageDodged");
    }

    [Fact]
    public void TheEraTablesEndWhereTheirBuildsDo()
    {
        // Lengths are the era fingerprint: 29 / 41 / 49 / 79 game messages, each plus six
        // haptics from 2009 on. An off-by-one anywhere in the derivation moves one of these.
        UserMessageNames.Lookup(40, Y2009).ShouldNotBeNull();
        UserMessageNames.Lookup(48, Y2011).ShouldBe("PlayerBonusPoints");
        UserMessageNames.Lookup(78, Modern).ShouldBe("BuiltObject");
        UserMessageNames.Lookup(51, Modern).ShouldBe("RDTeamPointsChanged");

        UserMessageNames.Lookup(55, Y2011).ShouldBeNull();
        UserMessageNames.Lookup(85, Modern).ShouldBeNull();
    }

    [Fact]
    public void AnUnspecimenedProtocolIsNamedOnlyWhereEveryEraAgrees()
    {
        // Protocols 17-23 have no surviving client and no demo, so the only defensible table is
        // the head every measured era shares. Naming id 40 here would be a guess between two
        // tables that disagree about it.
        UserMessageNames.Lookup(28, 20).ShouldBe("PlayerStatsUpdate");
        UserMessageNames.Lookup(40, 20).ShouldBeNull();
    }

    [Fact]
    public void ProtocolTwentyFourOffersTheMarch2013NameAsAnAlternate()
    {
        // Protocol 24 spans thirteen years and two incompatible tables, so an id above 50 has two
        // candidate meanings. RDTeamPointsChanged was inserted at 51 after March 2013 and shifts
        // everything above it, which is the entire difference between them.
        UserMessageNames.Alternate(69, Modern).ShouldBe("HapSetDrag");
        UserMessageNames.Alternate(51, Modern).ShouldBe("SpawnFlyingBird");

        // Below the insertion the tables agree, so there is no alternate to offer - offering one
        // would invite a fallback that could only ever produce the same answer.
        UserMessageNames.Alternate(50, Modern).ShouldBeNull();
        UserMessageNames.Alternate(28, Modern).ShouldBeNull();

        // And no other protocol is ambiguous: 11-16 are each one measured build.
        UserMessageNames.Alternate(69, Y2011).ShouldBeNull();
        UserMessageNames.Alternate(44, Y2009).ShouldBeNull();
    }

    [Fact]
    public void AnIdPastTheTable_IsUnnamedAtEveryProtocol()
    {
        UserMessageNames.Lookup(500, Modern).ShouldBeNull();
        UserMessageNames.Lookup(-1, Modern).ShouldBeNull();
    }
}
