using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`C_TFPlayer::BRenderAsZombie` and `GetSkin`'s zombie step (c_tf_player.cpp:7751-7847), and the holiday behind it.</summary>
public sealed class ZombieSkinConformanceTests
{
    private const int Disguised = 1 << 3;

    private static ScenePlayer Player(int team, int playerClass, int? skinOverride, int conditions = 0) =>
        new(1, 0f, 0f, 0f, Team: team, Health: 100, PlayerClass: playerClass, Conditions: new PlayerConditions(conditions, 0, 0, 0, 0))
        {
            PlayerSkinOverride = skinOverride,
        };

    [Test]
    public void VisibleSkin_AZombieWithHalloweenVision_IsItsClassZombieSkin()
    {
        Disguise.VisibleSkin(Player(3, 3, 1), viewerHalloweenVision: true).ShouldBe(5, "BLU 1 + 4 (:7747)");
        Disguise.VisibleSkin(Player(2, 8, 1), viewerHalloweenVision: true).ShouldBe(22, "a spy +22 (:7739)");
        Disguise.VisibleSkin(Player(3, 3, 1), viewerHalloweenVision: false).ShouldBe(1, "only if the local player opts in (:7754)");
        Disguise.VisibleSkin(Player(3, 3, null), viewerHalloweenVision: true).ShouldBe(1, "no override");
    }

    [Test]
    public void VisibleSkin_AnEnemyDisguisedAsAZombie_UsesTheDisguisesOverrideAndClassWithNoMask()
    {
        ScenePlayer spy = Player(2, 8, null, Disguised) with
        {
            IsEnemy = true,
            DisguiseClass = 6,
            DisguiseTeam = 3,
            DisguiseSkinOverride = 1,
        };

        Disguise.VisibleSkin(spy, viewerHalloweenVision: true).ShouldBe(5, "disguise team BLU, heavy +4 (:7838-7846)");
        Disguise.VisibleSkin(spy with { IsEnemy = false }, viewerHalloweenVision: true).ShouldBe(0 + 4 + ((6 - 1) * 2), "teammates see the mask (:7765)");
    }

    [Test]
    public void IsHalloweenOrFullMoonActive_ForcedMapAndCalendar_FollowTfIsHolidayActive()
    {
        DateTime july = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Local);
        Dictionary<string, string> forced = new() { ["tf_forced_holiday"] = "2" };

        TfHolidays.IsHalloweenOrFullMoonActive(forced.GetValueOrDefault, 0, july).ShouldBeTrue("tf_forced_holiday Halloween (:1035)");
        TfHolidays.IsHalloweenOrFullMoonActive(new Dictionary<string, string> { ["tf_forced_holiday"] = "2", ["tf_force_holidays_off"] = "1" }.GetValueOrDefault, 0, july)
            .ShouldBeFalse("tf_force_holidays_off (:1032)");
        TfHolidays.IsHalloweenOrFullMoonActive(_ => null, 2, july).ShouldBeTrue("a Halloween map (:1048)");
        TfHolidays.CalendarHalloween(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Local)).ShouldBeTrue();
        TfHolidays.CalendarHalloween(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Local)).ShouldBeFalse();
        TfHolidays.CalendarFullMoon(new DateTime(2025, 10, 6, 12, 0, 0, DateTimeKind.Local)).ShouldBeTrue("the cycle's own start");
        TfHolidays.CalendarFullMoon(new DateTime(2025, 10, 20, 12, 0, 0, DateTimeKind.Local)).ShouldBeFalse("mid-cycle");
    }
}
