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
/// so a run ends the tick its icon goes, with no fade. **Its start is kept, not recomputed**, so its particle is stepped
/// from the tick it began; a seek looks back at most <see cref="ReplayTicks"/>, two seconds, as the overheal pluses do
/// (`docs/memory/a-parity-rule-may-bound-the-cost.md`).
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

    private readonly Dictionary<(int Entity, string Name), int> _from = [];
    private readonly HashSet<(int Entity, string Name)> _seen = [];
    private readonly List<(int Entity, string Name)> _gone = [];
    private readonly List<OverheadIconRun> _runs = [];
    private int? _tick;

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

    /// <summary>Every icon shown at a tick, with its start.</summary>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="playersAt">The recorded players on a tick.</param>
    /// <returns>The icons, valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="playersAt"/> is null.</exception>
    public IReadOnlyList<OverheadIconRun> At(int tick, Func<int, IReadOnlyList<ScenePlayer>> playersAt)
    {
        ArgumentNullException.ThrowIfNull(playersAt);

        int from;

        if (_tick is { } last && tick >= last && tick - last <= ReplayTicks)
        {
            from = last + 1;
        }
        else
        {
            _from.Clear();
            from = tick - ReplayTicks;
        }

        for (int walked = from; walked <= tick; walked++)
        {
            Walk(walked, playersAt(walked));
        }

        _tick = tick;
        _runs.Clear();

        foreach (ScenePlayer player in playersAt(tick))
        {
            if (player.OverheadEffects is not { } names)
            {
                continue;
            }

            for (int index = 0; index < names.Count; index++)
            {
                _runs.Add(new OverheadIconRun(
                    player.EntityIndex, names[index], _from[(player.EntityIndex, names[index])], index, names.Count));
            }
        }

        return _runs;
    }

    /// <summary>One tick: each icon not seen before starts here; each no longer carried is forgotten.</summary>
    private void Walk(int tick, IReadOnlyList<ScenePlayer> players)
    {
        _seen.Clear();

        foreach (ScenePlayer player in players)
        {
            if (player.OverheadEffects is not { } names)
            {
                continue;
            }

            foreach (string name in names)
            {
                _seen.Add((player.EntityIndex, name));
                _from.TryAdd((player.EntityIndex, name), tick);
            }
        }

        _gone.Clear();

        foreach ((int, string) key in _from.Keys)
        {
            if (!_seen.Contains(key))
            {
                _gone.Add(key);
            }
        }

        foreach ((int, string) key in _gone)
        {
            _from.Remove(key);
        }
    }
}
