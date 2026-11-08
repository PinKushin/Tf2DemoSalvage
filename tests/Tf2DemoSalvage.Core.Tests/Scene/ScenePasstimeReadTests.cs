using System.Collections.Generic;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;
using static Tf2DemoSalvage.Core.Tests.Scene.EntityWorld;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFPasstimeLogic`, its ball and goals as the Pass Time HUD reads them (tf_hud_passtime.cpp).</summary>
public sealed class ScenePasstimeReadTests
{
    private const int EffectNoDraw = 0x20;
    private const float Tolerance = 1e-4f;

    private static (string Key, PropertyValue Value) Track(params (float X, float Y, float Z)[] points)
    {
        List<PropertyValue> values = [];

        foreach ((float x, float y, float z) in points)
        {
            values.Add(PropertyValue.FromVector(x, y, z));
        }

        return ("DT_TFPasstimeLogic.m_trackPoints", PropertyValue.FromArray(values));
    }

    private static ScenePasstime Bare(params (float X, float Y, float Z)[] points) =>
        new(points, 0, 0f, 0, 0, 0, default, 0, 0, 0, []);

    private static ScenePasstime? Read(EntityWorld world, ScenePasstime? last = null) => ScenePasstime.Read(world.Table, last);

    [Test]
    public void Read_NoLogicEntity_IsNull() => Read(new EntityWorld()).ShouldBeNull();

    [Test]
    public void Read_ALogicWithNothingSent_IsTheEmptyState()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1);

        ScenePasstime read = Read(world)!;

        read.TrackPoints.Count.ShouldBe(16);
        read.TrackPoints.ShouldAllBe(point => point.X == 0f && point.Y == 0f && point.Z == 0f);
        read.NumSections.ShouldBe(0);
        read.MaxPassRange.ShouldBe(0f);
        read.BallPower.ShouldBe(0);
        read.Ball.ShouldBe(0);
        read.BallTeam.ShouldBe(0);
        read.BallOrigin.ShouldBe((0f, 0f, 0f));
        read.Carrier.ShouldBe(0);
        read.PrevCarrier.ShouldBe(0);
        read.HomingTarget.ShouldBe(0);
        read.Goals.ShouldBeEmpty();
        read.Players.ShouldBeEmpty();
        read.Target.ShouldBe((false, (0f, 0f, 0f)));
    }

    [Test]
    public void Read_ALogicsOwnFields_ArriveAsSent()
    {
        EntityWorld world = new();
        world.Add(
            "CTFPasstimeLogic",
            1,
            1,
            Track((1f, 2f, 3f), (4f, 5f, 6f)),
            Int("DT_TFPasstimeLogic.m_iNumSections", 4),
            Float("DT_TFPasstimeLogic.m_flMaxPassRange", 1500f),
            Int("DT_TFPasstimeLogic.m_iBallPower", 80));

        ScenePasstime read = Read(world)!;

        read.TrackPoints[0].ShouldBe((1f, 2f, 3f));
        read.TrackPoints[1].ShouldBe((4f, 5f, 6f));
        read.TrackPoints[2].ShouldBe((0f, 0f, 0f));
        read.NumSections.ShouldBe(4);
        read.MaxPassRange.ShouldBe(1500f);
        read.BallPower.ShouldBe(80);
    }

    [Test]
    public void Read_ACarriedBall_NamesItsCarrierAndTheReticleFollowsTheCarrier()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_hBall", Handle(2, 1)));
        world.Add(
            "CPasstimeBall",
            2,
            1,
            Int("DT_BaseEntity.m_iTeamNum", 3),
            Vector("DT_BaseEntity.m_vecOrigin", 10f, 20f, 30f),
            Int("DT_PasstimeBall.m_hCarrier", Handle(3, 1)),
            Int("DT_PasstimeBall.m_hPrevCarrier", Handle(4, 1)),
            Int("DT_PasstimeBall.m_hHomingTarget", Handle(4, 1)));
        world.Add(
            "CTFPlayer",
            3,
            1,
            Vector("DT_BaseEntity.m_vecOrigin", 100f, 0f, 0f),
            Float("DT_LocalPlayerExclusive.m_vecViewOffset[0]", 1f),
            Float("DT_LocalPlayerExclusive.m_vecViewOffset[1]", 2f),
            Float("DT_LocalPlayerExclusive.m_vecViewOffset[2]", 64f),
            Float("DT_TFPlayerShared.m_askForBallTime", 2.5f),
            Int("DT_TFPlayerShared.m_bIsTargetedForPasstimePass", 1),
            Int("DT_TFPlayerShared.m_bHasPasstimeBall", 1),
            Int("DT_BaseEntity.m_fEffects", EffectNoDraw),
            Vector("DT_CollisionProperty.m_vecMins", -10f, -10f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 10f, 10f, 80f));
        world.Add("CTFPlayer", 4, 1);

        ScenePasstime read = Read(world)!;

        read.Ball.ShouldBe(2);
        read.BallTeam.ShouldBe(3);
        read.BallOrigin.ShouldBe((10f, 20f, 30f));
        read.Carrier.ShouldBe(3);
        read.PrevCarrier.ShouldBe(4);
        read.HomingTarget.ShouldBe(4);
        read.Players.ShouldBe(
        [
            new ScenePasstimePlayer(3, 2.5f, true, true, (101f, 2f, 64f)),
            new ScenePasstimePlayer(4, 0f, false, false, (0f, 0f, 0f)),
        ]);

        // The carrier's EF_NODRAW and the middle of its box above its origin: (100, 0, 0) + (0, 0, 40).
        read.Target.ShouldBe((true, (100f, 0f, 40f)));
    }

    [Test]
    public void Read_ABallNobodyCarries_IsTheReticlesTarget()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_hBall", Handle(2, 1)));
        world.Add(
            "CPasstimeBall",
            2,
            1,
            Vector("DT_BaseEntity.m_vecOrigin", 10f, 20f, 30f),
            Vector("DT_CollisionProperty.m_vecMins", -2f, -2f, -2f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 2f, 6f));

        ScenePasstime read = Read(world)!;

        read.Carrier.ShouldBe(0);
        read.Target.ShouldBe((false, (10f, 20f, 32f)));
    }

    [Test]
    public void Read_ACarrierHandleThatResolvesToNothing_FallsBackToTheBall()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_hBall", Handle(2, 1)));
        world.Add(
            "CPasstimeBall",
            2,
            1,
            Vector("DT_BaseEntity.m_vecOrigin", 10f, 20f, 30f),
            Int("DT_PasstimeBall.m_hCarrier", Handle(40, 1)));

        ScenePasstime read = Read(world)!;

        read.Carrier.ShouldBe(0);
        read.Target.ShouldBe((false, (10f, 20f, 30f)));
    }

    [Test]
    public void Read_ABallHandleThatResolvesToNothing_HasNoBallAndNoTarget()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_hBall", Handle(9, 1)));

        ScenePasstime read = Read(world)!;

        read.Ball.ShouldBe(0);
        read.Target.ShouldBe((false, (0f, 0f, 0f)));
    }

    [Test]
    public void Read_AGoal_ArrivesWithItsTeamTypeAndPlace()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1);
        world.Add(
            "CFuncPasstimeGoal",
            7,
            1,
            Int("DT_BaseEntity.m_iTeamNum", 2),
            Int("DT_FuncPasstimeGoal.m_bTriggerDisabled", 1),
            Int("DT_FuncPasstimeGoal.m_iGoalType", 2),
            Vector("DT_BaseEntity.m_vecOrigin", 5f, 6f, 7f));
        world.Add("CFuncPasstimeGoal", 8, 1);

        Read(world)!.Goals.ShouldBe(
        [
            new ScenePasstimeGoal(7, 2, true, 2, (5f, 6f, 7f)),
            new ScenePasstimeGoal(8, 0, false, 0, (0f, 0f, 0f)),
        ]);
    }

    [Test]
    public void Read_NothingChangedSinceTheLastRead_ReturnsTheLastInstance()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_iBallPower", 10));
        ScenePasstime first = Read(world)!;

        Read(world, first).ShouldBeSameAs(first);
    }

    [Test]
    public void Read_APowerChangedSinceTheLastRead_IsANewInstance()
    {
        EntityWorld before = new();
        before.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_iBallPower", 10));
        EntityWorld after = new();
        after.Add("CTFPasstimeLogic", 1, 1, Int("DT_TFPasstimeLogic.m_iBallPower", 20));
        ScenePasstime first = Read(before)!;

        ScenePasstime second = Read(after, first)!;

        second.ShouldNotBeSameAs(first);
        second.BallPower.ShouldBe(20);
    }

    [Test]
    public void Player_ByIndex_FindsTheMatchOrNull()
    {
        ScenePasstimePlayer player = new(4, 1f, false, false, default);
        ScenePasstime state = Bare() with { Players = [new ScenePasstimePlayer(3, 0f, false, false, default), player] };

        state.Player(4).ShouldBe(player);
        state.Player(9).ShouldBeNull();
    }

    [Test]
    public void Player_WithNoPlayersList_IsNull() => Bare().Player(4).ShouldBeNull();

    [Test]
    public void ProgressFrac_NoTrackPoints_IsZero() => Bare().ProgressFrac((1f, 2f, 3f)).ShouldBe(0f);

    [Test]
    public void ProgressFrac_OnePoint_IsZero() => Bare((1f, 1f, 1f)).ProgressFrac((1f, 2f, 3f)).ShouldBe(0f);

    [Test]
    public void ProgressFrac_ASecondPointAtZero_EndsTheTrackAndIsZero() =>
        Bare((1f, 1f, 1f), (0f, 0f, 0f), (5f, 5f, 5f)).ProgressFrac((1f, 2f, 3f)).ShouldBe(0f);

    [Test]
    public void ProgressFrac_HalfwayAlongTheFirstSegment_IsItsShareOfTheTrackPlusOne()
    {
        ScenePasstime state = Bare((0f, 0f, 0f), (100f, 0f, 0f), (100f, 100f, 0f));

        // The nearest point is (50, 0, 0), 50 along a track of 1 + 100 + 100: 51 / 201.
        state.ProgressFrac((50f, 10f, 0f)).ShouldBe(51f / 201f, Tolerance);
    }

    [Test]
    public void ProgressFrac_BeyondTheFirstSegmentNearerTheSecond_IsPastItsOwnLength()
    {
        ScenePasstime state = Bare((0f, 0f, 0f), (100f, 0f, 0f), (100f, 100f, 0f));

        // The nearest point is (100, 60, 0): 100 along the first segment plus 60 along the second, plus the 1: 161 / 201.
        state.ProgressFrac((110f, 60f, 0f)).ShouldBe(161f / 201f, Tolerance);
    }

    [Test]
    public void ProgressFrac_ARepeatedPoint_UsesAZeroFractionForTheEmptySegment()
    {
        // The segment has no length (below 0.00001), so the closest point is its start, and the whole track is 1 long.
        Bare((5f, 5f, 5f), (5f, 5f, 5f)).ProgressFrac((5f, 5f, 5f)).ShouldBe(1f);
    }

    private static IEnumerable<TestCaseData> Differing()
    {
        yield return Case("TrackPoints", state => state with { TrackPoints = [(9f, 9f, 9f)] });
        yield return Case("NumSections", state => state with { NumSections = 9 });
        yield return Case("MaxPassRange", state => state with { MaxPassRange = 9f });
        yield return Case("BallPower", state => state with { BallPower = 9 });
        yield return Case("Ball", state => state with { Ball = 9 });
        yield return Case("BallTeam", state => state with { BallTeam = 9 });
        yield return Case("BallOrigin", state => state with { BallOrigin = (9f, 9f, 9f) });
        yield return Case("Carrier", state => state with { Carrier = 9 });
        yield return Case("PrevCarrier", state => state with { PrevCarrier = 9 });
        yield return Case("HomingTarget", state => state with { HomingTarget = 10 });
        yield return Case("Goals", state => state with { Goals = [] });
        yield return Case("Players", state => state with { Players = [] });
        yield return Case("Target", state => state with { Target = (true, (1f, 1f, 1f)) });
    }

    private static TestCaseData Case(string field, System.Func<ScenePasstime, ScenePasstime> change) =>
        new TestCaseData(change).SetName($"Equals_{field}Differs_IsFalse");

    private static ScenePasstime Full() => new(
        [(1f, 2f, 3f)],
        3,
        1000f,
        50,
        2,
        3,
        (4f, 5f, 6f),
        7,
        8,
        9,
        [new ScenePasstimeGoal(10, 2, false, 1, (7f, 7f, 7f))],
        [new ScenePasstimePlayer(7, 0f, false, true, (1f, 1f, 1f))],
        (false, (2f, 2f, 2f)));

    [TestCaseSource(nameof(Differing))]
    public void Equals_OneFieldDiffers_IsFalse(System.Func<ScenePasstime, ScenePasstime> change)
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
    public void Equals_NullPlayersAgainstAnEmptyList_IsTrue() =>
        (Full() with { Players = null }).Equals(Full() with { Players = [] }).ShouldBeTrue();

    [Test]
    public void Equals_Null_IsFalse() => Full().Equals(null).ShouldBeFalse();

    [Test]
    public void GetHashCode_IsTheCombinationOfBallCarrierPowerAndGoalCount() =>
        Full().GetHashCode().ShouldBe(System.HashCode.Combine(2, 7, 50, 1));

    [Test]
    public void Read_ACarrierPlayersEye_AddsTheViewOffsetToTheOriginWithinTolerance()
    {
        EntityWorld world = new();
        world.Add("CTFPasstimeLogic", 1, 1);
        world.Add("CTFPlayer", 3, 1, Vector("DT_BaseEntity.m_vecOrigin", 0.1f, 0.2f, 0.3f), Float("DT_LocalPlayerExclusive.m_vecViewOffset[2]", 64f));

        ScenePasstimePlayer player = Read(world)!.Players!.ShouldHaveSingleItem();

        player.Eye.X.ShouldBe(0.1f, Tolerance);
        player.Eye.Y.ShouldBe(0.2f, Tolerance);
        player.Eye.Z.ShouldBe(64.3f, Tolerance);
    }
}
