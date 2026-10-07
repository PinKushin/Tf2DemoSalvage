using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>TF_IsHolidayActive( kHoliday_Christmas )</c> (`tf_gamerules.cpp:1030-1060`), which <c>CTFGameRules::IsHolidayActive</c>
/// returns (`:17110-17116`) and the rope manager asks (`c_rope.cpp:662`) (B478).
/// </summary>
public sealed class TfHolidaysChristmasConformanceTests
{
    /// <remarks>
    /// **The calendar is two dated holidays OR-ed**: <c>"christmas1"</c> 12-01 to 12-31 23:59:59 and <c>"christmas2"</c>
    /// 01-01 to 01-08 (`econ_holidays.cpp:311-313`), each end inclusive to the second as the Halloween one is read.
    /// </remarks>
    [TestCase(2026, 11, 30, 23, 59, 59, false)]
    [TestCase(2026, 12, 1, 0, 0, 0, true)]
    [TestCase(2026, 12, 31, 23, 59, 59, true)]
    [TestCase(2027, 1, 1, 0, 0, 0, true)]
    [TestCase(2027, 1, 8, 0, 0, 0, true)]
    [TestCase(2027, 1, 8, 0, 0, 1, false)]
    [TestCase(2026, 7, 4, 12, 0, 0, false)]
    public void IsChristmasActive_OnTheCalendar_IsDecemberToTheEighthOfJanuary(
        int year, int month, int day, int hour, int minute, int second, bool active)
    {
        TfHolidays.IsChristmasActive(_ => null, 0, new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local))
            .ShouldBe(active);
    }

    /// <remarks>
    /// **Off forces off, a forced holiday of exactly 3 forces on, and so does a Christmas map** — in that order
    /// (`tf_gamerules.cpp:1032-1056`); <c>BIsCvarIndicatingHolidayIsActive</c>'s Christmas case is equality alone (`:998`).
    /// </remarks>
    [TestCase("tf_force_holidays_off", "1", 3, false)]
    [TestCase("tf_forced_holiday", "3", 0, true)]
    [TestCase("tf_item_based_forced_holiday", "3", 0, true)]
    [TestCase("tf_forced_holiday", "9", 0, false)]
    [TestCase("none", "0", 3, true)]
    public void IsChristmasActive_InJuly_FollowsTheCvarsAndTheMap(string cvar, string value, int mapHoliday, bool active)
    {
        TfHolidays.IsChristmasActive(
                name => name == cvar ? value : null,
                mapHoliday,
                new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Local))
            .ShouldBe(active);
    }
}
