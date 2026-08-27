using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// How a capture point changes appearance when it is captured — answered from Valve's source.
/// </summary>
/// <remarks>
/// **B83 has cost five hypotheses about this one surface, every one proposed from a screenshot and
/// every one killed by the owner looking at the game.** Ambient cube, envmap on a prop, indoor
/// shadowing, sun asymmetry, and a skin-selection defect that turned out not to exist because the
/// rings are on every point.
///
/// **This is the same question answered by reading instead.** TF2's game code is in the SDK, which
/// the ninth batch established after this project had recorded three times that it was not. So what
/// a capture point does on capture is not a matter for inference at all —
/// <c>team_control_point.cpp:569</c> states it in three lines:
///
/// <code>
/// SetModel( STRING(m_TeamData[m_iTeam].iszModel) );
/// SetBodygroup( 0, m_iTeam );
/// m_nSkin = ( m_iTeam == TEAM_UNASSIGNED ) ? 2 : (m_iTeam - 2);
/// </code>
///
/// **Three mechanisms at once, not one.** The model itself can be swapped per team, bodygroup 0 is
/// set to the raw team number, and the skin is a small remap of it. Any implementation that follows
/// one and not the others is right for some points and wrong for others — which is exactly the
/// pattern of "some points look wrong" that started B83.
///
/// The lesson is the one now in memory as `tf2-game-code-is-in-the-sdk`: a month of appearance
/// theories, and the answer was a published file. **Read the source before measuring the picture.**
///
/// **B83's open half was closed on 2026-08-16, and the answer was no.** This class used to carry a
/// third, skipping test asking whether a capture point's networked <c>m_nSkin</c> reached its draw
/// call. It did not — and not because the renderer ignored it. <c>EntityState.NetworkedProperties</c>
/// is a whitelist and did not retain the property, so it was discarded before the pose was built.
/// **Every entity in every demo ever parsed here drew with skin 0.**
///
/// The path is now whitelist → <c>EntityState.Skin()</c> → <c>ScenePose.Skin</c> → the renderer,
/// held at two levels because either fix alone still yields zeros — the field was missing from the
/// filter AND unread at the construction site:
///
/// - <c>RetainedPropertyTests</c> (Core.Tests) locks the whitelist as a whole list, so a future
///   field cannot go missing the same way.
/// - <c>CorpusEntitySkinTests</c> (Corpus.Tests) proves a real demo's distinct skins survive into
///   the poses, verified by manipulation: with the pose line reverted it reports exactly one
///   distinct skin, which is the original defect.
///
/// Measured — cp_foundry carries skins 0, 1 and 2 in a 38/5/2 split and z1800 in 42/15/1, exactly
/// the three values <c>team_control_point.cpp:569</c> produces.
///
/// The skipping test is gone rather than kept green: a conformance entry that has been settled
/// should stop counting as a gap. Its assertions live in the projects that can see the types.
/// </remarks>
public sealed class ControlPointAppearanceConformanceTests
{
    /// <summary>Where the capture point's ownership change is implemented.</summary>
    private const string ControlPoint = "src/game/server/team_control_point.cpp";

    /// <summary>Where TF2's team numbering is declared.</summary>
    private const string TfDefs = "src/game/shared/tf/tf_shareddefs.h";

    /// <summary>Where the shared team numbering is declared.</summary>
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void ControlPoint_Skin_IsTheTeamNumberOffset()
    {
        // Derived end to end rather than asserted as 0/1/2, because every number in the formula is
        // published: TEAM_UNASSIGNED is 0, LAST_SHARED_TEAM is TEAM_SPECTATOR which is 1, and
        // TF_TEAM_RED is LAST_SHARED_TEAM + 1. So RED is 2, BLU is 3, and the skin is team - 2.
        //
        // Working it out this way rather than reading three literals off the model is the point: the
        // model has three skin families and nothing in the model says which is which. The mapping is
        // in the game code, and it is arithmetic on the team enumeration.
        IReadOnlyDictionary<string, int> shared = SourceSdk.Constants(SharedDefs);

        int unassigned = shared["TEAM_UNASSIGNED"];

        // LAST_SHARED_TEAM is `#define LAST_SHARED_TEAM TEAM_SPECTATOR` — a plain alias, resolved by
        // the reader's alias pass. TF2's own teams are then declared in terms of it rather than as
        // numbers, which is why they cannot simply be read as enumerators.
        shared["LAST_SHARED_TEAM"].ShouldBe(shared["TEAM_SPECTATOR"]);

        string tfDefs = SourceSdk.Text(TfDefs).ShouldNotBeNull();

        tfDefs.ShouldContain("TF_TEAM_RED = LAST_SHARED_TEAM+1");

        int red = shared["LAST_SHARED_TEAM"] + 1;
        int blue = red + 1;

        red.ShouldBe(2);

        // The formula from team_control_point.cpp:569, applied to the values just derived.
        static int Skin(int team, int unassignedTeam, int redTeam) =>
            team == unassignedTeam ? 2 : team - redTeam;

        Skin(red, unassigned, red).ShouldBe(0);
        Skin(blue, unassigned, red).ShouldBe(1);
        Skin(unassigned, unassigned, red).ShouldBe(2);
    }

    [Test]
    public void ControlPoint_Capture_ChangesModelBodygroupAndSkin()
    {
        // The three lines, pinned as text because their being adjacent is the finding. Reading any
        // one of them alone gives a mechanism that is real and insufficient.
        //
        // Note the bodygroup is set to the RAW team number — 0, 2 or 3 — not to the remapped skin
        // index. Two different encodings of the same fact, three lines apart, and using one where the
        // other belongs picks a valid-looking bodygroup that is wrong.
        string source = SourceSdk.Text(ControlPoint).ShouldNotBeNull();

        source.ShouldContain("SetModel( STRING(m_TeamData[m_iTeam].iszModel) )");
        source.ShouldContain("SetBodygroup( 0, m_iTeam )");
        source.ShouldContain("m_nSkin = ( m_iTeam == TEAM_UNASSIGNED ) ? 2 : (m_iTeam - 2)");

        // **The gap, asserted so this marker can close (D45).** A control point's appearance is
        // three coupled changes and this project reproduces none of them, so a captured point looks
        // exactly like an uncaptured one. Control first, or an always-false search would let every
        // marker skip for ever.
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a name that is demonstrably compiled in");

        SchemaGap.AnyProductionAssemblyMentions("m_TeamData").ShouldBeFalse(
            "the point's per-team data is now read — replace this marker with a parity test that "
            + "covers all THREE changes, since reading any one alone gives a mechanism that is "
            + "real and insufficient");

        Assert.Ignore(
            "control point appearance is not reproduced: the model, the bodygroup and the skin all "
            + "change on capture (team_control_point.cpp), and none of the three is read here — so "
            + "a captured point is indistinguishable from an uncaptured one.");
    }

}
