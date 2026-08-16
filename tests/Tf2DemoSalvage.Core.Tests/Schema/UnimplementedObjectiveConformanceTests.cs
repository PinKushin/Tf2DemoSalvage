using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The objective state — who owns what, how far a capture got, and how long is left.
/// </summary>
/// <remarks>
/// **Thirteenth batch. For a review tool this is the score, and nothing here reads any of it.**
/// Positions, ownership, capture progress and the round clock all live on one entity,
/// <c>CTeamObjectiveResource</c>, as flat arrays indexed by control point.
///
/// **One of them removes a dependency rather than adding work.** <c>m_vCPPositions</c> networks each
/// capture point's world position, so a viewer can place them without parsing map entities at all.
/// That is worth knowing before anyone writes a BSP entity-lump reader to find them.
///
/// The trap in here is an indexing one, and it is the rare case where this project's usual
/// arithmetic defence cannot help. See <see cref="PerTeamArraysAreTeamMajorAndTheSizeCannotTellYou"/>.
/// </remarks>
public sealed class UnimplementedObjectiveConformanceTests
{
    /// <summary>Where the objective dimensions are declared.</summary>
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    /// <summary>The client's view of the objective resource.</summary>
    private const string ObjectiveHeader = "src/game/client/c_team_objectiveresource.h";

    /// <summary>Its networked table.</summary>
    private const string ObjectiveSource = "src/game/client/c_team_objectiveresource.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void PerTeamArraysAreTeamMajorAndTheSizeCannotTellYou()
    {
        // c_team_objectiveresource.h:18
        //
        //   #define TEAM_ARRAY( index, team )  (index + (team * MAX_CONTROL_POINTS))
        //
        // **Team-major.** The point index is the fast axis and the team stride is
        // MAX_CONTROL_POINTS. The other plausible layout — index * TEAMS + team — is what anyone
        // writing this from the field names would reach for, and it is wrong.
        //
        // **This is the case where this project's favourite technique fails, which is why it is
        // worth pinning.** "Arithmetic settles disputes" works because a wrong layout usually
        // implies a wrong SIZE. Here MAX_CONTROL_POINTS and MAX_CONTROL_POINT_TEAMS are both 8, so
        // both layouts give a 64-entry array and the total tells you nothing. A transposed read is
        // not a decode error and not a length mismatch — it silently returns another team's value
        // for another point, and on a symmetric map the result can even look plausible.
        //
        // The only thing that distinguishes them is the macro, so the macro is the assertion.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        int points = defs["MAX_CONTROL_POINTS"];
        int teams = defs["MAX_CONTROL_POINT_TEAMS"];

        // Recorded explicitly: the two dimensions being equal is exactly why a size check is blind
        // here. If Valve ever changes one, this fails and the size check becomes usable again —
        // which is a change of situation worth being told about.
        points.ShouldBe(teams);

        string header = SourceSdk.Text(ObjectiveHeader).ShouldNotBeNull();

        header.ShouldContain("#define TEAM_ARRAY( index, team )");
        header.ShouldContain("(index + (team * MAX_CONTROL_POINTS))");
    }

    [Test]
    public void PreviousPointsIsIndexedOnThreeAxesAtOnce()
    {
        // c_team_objectiveresource.h:155 — the deepest index in the structure:
        //
        //   iPrevIndex + (index_ * MAX_PREVIOUS_POINTS) + (team * MAX_CONTROL_POINTS * MAX_PREVIOUS_POINTS)
        //
        // Three axes flattened into one array: which previous point, which control point, which
        // team. **And its stride order is the opposite of TEAM_ARRAY's** — here the point index is
        // multiplied by MAX_PREVIOUS_POINTS, whereas there the point index was the raw offset.
        //
        // Two different flattening conventions in one class, twenty lines apart. An implementation
        // that derives one from the other gets the second wrong, so the size is derived here and the
        // shape asserted separately.
        IReadOnlyDictionary<string, int> defs = SourceSdk.Constants(SharedDefs);

        int previous = defs["MAX_PREVIOUS_POINTS"];
        int points = defs["MAX_CONTROL_POINTS"];
        int teams = defs["MAX_CONTROL_POINT_TEAMS"];

        previous.ShouldBe(3);
        (previous * points * teams).ShouldBe(192);

        string header = SourceSdk.Text(ObjectiveHeader).ShouldNotBeNull();

        header.ShouldContain("(index_ * MAX_PREVIOUS_POINTS) + (team * MAX_CONTROL_POINTS * MAX_PREVIOUS_POINTS)");
    }

    [Test]
    public void CapturePointPositionsAreNetworkedSoTheMapNeedNotBeParsedForThem()
    {
        // RecvPropArray( RecvPropVector(RECVINFO(m_vCPPositions[0])), m_vCPPositions )
        //
        // **This removes work rather than adding it.** The obvious way to place capture points in a
        // viewer is to read the BSP entity lump and find the team_control_point entities. That is
        // unnecessary: the positions are sent, per point, on an entity this project already decodes.
        //
        // Worth writing down before someone builds the entity-lump reader, which is the sort of
        // thing that gets built once and then justified afterwards.
        string source = SourceSdk.Text(ObjectiveSource).ShouldNotBeNull();

        source.ShouldContain("RecvPropArray( RecvPropVector(RECVINFO(m_vCPPositions[0])), m_vCPPositions)");

        Assert.Ignore(
            "capture point positions are not read. m_vCPPositions networks them per point, so a " +
            "viewer needs no BSP entity-lump parsing to place them.");
    }

    [Test]
    public void TheObjectiveResourceCarriesOwnershipProgressAndWhoIsStandingThere()
    {
        // One entity, a dozen parallel arrays: m_iOwner per point, m_flLazyCapPerc for capture
        // progress, m_iNumTeamMembers for who is standing in the zone, m_iTeamReqCappers for how
        // many are needed, m_bTeamCanCap for whether a team is allowed to.
        //
        // **This is the match, as data.** A demo review tool that shows player positions and not
        // this is showing the players without the game they were playing — who was capping, how far
        // it got, whether it was blocked, and by how many.
        //
        // Grouped into one entry rather than one per array on purpose: they are meaningless
        // individually and are read together or not at all.
        string source = SourceSdk.Text(ObjectiveSource).ShouldNotBeNull();

        foreach (string field in new[]
        {
            "m_iNumControlPoints", "m_flLazyCapPerc", "m_iTeamReqCappers", "m_bTeamCanCap",
        })
        {
            source.ShouldContain(field);
        }

        Assert.Ignore(
            "the objective resource is not decoded. Ownership, capture progress, who is in the " +
            "zone, how many cappers are required and whether a team may cap are all on one entity " +
            "— a review tool without them shows the players and not the game.");
    }

    [Test]
    public void TheRoundClockIsAnEntityRatherThanADerivedNumber()
    {
        // teamplay_round_timer.cpp:121-132 — twelve networked fields, including m_flTimerEndTime,
        // m_bTimerPaused, m_nSetupTimeLength and m_nState.
        //
        // **The clock is not derivable from tick numbers**, which is the reason this is not simply
        // "count the ticks and divide". It pauses, it is disabled during setup, it has a separate
        // setup length, and a round can end without it reaching zero. Anything computing a match
        // clock arithmetically will disagree with what players saw, most visibly during setup and
        // overtime.
        string timer = SourceSdk.Text("src/game/shared/teamplay_round_timer.cpp").ShouldNotBeNull();

        foreach (string field in new[]
        {
            "m_flTimerEndTime", "m_bTimerPaused", "m_nSetupTimeLength", "m_nState",
        })
        {
            timer.ShouldContain($"RECVINFO( {field} )");
        }

        Assert.Ignore(
            "the round timer entity is not decoded. The clock pauses, has a separate setup length " +
            "and a state, so it is not derivable from tick arithmetic — a computed clock disagrees " +
            "with what players saw during setup and overtime.");
    }
}
