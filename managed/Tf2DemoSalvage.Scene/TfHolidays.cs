using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// `TF_IsHolidayActive( eHoliday )` (tf_gamerules.cpp:1030-1060) and the calendar it falls back to,
/// `EconHolidays_IsHolidayActive` (econ_holidays.cpp).
/// </summary>
/// <remarks>
/// The calendar is the WATCHER's clock: `UTIL_CalculateHolidays` reads `CRTime::RTime32TimeCur()` once
/// (util_shared.cpp:1351-1365), so a demo watched in October plays with Halloween on, as the game does.
/// </remarks>
public static class TfHolidays
{
    /// <summary>`kHoliday_Halloween` (econ_item_constants.h:970).</summary>
    public const int Halloween = 2;

    /// <summary>`kHoliday_Valentines`.</summary>
    public const int Valentines = 6;

    /// <summary>`kHoliday_FullMoon`.</summary>
    public const int FullMoon = 8;

    /// <summary>`kHoliday_HalloweenOrFullMoon`.</summary>
    public const int HalloweenOrFullMoon = 9;

    /// <summary>`kHoliday_HalloweenOrFullMoonOrValentines`.</summary>
    public const int HalloweenOrFullMoonOrValentines = 10;

    /// <summary>`kHoliday_TFBirthday`.</summary>
    public const int TFBirthday = 1;

    /// <summary>`TF_IsHolidayActive` for the Halloween-or-full-moon holiday, the one the vision filter asks (c_tf_player.cpp:8092).</summary>
    /// <param name="serverConVar">The demo's replicated cvars: `tf_forced_holiday`, `tf_item_based_forced_holiday`, `tf_force_holidays_off`.</param>
    /// <param name="mapHolidayType">`m_nMapHolidayType` (tf_gamerules.h:595), 0 when unsent.</param>
    /// <param name="now">The watcher's local clock.</param>
    /// <returns>Whether it is active.</returns>
    public static bool IsHalloweenOrFullMoonActive(Func<string, string?> serverConVar, int mapHolidayType, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(serverConVar);

        if (Int(serverConVar("tf_force_holidays_off")) != 0)
        {
            return false;
        }

        if (CvarIndicates(Int(serverConVar("tf_forced_holiday"))) || CvarIndicates(Int(serverConVar("tf_item_based_forced_holiday"))))
        {
            return true;
        }

        // `IsHolidayMap( kHoliday_Halloween )` / `( kHoliday_FullMoon )` (:1046-1052), then `IsHolidayMap( eHoliday )`.
        if (mapHolidayType is Halloween or FullMoon or HalloweenOrFullMoon)
        {
            return true;
        }

        return CalendarHalloween(now) || CalendarFullMoon(now);
    }

    /// <summary>`BIsCvarIndicatingHolidayIsActive( value, kHoliday_HalloweenOrFullMoon )` (tf_gamerules.cpp:1002).</summary>
    private static bool CvarIndicates(int value) =>
        value is Halloween or FullMoon or HalloweenOrFullMoon or HalloweenOrFullMoonOrValentines;

    /// <summary>`g_Holiday_Halloween( "halloween", "10-01", "11-08" )` (econ_holidays.cpp): start and end inclusive, to the second.</summary>
    public static bool CalendarHalloween(DateTime now) =>
        now >= new DateTime(now.Year, 10, 1, 0, 0, 0, DateTimeKind.Local) && now <= new DateTime(now.Year, 11, 8, 0, 0, 0, DateTimeKind.Local);

    /// <summary>
    /// `g_Holiday_FullMoon( "fullmoon", 10, 06, 2025, 29.53f, 1.0f )` — a `CCyclicalHoliday`: within a day either side of
    /// each 29.53-day cycle from 2025-10-06 local midnight, in whole seconds as its int arithmetic does.
    /// </summary>
    public static bool CalendarFullMoon(DateTime now)
    {
        const int SecondsPerDay = 24 * 60 * 60;
        const int CycleLength = (int)(29.53f * SecondsPerDay);
        const int Buffer = (int)(1.0f * SecondsPerDay);

        long initial = new DateTimeOffset(new DateTime(2025, 10, 6, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeSeconds();
        long current = new DateTimeOffset(now).ToUnixTimeSeconds();

        // `int iSecondsIntoCycle = ( RTime32 - time_t ) % int`: a 64-bit signed difference (time_t), its remainder cut to int.
        int intoCycle = (int)((current - initial) % CycleLength);

        return intoCycle < Buffer || intoCycle > CycleLength - Buffer;
    }

    private static int Int(string? value) =>
        value is null ? 0 : (int)Hud.PanelLayout.Atof(value);
}
