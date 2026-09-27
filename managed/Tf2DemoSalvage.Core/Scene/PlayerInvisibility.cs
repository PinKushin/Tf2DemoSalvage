using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>`CTFPlayerShared::GetPercentInvisible` (tf_player_shared.cpp:8047): `m_flInvisibility`, as `InvisibilityThink` sets it.</summary>
/// <remarks>
/// `m_flInvisibility` is not networked; every client derives it each think from networked state — the conditions,
/// `m_flInvisChangeCompleteTime`, `m_flCloakMeter` — and `curtime`. `InvisibilityThink` (:7977) keeps no state it reads back
/// (`m_flPrevInvisibility` is written, never read here), so it is a function of those inputs.
/// </remarks>
public static class PlayerInvisibility
{
    /// <summary>`TF_CLASS_SPY` (tf_shareddefs.h).</summary>
    private const int Spy = 8;

    /// <summary>`TF_COND_STEALTHED_BLINK` (tf_shareddefs.h:699).</summary>
    private const int StealthedBlink = 9;

    /// <summary>`TF_SPY_STEALTH_BLINKSCALE` (tf_player_shared.cpp:215).</summary>
    private const float BlinkScale = 0.85f;

    /// <summary>`InvisibilityThink` (tf_player_shared.cpp:7977-8040), returning what it stores in `m_flInvisibility`.</summary>
    /// <param name="player">The player.</param>
    /// <param name="curTime">`gpGlobals->curtime`, on the clock `m_flInvisChangeCompleteTime` is stamped with.</param>
    /// <param name="motionCloak">
    /// `m_bMotionCloak`: latched by `OnAddStealthed` (:7022) from the first `TF_WEAPON_INVIS` among the weapons —
    /// `HasMotionCloak`, `set_weapon_mode` 2 (tf_weapon_invis.h:68).
    /// </param>
    /// <returns>0 visible through 1 invisible.</returns>
    public static float Percent(ScenePlayer player, float curTime, bool motionCloak)
    {
        PlayerConditions cond = player.Conditions;

        // "Shouldn't happen, but it's a safety net" (:7979).
        if (player.PlayerClass != Spy && cond.Has(PlayerConditions.Stealthed))
        {
            return 0f;
        }

        float target;
        float scale = cond.Has(StealthedBlink) || cond.Has(PlayerConditions.Urine) ? BlinkScale : 1f;
        float complete = player.InvisChangeCompleteTime ?? 0f;

        if (complete > curTime)
        {
            target = cond.IsStealthed ? 1f - (complete - curTime) : (complete - curTime) * 0.5f;
        }
        else if (cond.IsStealthed)
        {
            target = 1f;

            if (motionCloak)
            {
                if ((player.CloakMeter ?? 0f) == 0f)
                {
                    (float x, float y, float z) = player.Velocity ?? (0f, 0f, 0f);
                    float maxSpeed = player.MaxSpeed ?? 0f;

                    target = RemapVal((x * x) + (y * y) + (z * z), 0f, maxSpeed * maxSpeed, 1f, 0.5f);
                }
                else
                {
                    target = 1f;
                }
            }
        }
        else
        {
            target = 0f;
        }

        return Math.Clamp(target * scale, 0f, 1f);
    }

    /// <summary>`RemapVal` (mathlib/mathlib.h:612): unclamped; a zero-width input range answers by which side `val` is on.</summary>
    private static float RemapVal(float value, float a, float b, float c, float d)
    {
        // The engine's own exact `A == B`, which a tolerance would change.
#pragma warning disable S1244
        if (a == b)
#pragma warning restore S1244
        {
            return value >= b ? d : c;
        }

        return c + ((d - c) * (value - a) / (b - a));
    }
}
