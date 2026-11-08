using Tf2DemoSalvage.Core.Scene;
using static Tf2DemoSalvage.Core.Tests.Scene.EntityWorld;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CCaptureFlag` and `CCaptureZone` as the flag HUD reads them (entity_capture_flag.cpp, func_capture_zone.cpp).</summary>
public sealed class SceneFlagsReadTests
{
    private const int SolidBbox = 2;
    private const int SolidObb = 3;
    private const int ForceWorldAligned = 0x40;
    private const float Tolerance = 1e-4f;

    private static SceneFlags? Read(EntityWorld world, SceneFlags? last = null, System.Func<int, bool>? dormant = null) =>
        SceneFlags.Read(world.Table, last, dormant);

    [Test]
    public void Read_NeitherFlagsNorZones_IsNull() =>
        Read(new EntityWorld()).ShouldBeNull();

    [Test]
    public void Read_AFlag_ArrivesWithEveryFieldTheHudReads()
    {
        EntityWorld world = new();
        world.Add("CTFPlayer", 5, 1, Int("DT_TFPlayer.m_hItem", Handle(9, 4)));
        world.Add("CTFPlayer", 6, 1, Int("DT_TFPlayer.m_hItem", Handle(30, 1)));
        world.Add("CTFPlayer", 7, 1);
        world.Add("CTFPlayer", 20, 3, Int("DT_BaseEntity.m_iTeamNum", 3));
        world.Add(
            "CCaptureFlag",
            9,
            4,
            Int("DT_BaseEntity.m_iTeamNum", 2),
            Int("DT_CaptureFlag.m_nFlagStatus", 1),
            Int("DT_CaptureFlag.m_bDisabled", 1),
            Int("DT_CaptureFlag.m_bVisibleWhenDisabled", 1),
            Int("DT_CaptureFlag.m_nType", 3),
            Str("DT_CaptureFlag.m_szHudIcon", "../hud/objectives_flagpanel_carried"),
            Float("DT_CaptureFlag.m_flTimeToSetPoisonous", 15f),
            Int("DT_CaptureFlag.m_nPointValue", 7),
            Float("DT_CaptureFlag.m_flResetTime", 60f),
            Float("DT_CaptureFlag.m_flNeutralTime", 50f),
            Float("DT_CaptureFlag.m_flMaxResetTime", 30f),
            Int("DT_CaptureFlag.m_hPrevOwner", Handle(20, 3)),
            Vector("DT_BaseEntity.m_vecOrigin", 100f, 200f, 300f),
            Vector("DT_CollisionProperty.m_vecMins", -4f, -4f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 4f, 4f, 10f));

        SceneFlag flag = Read(world, dormant: index => index == 9)!.Flags.ShouldHaveSingleItem();

        flag.EntityIndex.ShouldBe(9);
        flag.Team.ShouldBe(2);
        flag.Status.ShouldBe(1);
        flag.IsStolen.ShouldBeTrue();
        flag.IsDropped.ShouldBeFalse();
        flag.IsHome.ShouldBeFalse();
        flag.Disabled.ShouldBeTrue();
        flag.VisibleWhenDisabled.ShouldBeTrue();
        flag.Type.ShouldBe(3);
        flag.HudIcon.ShouldBe("../hud/objectives_flagpanel_carried");
        flag.PoisonTime.ShouldBe(15f);
        flag.PointValue.ShouldBe(7);
        flag.ResetTime.ShouldBe(60f);
        flag.NeutralTime.ShouldBe(50f);
        flag.MaxResetTime.ShouldBe(30f);
        flag.PrevOwner.ShouldBe(20);
        flag.PrevOwnerTeam.ShouldBe(3);
        flag.Dormant.ShouldBeTrue();
        flag.SerialNumber.ShouldBe(4);
        flag.Origin.ShouldBe((100f, 200f, 300f));
        flag.Center.ShouldBe((100f, 200f, 305f));

        // Players 5 and 6 are scanned past; only the one whose held item is this flag's slot carries it.
        flag.Carrier.ShouldBe(5);
    }

    [Test]
    public void Read_AFlagWithNothingSent_IsTheUnsentDefaults()
    {
        EntityWorld world = new();
        world.Add("CCaptureFlag", 9, 1);

        SceneFlag flag = Read(world)!.Flags.ShouldHaveSingleItem();

        flag.Team.ShouldBe(0);
        flag.IsHome.ShouldBeTrue();
        flag.Disabled.ShouldBeFalse();
        flag.HudIcon.ShouldBe(string.Empty);
        flag.Carrier.ShouldBe(0);
        flag.PrevOwner.ShouldBe(0);
        flag.PrevOwnerTeam.ShouldBeNull();
        flag.Dormant.ShouldBeFalse();
        flag.Origin.ShouldBe((0f, 0f, 0f));
        flag.Center.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Read_APreviousOwnerWhoIsGoneFromTheTable_HasNoTeam()
    {
        EntityWorld world = new();
        world.Add("CCaptureFlag", 9, 1, Int("DT_CaptureFlag.m_hPrevOwner", Handle(20, 3)));

        // The handle names slot 20, which nothing occupies, so it resolves to nothing at all.
        SceneFlag flag = Read(world)!.Flags.ShouldHaveSingleItem();

        flag.PrevOwner.ShouldBe(0);
        flag.PrevOwnerTeam.ShouldBeNull();
    }

    [Test]
    public void Read_APreviousOwnerWithNoTeamSent_IsTeamZero()
    {
        EntityWorld world = new();
        world.Add("CTFPlayer", 20, 3);
        world.Add("CCaptureFlag", 9, 1, Int("DT_CaptureFlag.m_hPrevOwner", Handle(20, 3)));

        Read(world)!.Flags.ShouldHaveSingleItem().PrevOwnerTeam.ShouldBe(0);
    }

    [Test]
    public void Read_ACarriedFlag_SitsAtItsCarriersOrigin()
    {
        EntityWorld world = new();
        world.Add("CTFPlayer", 5, 2, Vector("DT_BaseEntity.m_vecOrigin", 10f, 20f, 30f));
        world.Add(
            "CCaptureFlag",
            9,
            1,
            Int("DT_BaseEntity.moveparent", Handle(5, 2)),
            Vector("DT_BaseEntity.m_vecOrigin", 1f, 2f, 3f));

        Read(world)!.Flags.ShouldHaveSingleItem().Origin.ShouldBe((10f, 20f, 30f));
    }

    [Test]
    public void Read_AParentWithNoOrigin_PlacesTheFlagAtZero()
    {
        EntityWorld world = new();
        world.Add("CTFPlayer", 5, 2);
        world.Add("CCaptureFlag", 9, 1, Int("DT_BaseEntity.moveparent", Handle(5, 2)), Vector("DT_BaseEntity.m_vecOrigin", 1f, 2f, 3f));

        Read(world)!.Flags.ShouldHaveSingleItem().Origin.ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Read_AZone_ArrivesWithItsRadiusAndDormancy()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_BaseEntity.m_iTeamNum", 3),
            Int("DT_CaptureZone.m_bDisabled", 1),
            Vector("DT_BaseEntity.m_vecOrigin", 50f, 60f, 70f),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 3f, 6f));

        SceneCaptureZone zone = Read(world, dormant: index => index == 12)!.Zones.ShouldHaveSingleItem();

        zone.EntityIndex.ShouldBe(12);
        zone.Team.ShouldBe(3);
        zone.Disabled.ShouldBeTrue();
        zone.Dormant.ShouldBeTrue();

        // sqrt(2^2 + 3^2 + 6^2) = 7, halved.
        zone.BoundingRadius.ShouldBe(3.5f);
        zone.Center.ShouldBe((51f, 61.5f, 73f));
    }

    [Test]
    public void Read_AZoneWithNothingSent_IsEnabledAtTheOriginWithNoRadius()
    {
        EntityWorld world = new();
        world.Add("CCaptureZone", 12, 1);

        SceneCaptureZone zone = Read(world)!.Zones.ShouldHaveSingleItem();

        zone.Team.ShouldBe(0);
        zone.Disabled.ShouldBeFalse();
        zone.Dormant.ShouldBeFalse();
        zone.BoundingRadius.ShouldBe(0f);
    }

    [Test]
    public void Read_OnlyZones_HasNoFlags()
    {
        EntityWorld world = new();
        world.Add("CCaptureZone", 12, 1);

        Read(world)!.Flags.ShouldBeEmpty();
    }

    [Test]
    public void Read_OnlyFlags_HasNoZones()
    {
        EntityWorld world = new();
        world.Add("CCaptureFlag", 9, 1);

        Read(world)!.Zones.ShouldBeEmpty();
    }

    [Test]
    public void Read_NothingChangedSinceTheLastRead_ReturnsTheLastInstance()
    {
        EntityWorld world = new();
        world.Add("CCaptureFlag", 9, 1, Int("DT_CaptureFlag.m_nFlagStatus", 2));
        world.Add("CCaptureZone", 12, 1);
        SceneFlags first = Read(world)!;

        Read(world, first).ShouldBeSameAs(first);
    }

    [Test]
    public void Read_AFlagChangedSinceTheLastRead_IsANewInstance()
    {
        EntityWorld before = new();
        before.Add("CCaptureFlag", 9, 1, Int("DT_CaptureFlag.m_nFlagStatus", 0));
        EntityWorld after = new();
        after.Add("CCaptureFlag", 9, 1, Int("DT_CaptureFlag.m_nFlagStatus", 2));
        SceneFlags first = Read(before)!;

        SceneFlags second = Read(after, first)!;

        second.ShouldNotBeSameAs(first);
        second.Flags.ShouldHaveSingleItem().IsDropped.ShouldBeTrue();
    }

    [Test]
    public void Equals_TheZonesDiffer_IsFalse()
    {
        EntityWorld one = new();
        one.Add("CCaptureZone", 12, 1, Int("DT_BaseEntity.m_iTeamNum", 2));
        EntityWorld other = new();
        other.Add("CCaptureZone", 12, 1, Int("DT_BaseEntity.m_iTeamNum", 3));

        Read(one)!.Equals(Read(other)).ShouldBeFalse();
    }

    [Test]
    public void Equals_Null_IsFalse() => new SceneFlags([], []).Equals(null).ShouldBeFalse();

    [Test]
    public void GetHashCode_IsTheCombinationOfTheFlagAndZoneCounts()
    {
        EntityWorld world = new();
        world.Add("CCaptureFlag", 9, 1);
        world.Add("CCaptureZone", 12, 1);
        world.Add("CCaptureZone", 13, 1);

        Read(world)!.GetHashCode().ShouldBe(System.HashCode.Combine(1, 2));
    }

    [Test]
    public void ReturnProgress_HalfwayThroughTheResetWindow_IsOneHalf()
    {
        SceneFlag flag = new(1, 2, 2, false, false, 0, string.Empty, default, 0, 0f)
        {
            ResetTime = 115f,
            NeutralTime = 0f,
            MaxResetTime = 30f,
        };

        // 1 - (115 - 100) / 30.
        flag.ReturnProgress(100f).ShouldBe(0.5f);
    }

    [Test]
    public void ReturnProgress_ANeutralTimeLaterThanTheReset_UsesTheNeutralTime()
    {
        SceneFlag flag = new(1, 2, 2, false, false, 0, string.Empty, default, 0, 0f)
        {
            ResetTime = 0f,
            NeutralTime = 130f,
            MaxResetTime = 30f,
        };

        // 1 - (130 - 100) / 30.
        flag.ReturnProgress(100f).ShouldBe(0f);
    }

    [Test]
    public void ReturnProgress_ZeroMaxResetTime_DividesAsCFloatsDo()
    {
        SceneFlag flag = new(1, 2, 2, false, false, 0, string.Empty, default, 0, 0f) { ResetTime = 110f };

        float.IsNegativeInfinity(flag.ReturnProgress(100f)).ShouldBeTrue();
    }

    [Test]
    public void Status_HomeStolenDropped_AreZeroOneTwo()
    {
        SceneFlag home = new(1, 2, 0, false, false, 0, string.Empty, default, 0, 0f);
        SceneFlag dropped = home with { Status = 2 };

        (home.IsHome, home.IsStolen, home.IsDropped).ShouldBe((true, false, false));
        (dropped.IsHome, dropped.IsStolen, dropped.IsDropped).ShouldBe((false, false, true));
    }

    [Test]
    public void Center_BoundsInEntitySpaceOnARotatedZone_TurnWithTheEntity()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_CollisionProperty.m_nSolidType", SolidObb),
            Vector("DT_BaseEntity.m_vecOrigin", 100f, 200f, 300f),
            Vector("DT_BaseEntity.m_angRotation", 0f, 90f, 0f),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 0f, 0f));

        // The local centre (1, 0, 0) turned by yaw 90 is (0, 1, 0).
        (float x, float y, float z) = Read(world)!.Zones.ShouldHaveSingleItem().Center;

        x.ShouldBe(100f, Tolerance);
        y.ShouldBe(201f, Tolerance);
        z.ShouldBe(300f, Tolerance);
    }

    [Test]
    public void Center_ARotationWithPitchAndRoll_FollowsAngleMatrix()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_CollisionProperty.m_nSolidType", SolidObb),
            Vector("DT_BaseEntity.m_angRotation", 90f, 0f, 90f),
            Vector("DT_CollisionProperty.m_vecMins", 2f, 4f, 6f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 4f, 6f));

        // AngleMatrix with pitch 90, roll 90 applied to (2, 4, 6):
        // x = cp*cy*2 + (sr*sp*cy - cr*sy)*4 + (cr*sp*cy + sr*sy)*6 = 0 + 4 + 0 = 4
        // y = cp*sy*2 + (sr*sp*sy + cr*cy)*4 + (cr*sp*sy - sr*cy)*6 = 0 + 0 - 6 = -6
        // z = -sp*2 + sr*cp*4 + cr*cp*6 = -2.
        (float x, float y, float z) = Read(world)!.Zones.ShouldHaveSingleItem().Center;

        x.ShouldBe(4f, Tolerance);
        y.ShouldBe(-6f, Tolerance);
        z.ShouldBe(-2f, Tolerance);
    }

    [Test]
    public void Center_AnEntitySpaceZoneWithNoAnglesSent_IsNotTurned()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_CollisionProperty.m_nSolidType", SolidObb),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 0f, 0f));

        Read(world)!.Zones.ShouldHaveSingleItem().Center.ShouldBe((1f, 0f, 0f));
    }

    [Test]
    public void Center_AWorldAlignedSolid_IsNotTurned()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_CollisionProperty.m_nSolidType", SolidObb),
            Int("DT_CollisionProperty.m_usSolidFlags", ForceWorldAligned),
            Vector("DT_BaseEntity.m_angRotation", 0f, 90f, 0f),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 0f, 0f));

        Read(world)!.Zones.ShouldHaveSingleItem().Center.ShouldBe((1f, 0f, 0f));
    }

    [Test]
    public void Center_ABboxSolid_IsNotTurned()
    {
        EntityWorld world = new();
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_CollisionProperty.m_nSolidType", SolidBbox),
            Vector("DT_BaseEntity.m_angRotation", 0f, 90f, 0f),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 0f, 0f));

        Read(world)!.Zones.ShouldHaveSingleItem().Center.ShouldBe((1f, 0f, 0f));
    }

    [Test]
    public void Center_AParentedEntitySpaceZone_IsNotTurned()
    {
        EntityWorld world = new();
        world.Add("CTFPlayer", 5, 2);
        world.Add(
            "CCaptureZone",
            12,
            1,
            Int("DT_BaseEntity.moveparent", Handle(5, 2)),
            Int("DT_CollisionProperty.m_nSolidType", SolidObb),
            Vector("DT_BaseEntity.m_angRotation", 0f, 90f, 0f),
            Vector("DT_CollisionProperty.m_vecMins", 0f, 0f, 0f),
            Vector("DT_CollisionProperty.m_vecMaxs", 2f, 0f, 0f));

        Read(world)!.Zones.ShouldHaveSingleItem().Center.ShouldBe((1f, 0f, 0f));
    }
}
