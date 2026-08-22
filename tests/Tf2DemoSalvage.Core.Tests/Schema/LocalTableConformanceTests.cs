using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Fields the engine sends only to the player they belong to, and what that costs a demo.
/// </summary>
/// <remarks>
/// **Fifteenth batch, and it is one finding rather than four.** TF2 splits a player's state across
/// network tables by AUDIENCE: a "local" table sent only to the player it describes, and a shared
/// table sent to everyone else. The engine does it to save bandwidth, and the consequence for this
/// project is that **a POV demo and a SourceTV demo of the same moment do not contain the same
/// data.**
///
/// Not a subtlety — it is structural, and it appears at least three times:
///
/// | Field | Local table | Shared table |
/// |---|---|---|
/// | übercharge | full precision | 12 bits over 0..100 |
/// | disguise | <c>m_nDesiredDisguise*</c> | <c>m_nDisguise*</c> |
/// | stealth timers | present | absent |
///
/// **This sharpens the existing rule about recording both points of view.** That was written after a
/// POV/STV pair proved a 64 KiB schema cap belonged to the writer rather than the parser — a pair as
/// a control. This says the pair is also a pair of *different datasets*, so "the STV demo does not
/// have it" can mean the field was never sent rather than that decoding failed.
///
/// The practical rule: **before concluding a field is missing, establish which table it lives in and
/// whose recording this is.** Absent from an STV demo is the documented behaviour for anything local.
/// </remarks>
public sealed class LocalTableConformanceTests
{
    /// <summary>Where the player's networked tables are declared.</summary>
    private const string PlayerShared = "src/game/shared/tf/tf_player_shared.cpp";

    /// <summary>Where TF2's flag constants live.</summary>
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void LocalTables_DisguiseIntentAndState_AreLocalAndSharedRespectively()
    {
        // tf_player_shared.cpp:334 opens DT_TFPlayerSharedLocal, holding m_nDesiredDisguiseTeam and
        // m_nDesiredDisguiseClass. The shared table at 400-401 holds m_nDisguiseTeam and
        // m_nDisguiseClass.
        //
        // **Desired and actual are different fields in different tables**, and the difference is
        // real: a Spy part-way through disguising has a desired disguise and not yet an actual one.
        // Only the Spy's own client is told what they are aiming at.
        //
        // For a review tool: an STV demo can show what a Spy WAS disguised as, and cannot show what
        // they were switching to. A POV demo by that Spy can show both. Neither is a decode failure.
        string player = SourceSdk.Text(PlayerShared).ShouldNotBeNull();

        player.ShouldContain("BEGIN_RECV_TABLE_NOBASE( CTFPlayerShared, DT_TFPlayerSharedLocal )");
        player.ShouldContain("RecvPropInt( RECVINFO( m_nDesiredDisguiseClass ) )");
        player.ShouldContain("RecvPropInt( RECVINFO( m_nDisguiseClass ) )");

        Assert.Ignore(
            "disguise is not decoded, and it is split by audience: intent " +
            "(m_nDesiredDisguise*) is in the local table and only reaches the Spy's own client, " +
            "while the actual disguise is shared. An STV demo legitimately lacks the first.");
    }

    [Test]
    public void LocalTables_StealthTimers_AreLocalSoAnStvDemoLacksThem()
    {
        // m_flStealthNoAttackExpire and m_flStealthNextChangeTime, both in DT_TFPlayerSharedLocal.
        //
        // **These are the cloak's rules, and they are invisible to everyone but the Spy.** How long
        // until they can attack after decloaking, and when they may next change state. An observer
        // is told the Spy is cloaked and nothing about the timing.
        //
        // Worth pinning specifically because "the field is not in this demo" is the kind of
        // observation that gets attributed to a decoder bug. It is the documented design.
        string player = SourceSdk.Text(PlayerShared).ShouldNotBeNull();

        player.ShouldContain("RecvPropTime( RECVINFO( m_flStealthNoAttackExpire ) )");
        player.ShouldContain("RecvPropTime( RECVINFO( m_flStealthNextChangeTime ) )");

        Assert.Ignore(
            "cloak timing is not decoded and is local-only, so it is absent from any STV recording " +
            "by design — not a decode failure.");
    }

    [Test]
    public void LocalTables_FlagStatus_IsABitFieldWhoseHomeStateIsZero()
    {
        // tf_shareddefs.h:874 onward:
        //
        //   #define TF_FLAGINFO_HOME     0
        //   #define TF_FLAGINFO_STOLEN   (1<<0)
        //   #define TF_FLAGINFO_DROPPED  (1<<1)
        //
        // **HOME is zero because it means "no bits set", not because it is the first of three
        // values.** The three look exactly like an enumeration — 0, 1, 2 — and a decoder that treats
        // them as one will be right for every state except the combinations, and will have no way to
        // represent a flag that is both.
        //
        // Derived rather than transcribed: STOLEN and DROPPED must be distinct single bits, and HOME
        // must be their absence. That is the property a bit field has and an enumeration does not.
        System.Collections.Generic.IReadOnlyDictionary<string, int> defs =
            SourceSdk.Constants(SharedDefs);

        int home = defs["TF_FLAGINFO_HOME"];
        int stolen = defs["TF_FLAGINFO_STOLEN"];
        int dropped = defs["TF_FLAGINFO_DROPPED"];

        home.ShouldBe(0);
        (stolen & (stolen - 1)).ShouldBe(0, "STOLEN is not a single bit");
        (dropped & (dropped - 1)).ShouldBe(0, "DROPPED is not a single bit");
        (stolen & dropped).ShouldBe(0, "STOLEN and DROPPED share a bit");

        Assert.Ignore(
            "flag status is not decoded. It is a BIT FIELD whose home state is the absence of bits " +
            "(tf_shareddefs.h:874) — reading 0/1/2 as an enumeration cannot represent a flag that " +
            "is both stolen and dropped.");
    }

    [Test]
    public void LocalTables_TheFlagType_DistinguishesGameModesSharingOneEntity()
    {
        // TF_FLAGTYPE_CTF = 0, then ATTACK_DEFEND, TERRITORY_CONTROL, INVADE, RESOURCE_CONTROL,
        // ROBOT_DESTRUCTION, PLAYER_DESTRUCTION — one entity class serving seven game modes.
        //
        // **The same entity means different things depending on m_nType**, so a viewer that draws
        // "the intelligence" draws the wrong thing in six of the seven modes. Robot destruction and
        // player destruction in particular are not briefcases at all.
        //
        // Pinned at the head and by count: the tail grows with each mode Valve adds, and a count
        // that changes is a signal to look rather than a failure to suppress.
        System.Collections.Generic.IReadOnlyDictionary<string, int> types =
            SourceSdk.Enumerators(SharedDefs, "ETFFlagType");

        types["TF_FLAGTYPE_CTF"].ShouldBe(0);
        types.Count.ShouldBeGreaterThanOrEqualTo(7);

        // The gap, with its control, so this marker fails when the field is read (D45).
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a name that is demonstrably compiled in");

        SchemaGap.AnyProductionAssemblyMentions("m_nType").ShouldBeFalse(
            "the flag's type field is now read — replace this marker with a parity test against " +
            "ETFFlagType above, and check the drawing distinguishes the seven modes");

        Assert.Ignore(
            "the flag type is not read. One entity class serves seven game modes " +
            "(tf_shareddefs.h:258), so drawing it as 'the intelligence' is wrong in six of them.");
    }
}
