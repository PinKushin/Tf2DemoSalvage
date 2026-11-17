using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One overhead icon this frame (B765).</summary>
/// <param name="Entity">The player wearing it.</param>
/// <param name="Name">Its particle system.</param>
/// <param name="From">The tick it was added, or the window's start when a seek met it already running.</param>
/// <param name="Index">Its place in `m_mapOverheadEffects`' walk.</param>
/// <param name="Count">How many he carries.</param>
public readonly record struct OverheadIconRun(int Entity, string Name, int From, int Index, int Count);

/// <summary>
/// The icons over players' heads, each as `C_TFPlayer::UpdateOverheadEffects` places it (B765). Which icons and in what
/// order is the timeline's (<see cref="ScenePlayer.OverheadEffects"/>); this is where they stand and since when.
/// </summary>
/// <remarks>
/// The engine is quoted in `OverheadIconsConformanceTests`. An icon is removed instantly (`RemoveOverheadEffect( ..., true )`),
/// so a run ends the tick its icon goes, with no fade. **Its start is the tick its effect was made**
/// (<see cref="OverheadIcon.Since"/>), so an icon made again — on entering dormancy, or for a new player in the slot —
/// starts afresh as the engine's new effect does; a seek replays at most <see cref="ReplayTicks"/>, two seconds, as the
/// overheal pluses do (`docs/memory/a-parity-rule-may-bound-the-cost.md`).
/// </remarks>
public sealed class OverheadIcons
{
    /// <summary>How far back a seek looks for an icon's start.</summary>
    public const int ReplayTicks = 132;

    /// <summary>`flEffectGap`.</summary>
    private const float Gap = 24f;

    /// <summary>`GetOverheadEffectPosition`'s lift above the eyes.</summary>
    private const float AboveEyes = 20f;

    /// <summary>`VEC_VIEW`'s height, for a class outside scout to engineer.</summary>
    private const float VecView = 72f;

    /// <summary>`TF_FIRST_NORMAL_CLASS` and `TF_LAST_NORMAL_CLASS`.</summary>
    private const int FirstNormalClass = 1, LastNormalClass = 9;

    private readonly List<OverheadIconRun> _runs = [];

    /// <summary>`GetClassEyeHeight`'s height (tf_player_shared.cpp:12729-12748).</summary>
    /// <param name="playerClass">`m_iClass`.</param>
    /// <param name="scale">`GetModelScale()`.</param>
    /// <returns>Units above his origin.</returns>
    public static float EyeHeight(int? playerClass, float scale) =>
        (playerClass is >= FirstNormalClass and <= LastNormalClass ? PlayerEye.Standing(playerClass.Value) : VecView) * scale;

    /// <summary>`UpdateOverheadEffects`' offset from his origin for one icon (c_tf_player.cpp:11616-11656).</summary>
    /// <param name="index">The icon's place in the walk.</param>
    /// <param name="count">How many he carries.</param>
    /// <param name="eyeHeight">His <see cref="EyeHeight"/>.</param>
    /// <param name="headToHead">His eyes less the local player's, across the ground; null with no local player.</param>
    /// <returns>The offset, in world axes.</returns>
    /// <remarks>
    /// Only the horizontal of `vecHeadToHead` reaches the result, as its cross product with up has no vertical part; a
    /// zero one normalises to zero, as `Vector::Normalized` does.
    /// </remarks>
    public static Vector3 Offset(int index, int count, float eyeHeight, Vector2? headToHead)
    {
        Vector3 over = new(0f, 0f, eyeHeight + AboveEyes);

        if (headToHead is not { } toHim)
        {
            return over;
        }

        Vector3 right = new(toHim.Y, -toHim.X, 0f);
        float length = right.Length();
        right = length > 0f ? right / length : Vector3.Zero;

        float first = -Gap * 0.5f * (count - 1);

        return over + ((first + (Gap * index)) * right);
    }

    /// <summary>Every icon shown at a tick, with its start: the players', then the dormant players'.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="players">The recorded players at the tick.</param>
    /// <param name="dormant">The dormant players at the tick, whose think keeps a rune and a mark (B765).</param>
    /// <returns>The icons, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<OverheadIconRun> At(
        int tick, IReadOnlyList<ScenePlayer> players, IReadOnlyList<SceneDormantPlayer> dormant)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(dormant);

        _runs.Clear();

        foreach (ScenePlayer player in players)
        {
            Report(tick, player.EntityIndex, player.OverheadEffects);
        }

        foreach (SceneDormantPlayer asleep in dormant)
        {
            Report(tick, asleep.EntityIndex, asleep.OverheadEffects);
        }

        return _runs;
    }

    private void Report(int tick, int entity, IReadOnlyList<OverheadIcon>? icons)
    {
        if (icons is null)
        {
            return;
        }

        for (int index = 0; index < icons.Count; index++)
        {
            OverheadIcon icon = icons[index];
            _runs.Add(new OverheadIconRun(entity, icon.Name, Math.Max(icon.Since, tick - ReplayTicks), index, icons.Count));
        }
    }
}
