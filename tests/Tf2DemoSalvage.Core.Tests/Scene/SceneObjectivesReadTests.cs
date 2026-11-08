using System.Collections.Generic;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using static Tf2DemoSalvage.Core.Tests.Scene.EntityWorld;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_BaseTeamObjectiveResource` as the control-point HUD reads it (c_team_objectiveresource.h).</summary>
public sealed class SceneObjectivesReadTests
{
    private const string Table = "DT_BaseTeamObjectiveResource.";
    private const float Now = 8f;

    private static EntityState Resource(params (string Key, Tf2DemoSalvage.Core.Schema.PropertyValue Value)[] properties) =>
        new EntityWorld().Add("CTFObjectiveResource", 1, 1, properties);

    private static NameTable Materials(params (int Index, string Name)[] names)
    {
        NameTable table = new();
        List<StringTableEntry> entries = [];

        foreach ((int index, string name) in names)
        {
            entries.Add(new StringTableEntry(index, name, []));
        }

        table.Apply(entries);

        return table;
    }

    private static SceneObjectives Read(EntityState resource, SceneObjectives? last = null, NameTable? materials = null) =>
        SceneObjectives.Read(resource, last, materials ?? Materials(), Now)!;

    [Test]
    public void Read_NoResource_IsNull() =>
        SceneObjectives.Read(null, null, Materials(), Now).ShouldBeNull();

    [Test]
    public void PackPrevious_ThreeSlots_UnpackToTheSameSignedValues()
    {
        int packed = SceneObjectives.PackPrevious(3, -1, 7);

        SceneObjectives.UnpackPrevious(packed, 0).ShouldBe(3);
        SceneObjectives.UnpackPrevious(packed, 1).ShouldBe(-1);
        SceneObjectives.UnpackPrevious(packed, 2).ShouldBe(7);
    }

    [Test]
    public void Read_APointsEveryField_ArrivesWhereTheHudReadsIt()
    {
        EntityState resource = Resource(
            Int(Table + "m_iNumControlPoints", 2),
            Int("m_bCPIsVisible.001", 1),
            Int("m_iOwner.001", 2),
            Int("m_iCappingTeam.001", 3),
            Int("m_iCPGroup.001", 4),
            Int("m_bCPLocked.001", 1),
            Int("m_bBlocked.001", 1),
            Float("m_flLazyCapPerc.001", 0.5f),
            Float("m_flUnlockTimes.001", 40f),
            Int("m_iTeamIcons.001", 21),
            Int("m_iTeamIcons.009", 22),
            Int("m_iTeamIcons.017", 23),
            Int("m_iTeamIcons.025", 24),
            Int("m_iTeamOverlays.001", 31),
            Int("m_iNumTeamMembers.017", 5),
            Int("m_bTeamCanCap.001", 1),
            Int("m_bTeamCanCap.017", 1),
            Float("m_flTeamCapTime.009", 2.5f),
            Float("m_flTeamCapTime.025", 3.5f),
            Int("m_bInMiniRound.001", 1),
            Int("m_iPreviousPoints.051", 0),
            Int("m_iPreviousPoints.052", 1),
            Int("m_iPreviousPoints.075", 1),
            Float("m_flCPTimerTimes.001", 99f));

        SceneControlPoint point = Read(resource).Points[1];

        point.Visible.ShouldBeTrue();
        point.Owner.ShouldBe(2);
        point.CappingTeam.ShouldBe(3);
        point.Group.ShouldBe(4);
        point.Locked.ShouldBeTrue();
        point.Blocked.ShouldBeTrue();
        point.LazyCapPercent.ShouldBe(0.5f);
        point.UnlockTime.ShouldBe(40f);
        point.Icons.ShouldBe(new TeamQuad<int>(21, 22, 23, 24));
        point.Overlays.ShouldBe(new TeamQuad<int>(31, 0, 0, 0));
        point.TeamMembers.ShouldBe(new TeamQuad<int>(0, 0, 5, 0));
        point.CanCap.ShouldBe(new TeamQuad<bool>(true, false, true, false));
        point.CapTime.ShouldBe(new TeamQuad<float>(0f, 2.5f, 0f, 3.5f));
        point.InMiniRound.ShouldBeTrue();
        point.TimerTime.ShouldBe(99f);

        // Red's slots for point 1 start at 1 * 3 + 2 * 8 * 3 = 51; blue's at 1 * 3 + 3 * 8 * 3 = 75.
        SceneObjectives.UnpackPrevious(point.PreviousRed, 0).ShouldBe(0);
        SceneObjectives.UnpackPrevious(point.PreviousRed, 1).ShouldBe(1);
        SceneObjectives.UnpackPrevious(point.PreviousRed, 2).ShouldBe(-1);
        SceneObjectives.UnpackPrevious(point.PreviousBlue, 0).ShouldBe(1);
        SceneObjectives.UnpackPrevious(point.PreviousBlue, 1).ShouldBe(-1);
    }

    [Test]
    public void Read_AnUnsentPoint_IsAllDefaultsWithNoTimer()
    {
        SceneControlPoint point = Read(Resource(Int(Table + "m_iNumControlPoints", 1))).Points[0];

        point.ShouldBe(new SceneControlPoint(
            false, 0, 0, 0, false, false, 0f, 0f, default, default, default, default, default, false, SceneObjectives.PackPrevious(-1, -1, -1), SceneObjectives.PackPrevious(-1, -1, -1)));
    }

    [TestCase(12, 8)]
    [TestCase(-3, 0)]
    [TestCase(5, 5)]
    public void Read_ThePointCount_IsClampedToTheResourcesCapacity(int sent, int expected) =>
        Read(Resource(Int(Table + "m_iNumControlPoints", sent))).Points.Count.ShouldBe(expected);

    [Test]
    public void Read_TheWholeResource_ArrivesWithItsLayoutBasesAndPosition()
    {
        SceneObjectives read = Read(Resource(
            Str(Table + "m_pszCapLayoutInHUD", "0 1"),
            Int(Table + "m_bPlayingMiniRounds", 1),
            Int("m_iBaseControlPoints.002", 4),
            Int("m_iBaseControlPoints.003", 6),
            Float(Table + "m_flCustomPositionX", 0.25f),
            Float(Table + "m_flCustomPositionY", 0.75f)));

        read.CapLayout.ShouldBe("0 1");
        read.PlayingMiniRounds.ShouldBeTrue();
        read.BaseRed.ShouldBe(4);
        read.BaseBlue.ShouldBe(6);
        read.CustomPositionX.ShouldBe(0.25f);
        read.CustomPositionY.ShouldBe(0.75f);
    }

    [Test]
    public void Read_AnEmptyResource_IsTheUnsentDefaults()
    {
        SceneObjectives read = Read(Resource());

        read.CapLayout.ShouldBe(string.Empty);
        read.PlayingMiniRounds.ShouldBeFalse();
        read.BaseRed.ShouldBe(-1);
        read.BaseBlue.ShouldBe(-1);
        read.CustomPositionX.ShouldBe(-1f);
        read.CustomPositionY.ShouldBe(-1f);
    }

    [Test]
    public void Read_TheTrack_ArrivesAsTheHillsAndAlarmsTheResourceSent()
    {
        SceneTrack track = Read(Resource(
            Float("m_flPathDistance.001", 0.4f),
            Int("m_nNumNodeHillData.003", 2),
            Float("m_flNodeHillData.030", 0.1f),
            Float("m_flNodeHillData.031", 0.2f),
            Int("m_bTrackAlarm.002", 1),
            Int("m_bHillIsDownhill.016", 1))).Track!;

        track.PathDistances[1].ShouldBe(0.4f);
        track.HillCount(3).ShouldBe(2);
        track.Hill(3, 0).ShouldBe((0.1f, 0.2f));
        track.TrackAlarm(2).ShouldBeTrue();
        track.IsDownhill(3, 1).ShouldBeTrue();
    }

    [Test]
    public void Read_NothingChanged_ReturnsTheLastInstance()
    {
        EntityState resource = Resource(Int(Table + "m_iNumControlPoints", 2), Int("m_iOwner.000", 2));
        SceneObjectives first = Read(resource);

        Read(resource, first).ShouldBeSameAs(first);
    }

    [Test]
    public void Read_ASecondFrameWithMorePointsThanTheLast_StartsTheNewPointsPulseFromNothing()
    {
        SceneObjectives first = Read(Resource(Int(Table + "m_iNumControlPoints", 1), Int("m_iCappingTeam.000", 3), Int("m_iOwner.000", 2)));
        EntityState later = Resource(
            Int(Table + "m_iNumControlPoints", 2),
            Int("m_iCappingTeam.000", 3),
            Int("m_iOwner.000", 2),
            Int("m_iCappingTeam.001", 2),
            Int("m_iOwner.001", 3));

        SceneObjectives second = Read(later, first);

        // Point 0 kept capping 3 over 2: no change, so its pulse is the one the first frame started.
        second.Points[0].Pulse.ShouldBe(first.Points[0].Pulse);
        first.Points[0].Pulse.ShouldBe(new CapPulse(3, Now, 0f, true));

        // Point 1 had no earlier frame: capping 2 against a before of 0 is a change, and 2 is not 3, the owner.
        second.Points[1].Pulse.ShouldBe(new CapPulse(2, Now, 0f, true));
    }

    private static IEnumerable<TestCaseData> Changes()
    {
        yield return new TestCaseData(new[] { Int(Table + "m_bPlayingMiniRounds", 1) }, null).SetName("Read_MiniRoundsChange_IsANewInstance");
        yield return new TestCaseData(new[] { Int("m_iBaseControlPoints.002", 1) }, null).SetName("Read_TheRedBaseChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Int("m_iBaseControlPoints.003", 1) }, null).SetName("Read_TheBlueBaseChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Float(Table + "m_flCustomPositionX", 0.5f) }, null).SetName("Read_TheCustomXChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Float(Table + "m_flCustomPositionY", 0.5f) }, null).SetName("Read_TheCustomYChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Str(Table + "m_pszCapLayoutInHUD", "1") }, null).SetName("Read_TheLayoutChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Float("m_flPathDistance.000", 0.5f) }, null).SetName("Read_TheTrackChanges_IsANewInstance");
        yield return new TestCaseData(new[] { Int("m_iOwner.000", 3) }, null).SetName("Read_APointChanges_IsANewInstance");
        yield return new TestCaseData(System.Array.Empty<(string, Tf2DemoSalvage.Core.Schema.PropertyValue)>(), Materials((9, "a"))).SetName("Read_TheMaterialTableGrows_IsANewInstance");
    }

    [TestCaseSource(nameof(Changes))]
    public void Read_OneInputChanges_IsANewInstanceThatDiffers(
        (string Key, Tf2DemoSalvage.Core.Schema.PropertyValue Value)[] changed, NameTable? materials)
    {
        (string, Tf2DemoSalvage.Core.Schema.PropertyValue)[] baseline = [Int(Table + "m_iNumControlPoints", 1)];
        SceneObjectives first = Read(Resource(baseline));
        (string, Tf2DemoSalvage.Core.Schema.PropertyValue)[] all = [.. baseline, .. changed];

        SceneObjectives second = Read(Resource(all), first, materials);

        second.ShouldNotBeSameAs(first);

        // A longer table with no icon naming it changes the read's bookkeeping and nothing that `Equals` compares.
        second.Equals(first).ShouldBe(materials is not null);
    }

    [Test]
    public void Read_TheTrackUnchangedButAPointChanged_ReusesTheLastTrackInstance()
    {
        SceneObjectives first = Read(Resource(Int(Table + "m_iNumControlPoints", 1)));
        SceneObjectives second = Read(Resource(Int(Table + "m_iNumControlPoints", 1), Int("m_iOwner.000", 3)), first);

        second.Track.ShouldBeSameAs(first.Track);
    }

    [Test]
    public void Read_IconsAndOverlaysTheTableNames_AreResolvedAndTheOthersLeftOut()
    {
        NameTable materials = Materials((5, "hud/cp_a"), (6, "hud/cp_b"));
        SceneObjectives read = Read(
            Resource(
                Int(Table + "m_iNumControlPoints", 1),
                Int("m_iTeamIcons.000", 5),
                Int("m_iTeamIcons.016", 99),
                Int("m_iTeamOverlays.008", 6)),
            materials: materials);

        read.IconNames.ShouldBe(new Dictionary<int, string> { [5] = "hud/cp_a", [6] = "hud/cp_b" });
        read.MaterialCount.ShouldBe(2);
    }

    [Test]
    public void Equals_Null_IsFalse() => Read(Resource()).Equals(null).ShouldBeFalse();

    [Test]
    public void Equals_IconNamesDiffer_IsFalse()
    {
        EntityState resource = Resource(Int(Table + "m_iNumControlPoints", 1), Int("m_iTeamIcons.000", 5));

        Read(resource, materials: Materials((5, "a"))).Equals(Read(resource, materials: Materials((5, "b")))).ShouldBeFalse();
    }

    [Test]
    public void Equals_TheIconNamesDifferInKey_IsFalse()
    {
        // Same point icons, so only the table's key differs and `Points` cannot decide it first.
        EntityState resource = Resource(Int(Table + "m_iNumControlPoints", 1), Int("m_iTeamIcons.000", 5));
        SceneObjectives one = Read(resource, materials: Materials((5, "a")));
        SceneObjectives other = Read(resource, materials: Materials((6, "a")));

        one.Equals(other).ShouldBeFalse();
    }

    [Test]
    public void Equals_TheIconNamesDifferInCount_IsFalse()
    {
        SceneObjectives one = Read(Resource(Int(Table + "m_iNumControlPoints", 1)));
        SceneObjectives other = one with { IconNames = new Dictionary<int, string> { [1] = "x" } };

        one.Equals(other).ShouldBeFalse();
    }

    [Test]
    public void GetHashCode_TwoReadsOfOneResource_Agree()
    {
        EntityState resource = Resource(Int(Table + "m_iNumControlPoints", 2));

        Read(resource).GetHashCode().ShouldBe(Read(resource).GetHashCode());
    }
}
