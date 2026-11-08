using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The payload track the objective resource carries (c_team_objectiveresource.h:213-269, :303-337).</summary>
public sealed class SceneTrackTests
{
    // Every hill datum holds its own index, so a read at (team, hill) names the slot it came from.
    private static SceneTrack Track(
        int hillDataLength = 40,
        float[]? distances = null,
        int[]? counts = null,
        bool[]? alarms = null,
        bool[]? downhills = null,
        int changedDatum = -1) => new(
            distances ?? [0.1f, 0.2f],
            counts ?? [0, 1, 2, 3],
            [.. Enumerable.Range(0, hillDataLength).Select(index => index == changedDatum ? 99f : index)],
            alarms ?? [false, false, true, false],
            downhills ?? [.. Enumerable.Range(0, 20).Select(index => index == 7)]);

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(3, 3)]
    [TestCase(-1, 0)]
    [TestCase(4, 0)]
    public void HillCount_ByTeam_IsTheCountOrZeroPastTheTeams(int team, int expected) =>
        Track().HillCount(team).ShouldBe(expected);

    [Test]
    public void Hill_AValidSlot_ReadsItsStartAndEnd() =>
        // index = hill * 2 + team * MaxHills * 2 = 4 + 10.
        Track().Hill(1, 2).ShouldBe((14f, 15f));

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(4, 0)]
    [TestCase(0, 5)]
    public void Hill_OutsideTheTeamsOrHills_IsZeroZero(int team, int hill) =>
        Track().Hill(team, hill).ShouldBe((0f, 0f));

    [Test]
    public void Hill_PastTheEndOfAShortArray_IsZeroZero() =>
        // index 4 is not below Count - 1 = 3.
        Track(hillDataLength: 4).Hill(0, 2).ShouldBe((0f, 0f));

    [Test]
    public void Hill_TheLastPairOfAShortArray_IsRead() =>
        // index 2 is below Count - 1 = 3.
        Track(hillDataLength: 4).Hill(0, 1).ShouldBe((2f, 3f));

    [TestCase(1, 2, true)]
    [TestCase(1, 1, false)]
    [TestCase(-1, 2, false)]
    [TestCase(4, 2, false)]
    [TestCase(1, -1, false)]
    [TestCase(1, 5, false)]
    public void IsDownhill_ByTeamAndHill_ReadsTheFlagOrFalse(int team, int hill, bool expected) =>
        // The one downhill is index 7 = hill 2 + team 1 * MaxHills.
        Track().IsDownhill(team, hill).ShouldBe(expected);

    [TestCase(2, true)]
    [TestCase(1, false)]
    [TestCase(-1, false)]
    [TestCase(4, false)]
    public void TrackAlarm_ByTeam_ReadsTheAlarmOrFalse(int team, bool expected) =>
        Track().TrackAlarm(team).ShouldBe(expected);

    [Test]
    public void Equals_TheSameContentInOtherLists_IsTrueWithTheSameHash()
    {
        SceneTrack a = Track();
        SceneTrack b = Track();

        a.Equals(b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Test]
    public void Equals_Null_IsFalse() => Track().Equals(null).ShouldBeFalse();

    [Test]
    public void GetHashCode_IsTheCombinationOfTheDistanceCountAndTheSummedHillCounts() =>
        Track().GetHashCode().ShouldBe(System.HashCode.Combine(2, 6));

    private static IEnumerable<TestCaseData> Differing()
    {
        yield return new TestCaseData(Track(distances: [0.1f, 0.3f])).SetName("Equals_ADifferentDistance_IsFalse");
        yield return new TestCaseData(Track(counts: [0, 1, 2, 2])).SetName("Equals_ADifferentHillCount_IsFalse");
        yield return new TestCaseData(Track(changedDatum: 5)).SetName("Equals_ADifferentHillDatum_IsFalse");
        yield return new TestCaseData(Track(hillDataLength: 39)).SetName("Equals_AShorterHillData_IsFalse");
        yield return new TestCaseData(Track(alarms: [false, false, false, false])).SetName("Equals_ADifferentAlarm_IsFalse");
        yield return new TestCaseData(Track(downhills: [.. Enumerable.Range(0, 20).Select(index => index == 8)])).SetName("Equals_ADifferentDownhill_IsFalse");
    }

    [TestCaseSource(nameof(Differing))]
    public void Equals_OneListDiffers_IsFalse(SceneTrack other) => Track().Equals(other).ShouldBeFalse();
}
