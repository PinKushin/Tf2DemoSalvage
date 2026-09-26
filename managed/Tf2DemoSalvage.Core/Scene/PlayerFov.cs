using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>`C_BasePlayer::GetFOV` (c_baseplayer.cpp:2435) during demo playback: the view's field of view.</summary>
/// <remarks>
/// Every branch of `C_BasePlayer::CalcView` ends with `fov = GetFOV()` — the player's own view, in-eye, chase, roaming,
/// freeze cam and `C_TFPlayer::CalcDeathCamView` (c_tf_player.cpp:7259) — and SourceTV's in-eye camera takes its
/// target's (`C_HLTVCamera::CalcInEyeCamView`, hltvcamera.cpp:316).
///
/// **Not modelled:** a vehicle's view FOV, which TF2 has no vehicle to set, and the prediction branch of the lerp, which a
/// demo's entities never take because playback does not predict.
/// </remarks>
public static class PlayerFov
{
    /// <summary>`CGameRules::DefaultFOV` (gamerules.h:148), which TF does not override.</summary>
    public const int RulesDefault = 90;

    /// <summary>`MAX_FOV` (shareddefs.h).</summary>
    public const int Max = 90;

    /// <summary>`demo_fov_override`'s clamp (c_baseplayer.cpp:2444).</summary>
    public const float OverrideMinimum = 10f;

    /// <summary>The field of view the player sees through.</summary>
    /// <param name="player">The player.</param>
    /// <param name="byIndex">Players by entity index, for the observer target.</param>
    /// <param name="isLocal">Whether it is the local player — the recorder — whose zoom alone is lerped.</param>
    /// <param name="demoOverride">`demo_fov_override`; zero or less for none.</param>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public static float Get(ScenePlayer player, Func<int, ScenePlayer?> byIndex, bool isLocal, float demoOverride, float curTime)
    {
        ArgumentNullException.ThrowIfNull(byIndex);

        if (demoOverride > 0f)
        {
            return Math.Clamp(demoOverride, OverrideMinimum, Max);
        }

        // "get fov from observer target. Not if target is observer itself"
        if (InEyeTarget(player, byIndex) is { } target)
        {
            return Get(target, byIndex, isLocal: false, demoOverride, curTime);
        }

        float fov = player.Fov is { } zoom and not 0 ? zoom : Default(player, byIndex);

        // The engine also asks `fFOV != m_iFOVStart`; when they are equal the lerp answers the same value, so it is left out.
        if (isLocal && player.FovStart is { } start && player.FovRate is > 0f and { } rate)
        {
            float delta = (curTime - (player.FovTime ?? 0f)) / rate;

            // Past the zoom time the engine latches `m_iFOVStart = fFOV` and stops lerping, which answers the same.
            if (delta < 1f)
            {
                fov = SimpleSplineRemapValClamped(delta, 0f, 1f, start, fov);
            }
        }

        return fov;
    }

    /// <summary>`GetDefaultFOV` (baseplayer_shared.cpp:1877): the in-eye target's, else `m_iDefaultFOV` or the rules', capped.</summary>
    /// <param name="player">The player.</param>
    /// <param name="byIndex">Players by entity index, for the observer target.</param>
    public static int Default(ScenePlayer player, Func<int, ScenePlayer?> byIndex)
    {
        ArgumentNullException.ThrowIfNull(byIndex);

        if (InEyeTarget(player, byIndex) is { } target)
        {
            return Default(target, byIndex);
        }

        return Math.Min(player.DefaultFov is { } own and not 0 ? own : RulesDefault, Max);
    }

    private static ScenePlayer? InEyeTarget(ScenePlayer player, Func<int, ScenePlayer?> byIndex) =>
        player.ObserverMode == ObserverModes.InEye
        && player.ObserverTarget is { } index
        && byIndex(index) is { } target
        && target.ObserverMode is null or ObserverModes.None
            ? target
            : null;

    // `SimpleSplineRemapValClamped` (mathlib.h): the clamped fraction through `SimpleSpline`, 3x² - 2x³.
    private static float SimpleSplineRemapValClamped(float value, float a, float b, float c, float d)
    {
        float fraction = Math.Clamp((value - a) / (b - a), 0f, 1f);
        float squared = fraction * fraction;

        return c + ((d - c) * ((3f * squared) - (2f * squared * fraction)));
    }
}
