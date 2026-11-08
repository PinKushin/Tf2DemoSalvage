using System.Collections.Generic;
using Tf2DemoSalvage.Core.Scene;
using static Tf2DemoSalvage.Core.Tests.Scene.EntityWorld;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFRobotDestructionLogic`, `CTFPlayerDestructionLogic` and the robot groups (tf_logic_robot_destruction.cpp).</summary>
public sealed class SceneRobotDestructionReadTests
{
    private const string Logic = "DT_TFRobotDestructionLogic.";
    private const string Group = "DT_TFRobotDestruction_RobotGroup.";

    private static SceneRobotDestruction? Read(EntityWorld world, SceneRobotDestruction? last = null, System.Func<int, bool>? dormant = null) =>
        SceneRobotDestruction.Read(world.Table, last, dormant);

    private static SceneRobotDestruction Full() => new(
        false, 200, 10, 20, 30, 40, 5f, 6f, 7f, "res/rd.res", 8f, [])
    {
        TeamLeaders = (1, 2),
        CountdownImage = "img",
        UsingCountdownImage = true,
    };

    [Test]
    public void Read_NeitherLogic_IsNull() => Read(new EntityWorld()).ShouldBeNull();

    [Test]
    public void Read_ARobotDestructionLogic_ArrivesWithItsFields()
    {
        EntityWorld world = new();
        world.Add(
            "CTFRobotDestructionLogic",
            1,
            1,
            Int(Logic + "m_nMaxPoints", 200),
            Int(Logic + "m_nRedScore", 10),
            Int(Logic + "m_nBlueScore", 20),
            Int(Logic + "m_nRedTargetPoints", 30),
            Int(Logic + "m_nBlueTargetPoints", 40),
            Float(Logic + "m_flRedFinaleEndTime", 5f),
            Float(Logic + "m_flBlueFinaleEndTime", 6f),
            Float(Logic + "m_flFinaleLength", 7f),
            Str(Logic + "m_szResFile", "res/rd.res"),
            Float(Logic + "m_flCountdownEndTime", 8f));

        SceneRobotDestruction read = Read(world)!;

        read.PlayerDestruction.ShouldBeFalse();
        read.MaxPoints.ShouldBe(200);
        read.RedScore.ShouldBe(10);
        read.BlueScore.ShouldBe(20);
        read.RedTargetPoints.ShouldBe(30);
        read.BlueTargetPoints.ShouldBe(40);
        read.RedFinaleEndTime.ShouldBe(5f);
        read.BlueFinaleEndTime.ShouldBe(6f);
        read.FinaleLength.ShouldBe(7f);
        read.ResFile.ShouldBe("res/rd.res");
        read.CountdownEndTime.ShouldBe(8f);
        read.Groups.ShouldBeEmpty();
        read.TeamLeaders.ShouldBe((0, 0));
        read.CountdownImage.ShouldBe(string.Empty);
        read.UsingCountdownImage.ShouldBeFalse();
    }

    [Test]
    public void Read_ALogicWithNothingSent_IsAllZeroAndEmpty()
    {
        EntityWorld world = new();
        world.Add("CTFRobotDestructionLogic", 1, 1);

        SceneRobotDestruction read = Read(world)!;

        read.MaxPoints.ShouldBe(0);
        read.ResFile.ShouldBe(string.Empty);
        read.CountdownEndTime.ShouldBe(0f);
    }

    [Test]
    public void Read_APlayerDestructionLogic_IsFlaggedAndNamesItsLeadersAndCountdown()
    {
        EntityWorld world = new();
        world.Add("CTFPlayerDestructionLogic", 1, 1, Int("DT_TFPlayerDestructionLogic.m_hRedTeamLeader", Handle(5, 2)), Int("DT_TFPlayerDestructionLogic.m_hBlueTeamLeader", Handle(6, 2)), Str("DT_TFPlayerDestructionLogic.m_iszCountdownImage", "hud/count"), Int("DT_TFPlayerDestructionLogic.m_bUsingCountdownImage", 1));
        world.Add("CTFPlayer", 5, 2);
        world.Add("CTFPlayer", 6, 2);

        SceneRobotDestruction read = Read(world)!;

        read.PlayerDestruction.ShouldBeTrue();
        read.TeamLeaders.ShouldBe((5, 6));
        read.CountdownImage.ShouldBe("hud/count");
        read.UsingCountdownImage.ShouldBeTrue();
    }

    [Test]
    public void Read_BothLogicsPresent_TakesTheRobotOne()
    {
        EntityWorld world = new();
        world.Add("CTFPlayerDestructionLogic", 2, 1);
        world.Add("CTFRobotDestructionLogic", 1, 1);

        Read(world)!.PlayerDestruction.ShouldBeFalse();
    }

    [Test]
    public void Read_ALeaderHandleThatResolvesToNothing_IsZero()
    {
        EntityWorld world = new();
        world.Add("CTFPlayerDestructionLogic", 1, 1, Int("DT_TFPlayerDestructionLogic.m_hRedTeamLeader", Handle(5, 2)));

        Read(world)!.TeamLeaders.ShouldBe((0, 0));
    }

    [Test]
    public void Read_RobotGroups_ArriveInEntityOrderWithDormancyAndSerial()
    {
        EntityWorld world = new();
        world.Add("CTFRobotDestructionLogic", 1, 1);
        world.Add(
            "CTFRobotDestruction_RobotGroup",
            10,
            6,
            Int(Group + "m_iTeamNum", 3),
            Int(Group + "m_nGroupNumber", 2),
            Int(Group + "m_nState", 3),
            Float(Group + "m_flRespawnStartTime", 1.5f),
            Float(Group + "m_flRespawnEndTime", 2.5f),
            Float(Group + "m_flLastAttackedTime", 3.5f),
            Str(Group + "m_pszHudIcon", "hud/robot"));
        world.Add("CTFRobotDestruction_RobotGroup", 11, 7);

        IReadOnlyList<SceneRobotGroup> groups = Read(world, dormant: index => index == 11)!.Groups;

        groups.Count.ShouldBe(2);
        groups[0].ShouldBe(new SceneRobotGroup(10, 3, 2, 3, 1.5f, 2.5f, 3.5f, "hud/robot") { SerialNumber = 6 });
        groups[1].ShouldBe(new SceneRobotGroup(11, 0, 0, 0, 0f, 0f, 0f, string.Empty) { SerialNumber = 7, Dormant = true });
    }

    [Test]
    public void Read_NothingChangedSinceTheLastRead_ReturnsTheLastInstance()
    {
        EntityWorld world = new();
        world.Add("CTFRobotDestructionLogic", 1, 1, Int(Logic + "m_nRedScore", 4));
        SceneRobotDestruction first = Read(world)!;

        Read(world, first).ShouldBeSameAs(first);
    }

    [Test]
    public void Read_AScoreChangedSinceTheLastRead_IsANewInstance()
    {
        EntityWorld before = new();
        before.Add("CTFRobotDestructionLogic", 1, 1, Int(Logic + "m_nRedScore", 4));
        EntityWorld after = new();
        after.Add("CTFRobotDestructionLogic", 1, 1, Int(Logic + "m_nRedScore", 5));
        SceneRobotDestruction first = Read(before)!;

        SceneRobotDestruction second = Read(after, first)!;

        second.ShouldNotBeSameAs(first);
        second.RedScore.ShouldBe(5);
    }

    [TestCase(2, 10)]
    [TestCase(3, 20)]
    [TestCase(0, 20)]
    public void Score_ByTeam_IsRedsForRedElseBlues(int team, int expected) => Full().Score(team).ShouldBe(expected);

    [TestCase(2, 30)]
    [TestCase(3, 40)]
    public void TargetScore_ByTeam_IsRedsForRedElseBlues(int team, int expected) => Full().TargetScore(team).ShouldBe(expected);

    [TestCase(2, 5f)]
    [TestCase(3, 6f)]
    public void FinaleWinTime_ByTeam_IsRedsForRedElseBlues(int team, float expected) => Full().FinaleWinTime(team).ShouldBe(expected);

    [TestCase(2, 1)]
    [TestCase(3, 2)]
    public void TeamLeader_ByTeam_IsRedsForRedElseBlues(int team, int expected) => Full().TeamLeader(team).ShouldBe(expected);

    private static IEnumerable<TestCaseData> Differing()
    {
        yield return Case("PlayerDestruction", state => state with { PlayerDestruction = true });
        yield return Case("MaxPoints", state => state with { MaxPoints = 1 });
        yield return Case("RedScore", state => state with { RedScore = 1 });
        yield return Case("BlueScore", state => state with { BlueScore = 1 });
        yield return Case("RedTargetPoints", state => state with { RedTargetPoints = 1 });
        yield return Case("BlueTargetPoints", state => state with { BlueTargetPoints = 1 });
        yield return Case("RedFinaleEndTime", state => state with { RedFinaleEndTime = 1f });
        yield return Case("BlueFinaleEndTime", state => state with { BlueFinaleEndTime = 1f });
        yield return Case("FinaleLength", state => state with { FinaleLength = 1f });
        yield return Case("ResFile", state => state with { ResFile = "other" });
        yield return Case("CountdownEndTime", state => state with { CountdownEndTime = 1f });
        yield return Case("TeamLeaders", state => state with { TeamLeaders = (9, 9) });
        yield return Case("CountdownImage", state => state with { CountdownImage = "other" });
        yield return Case("UsingCountdownImage", state => state with { UsingCountdownImage = false });
        yield return Case("Groups", state => state with { Groups = [new SceneRobotGroup(1, 2, 3, 0, 0f, 0f, 0f, string.Empty)] });
    }

    private static TestCaseData Case(string field, System.Func<SceneRobotDestruction, SceneRobotDestruction> change) =>
        new TestCaseData(change).SetName($"Equals_{field}Differs_IsFalse");

    [TestCaseSource(nameof(Differing))]
    public void Equals_OneFieldDiffers_IsFalse(System.Func<SceneRobotDestruction, SceneRobotDestruction> change)
    {
        System.ArgumentNullException.ThrowIfNull(change);

        Full().Equals(change(Full())).ShouldBeFalse();
    }

    [Test]
    public void Equals_TheSameContent_IsTrueWithTheSameHash()
    {
        Full().Equals(Full()).ShouldBeTrue();
        Full().GetHashCode().ShouldBe(Full().GetHashCode());
    }

    [Test]
    public void Equals_Null_IsFalse() => Full().Equals(null).ShouldBeFalse();

    [Test]
    public void GetHashCode_IsTheCombinationOfMaxPointsScoresAndGroupCount() =>
        Full().GetHashCode().ShouldBe(System.HashCode.Combine(200, 10, 20, 0));
}
