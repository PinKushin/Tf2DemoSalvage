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

    /// <summary>`tf_teammate_max_invis` (c_tf_player.cpp:1712), FCVAR_CHEAT | FCVAR_DEVELOPMENTONLY — never anything else.</summary>
    private const float TeammateMaxInvis = 0.95f;

    /// <summary>`OBS_MODE_DEATHCAM` (shareddefs.h).</summary>
    private const int DeathCam = 1;

    /// <summary>`OBS_MODE_FREEZECAM` (shareddefs.h).</summary>
    private const int FreezeCam = 2;

    /// <summary>`TF_VM_MIN_INVIS` / `TF_VM_MAX_INVIS` (tf_viewmodel.h).</summary>
    private const float ViewmodelMinInvis = 0.22f;

    private const float ViewmodelMaxInvis = 0.5f;

    /// <summary>`C_TFPlayer::GetEffectiveInvisibilityLevel` (c_tf_player.cpp:6844): the percent as the viewer is shown it.</summary>
    /// <param name="percent">`GetPercentInvisible()` — <see cref="Percent"/>.</param>
    /// <param name="isEnemy">`IsEnemyPlayer()`, against the recorder (false for a SourceTV viewer, who has no team).</param>
    /// <param name="entityIndex">This player's entity index.</param>
    /// <param name="recorderObserverMode">The local player's `GetObserverMode()`, or null.</param>
    /// <param name="recorderObserverTarget">The local player's observer target, by entity index, or null.</param>
    /// <param name="halloweenSpellStealth"><see cref="HalloweenSpellStealth"/>: capped whoever is watching.</param>
    /// <returns>0 visible through 1 invisible.</returns>
    /// <remarks>
    /// **Not ported, named:** the taunt stomp `taunt_attr_player_invis_percent` (an attribute on a taunt item), which
    /// does not reach a competitive or ordinary match.
    /// </remarks>
    public static float Effective(
        float percent,
        bool isEnemy,
        int entityIndex,
        int? recorderObserverMode,
        int? recorderObserverTarget,
        bool halloweenSpellStealth = false)
    {
        bool capped = !isEnemy || halloweenSpellStealth ||
            (recorderObserverMode is DeathCam or FreezeCam && recorderObserverTarget == entityIndex);

        return capped && percent > TeammateMaxInvis ? TeammateMaxInvis : percent;
    }

    /// <summary>`HALLOWEEN_SCENARIO_HIGHTOWER` (tf_gamerules.h).</summary>
    private const int Hightower = 4;

    /// <summary>`TF_COND_STEALTHED_USER_BUFF` (tf_shareddefs.h): the Halloween stealth spell.</summary>
    private const int StealthedUserBuff = 64;

    /// <summary>
    /// `bHalloweenSpellStealth` (c_tf_player.cpp:6849): `IsHalloweenScenario( HALLOWEEN_SCENARIO_HIGHTOWER ) &amp;&amp;
    /// InCond( TF_COND_STEALTHED_USER_BUFF )` — Valve's "crude way to limit Halloween spell".
    /// </summary>
    /// <param name="conditions">The cloaked player's conditions.</param>
    /// <param name="halloweenScenario">`m_halloweenScenario` (<see cref="SceneGameRules.HalloweenScenario"/>).</param>
    /// <returns>Whether the player is capped at the teammate maximum even for his enemies.</returns>
    public static bool HalloweenSpellStealth(PlayerConditions conditions, int halloweenScenario) =>
        halloweenScenario == Hightower && conditions.Has(StealthedUserBuff);

    /// <summary>`C_TFRagdoll::GetPercentInvisible` (c_tf_player.h): the corpse's own fade.</summary>
    /// <param name="cloaked">`m_bCloaked` — a Your Eternal Reward victim (tf_player.cpp:12707-12715).</param>
    /// <param name="secondsAlive">Client seconds since the corpse was created.</param>
    /// <returns>0 visible through 1 invisible.</returns>
    /// <remarks>
    /// `ClientThink` (c_tf_player.cpp:1392-1399), every frame (`CLIENT_THINK_ALWAYS`, :864): `if ( m_bCloaked &amp;&amp;
    /// m_flPercentInvisible &lt; 1.f ) m_flPercentInvisible += gpGlobals->frametime`, clamped to 1, from the zeroed
    /// allocation every client entity starts with. The sum of frame times is the time since creation (*interpolated*: the
    /// first think's frame time counts the frame the corpse was created in).
    /// </remarks>
    public static float Ragdoll(bool cloaked, float secondsAlive) =>
        cloaked ? Math.Clamp(secondsAlive, 0f, 1f) : 0f;

    /// <summary>`CInvisProxy::OnBind` for the LOCAL player (tf_viewmodel.cpp:575-594), the old `vm_invis` arithmetic.</summary>
    /// <param name="percent">`GetPercentInvisible()`.</param>
    /// <param name="blink">`InCond( TF_COND_STEALTHED_BLINK )`.</param>
    /// <param name="motionCloakDry">A motion-cloak watch with `GetSpyCloakMeter() &lt;= 0`.</param>
    /// <returns>The `$cloakfactor` the local player's own weapons and arms are drawn with.</returns>
    public static float LocalWeapon(float percent, bool blink, bool motionCloakDry)
    {
        // `( flPercentInvisible < 0.01 ) ? 0.0 : RemapVal( … )`, the literal a double as written.
        float invis = percent < 0.01 ? 0f : RemapVal(percent, 0f, 1f, ViewmodelMinInvis, ViewmodelMaxInvis);

        return blink || motionCloakDry ? 0.3f : invis;
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
