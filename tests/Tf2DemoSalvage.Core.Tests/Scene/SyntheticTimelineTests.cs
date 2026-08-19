using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>DemoTimeline</c> driven by a demo this test wrote, schema included.
/// </summary>
/// <remarks>
/// **The first timeline test in this project that needs no recording.** Everything the timeline
/// does was corpus-only until now for one reason: a schema arrives in <c>dem_datatables</c> and
/// nothing here could write one. It shows in the coverage — 424 of <c>DemoTimeline</c>'s 528 lines
/// were never executed by <c>Core.Tests</c>, and the entity decoder, the entity assembly and the
/// send-prop encoder sat in the same position.
///
/// **What a synthetic demo can do that ten recordings cannot** is state the answer. A corpus test
/// asks whether a player's position is inside the world, because nobody knows where the player
/// actually was; this puts them at a chosen coordinate and asserts it. That is the difference
/// between a plausibility range and a prediction.
/// </remarks>
public sealed class SyntheticTimelineTests
{
    [Test]
    public void Build_APlayerAtAKnownPosition_PlacesThemThere()
    {
        // The corpus version of this can only bound the coordinate by the world extents. Here the
        // expected value is chosen, so a wrong scale, a swapped axis or a lost sign all fail.
        //
        // The Z coordinate travels in a different table from X and Y - m_vecOrigin carries two
        // components and m_vecOrigin[2] the third - which is exactly the split a fixture is
        // useful for pinning.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(512f, -1024f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(64f),
            ["m_iTeamNum"] = PropertyValue.FromInt(2),
            ["m_lifeState"] = PropertyValue.FromInt(0),
        }));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        player.X.ShouldBe(512f, 0.5f);
        player.Y.ShouldBe(-1024f, 0.5f);
        player.Z.ShouldBe(64f, 0.5f);
    }

    [Test]
    public void Build_ATeamAndLifeState_AreCarriedOntoThePlayer()
    {
        // Team decides which side a player is drawn on and life state whether they are drawn at
        // all, so both are read by the renderer rather than merely decoded.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
            ["m_lifeState"] = PropertyValue.FromInt(0),
        }));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        player.Team.ShouldBe(SceneTeams.Blu);
        player.IsPlaying.ShouldBeTrue();
        player.IsAlive.ShouldBeTrue();
    }

    [Test]
    public void Build_ADeadPlayer_IsNotAlive()
    {
        // A non-zero life state means dead, and the timeline derives IsAlive from it. Worth its
        // own case because the corpus can only find a dead player by looking for one, and a demo
        // where nobody dies would leave this path unexercised without saying so.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Red),
            ["m_lifeState"] = PropertyValue.FromInt(1),
        }));

        timeline.PlayersAt(66).ShouldHaveSingleItem().IsAlive.ShouldBeFalse();
    }

    [Test]
    public void Build_TheGroundFlag_DecidesWhetherAPlayerIsAirborne()
    {
        // FL_ONGROUND is bit 0 of m_fFlags. IsAirborne is its complement, and it drives which
        // animation the viewer picks - so getting the polarity backwards puts every player in a
        // jump. The corpus can show one state per sampled tick; both are asserted here.
        ScenePlayer grounded = Player(flags: 1);
        ScenePlayer airborne = Player(flags: 0);

        grounded.IsAirborne.ShouldBeFalse();
        airborne.IsAirborne.ShouldBeTrue();
    }

    [Test]
    public void Build_TheDuckFlag_DecidesWhetherAPlayerIsCrouched()
    {
        // FL_DUCKING is bit 1. Asserted alongside the ground bit rather than alone, because the
        // failure worth catching is the two being confused, and a test of one bit in isolation
        // cannot see that.
        Player(flags: 1 | 2).IsCrouched.ShouldBeTrue();
        Player(flags: 1).IsCrouched.ShouldBeFalse();
    }

    [Test]
    public void Build_NoDataTablesCommand_YieldsAnEmptyTimelineRatherThanThrowing()
    {
        // A demo whose schema never arrives is not corrupt - the launch-build SourceTV recording
        // in the corpus truncates its schema at exactly 64 KiB - and the timeline is expected to
        // come back empty rather than fail. Reproducible synthetically without a 20 MB file.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticDemo.Containing(SyntheticDemo.DefaultProtocol));

        timeline.Frames.ShouldBeEmpty();
        timeline.PlayersAt(66).ShouldBeEmpty();
    }

    /// <summary>A player carrying the given movement flags, at the origin.</summary>
    private static ScenePlayer Player(int flags) =>
        DemoTimeline.Build(SyntheticPlayer.Demo(new Dictionary<string, PropertyValue>
        {
            ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
            ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
            ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Red),
            ["m_lifeState"] = PropertyValue.FromInt(0),
            ["m_fFlags"] = PropertyValue.FromInt(flags),
        })).PlayersAt(66).ShouldHaveSingleItem();
}
