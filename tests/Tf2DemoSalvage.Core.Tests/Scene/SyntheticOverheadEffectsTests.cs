using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A player's overhead icons, carried by an authored demo through the timeline to the scene (B765).</summary>
/// <remarks>The rules are <see cref="OverheadEffectsConformanceTests"/>'; these hold the wiring.</remarks>
public sealed class SyntheticOverheadEffectsTests
{
    /// <summary>`TF_COND_MARKEDFORDEATH`, bit 30 of `m_nPlayerCond`.</summary>
    private const int Marked = 1 << 30;

    /// <remarks>
    /// **A player marked for death carries the mark**, unless he is the recorder (`UpdatedMarkedForDeathEffect` returns for
    /// the local player, `c_tf_player.cpp:5178`). The recorder slot decides which: 4 is someone else, 0 is this player.
    /// </remarks>
    [TestCase((byte)4, new[] { "mark_for_death" }, TestName = "Build_APlayerMarkedForDeath_CarriesTheMark")]
    [TestCase((byte)0, null, TestName = "Build_TheRecorderMarkedForDeath_CarriesNone")]
    public void Build_AMarkedPlayer_CarriesTheMarkUnlessHeIsTheRecorder(byte recorderSlot, string[]? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
            {
                PlayerCond = Marked, RecorderSlot = recorderSlot,
            }));

        timeline.Frames.Single(frame => frame.Tick == 100).Players.Single().OverheadEffects?.Select(icon => icon.Name)
            .ShouldBe(expected);
    }

    /// <remarks>
    /// **A dormant player keeps his mark**: entering dormancy frees every node (`:4308-4330`), and the think, which runs
    /// regardless, re-adds the mark on what he last held into the node it freed. Marked at tick 100, dormant at 101 (still
    /// marked, on the dormant player), back at 102 marked and carrying haste (`TF_COND_RUNE_HASTE`, 91, bit 27 of
    /// `m_nPlayerCondEx2`): the rune goes after the mark. Without the dormant think, the rune would take the freed node
    /// first and the dormant player would carry nothing. **The mark made again at 101 is a new effect**, so it runs from
    /// 101, not 100; the rune from 102.
    /// </remarks>
    [Test]
    public void Build_APlayerThroughDormancy_KeepsHisMarkInItsPlace()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround) { PlayerCond = Marked, RecorderSlot = 4 },
            new SyntheticPlayer.GestureSnapshot(101, 0f, PlayerActivityState.OnGround) { Dormant = true },
            new SyntheticPlayer.GestureSnapshot(102, 0f, PlayerActivityState.OnGround)
            {
                PlayerCond = Marked, PlayerCondEx2 = 1 << (91 - 64),
            }));

        timeline.Frames.Single(frame => frame.Tick == 100).Players.Single().OverheadEffects
            .ShouldBe([new OverheadIcon("mark_for_death", 100)], "the control: first made at 100");
        timeline.DormantPlayersAt(101).ShouldHaveSingleItem().OverheadEffects.ShouldBe([new OverheadIcon("mark_for_death", 101)]);
        timeline.Frames.Single(frame => frame.Tick == 102).Players.Single().OverheadEffects
            .ShouldBe([new OverheadIcon("mark_for_death", 101), new OverheadIcon("powerup_icon_haste_blue", 102)]);
    }
}
