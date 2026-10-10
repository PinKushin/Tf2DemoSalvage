using System;
using System.Linq;

namespace Tf2DemoSalvage.Render;

/// <summary>TF2's auto-exposure: the tone-map scale the main view's shaders multiply by (B514, D192).</summary>
/// <remarks>
/// **What the scale IS, read in disassembly**: <c>SetToneMappingScaleLinear</c> (<c>shaderapidx9.dll 0x180023be0</c>) loads
/// the current scale unchanged into <c>cLightScale.x</c> = <c>LINEAR_LIGHT_SCALE</c> under <c>HDR_TYPE_INTEGER</c>
/// (1 under none), with <c>cLightScale.y</c> 16 and <c>z</c> 16. So every <c>TONEMAP_SCALE_LINEAR</c> output is multiplied
/// by exactly <see cref="Current"/> — the viewer's <c>LinearLightScale</c>.
///
/// A port of <c>viewpostprocess.cpp</c> with <c>mat_tonemap_algorithm 1</c>, TF2's (only dod, cstrike, lostcoast and hl1
/// are forced to 0, `:836` — and its loop reads three of the four). Every number is Valve's:
///
/// - **The histogram**: sixteen luminance bins with edges <c>(i/16)^1.5</c>, the last capped bin counting everything
///   above 1 (`:426`, `:877-882`), over the centre of the view — <c>mat_exposure_center_region_x/y</c> 0.9 / 0.85 skip
///   5% and 7.5% at each edge (`:449-453`). Luminance is <c>luminance_compare_ps2x.fxc</c>'s 0.2125 0.7154 0.0721.
/// - **The target**: where the brightest <c>mat_tonemap_percent_bright_pixels</c> (2%) begin should sit at
///   <c>mat_tonemap_percent_target</c> (60%); if that border is already in the bin holding 60% the scale is held
///   (the "sticky bin"); a dark frame whose median sits under <c>mat_tonemap_min_avglum</c> (3%) is lifted to it
///   instead; and the frame was drawn at last frame's scale, so the result multiplies it (`:615-713`).
/// - **The range**: <c>mat_autoexposure_min/max</c> 0.5 / 2, unless an <c>env_tonemap_controller</c> set a positive
///   one (`GetExposureRange`, `:778-812`).
/// - **The goal**: a ten-entry history whose weighted average, clamped, is the goal (<c>SetToneMapScale</c>,
///   `:1130-1182`) — the weights are <c>|i - 5| / 5</c>, oldest and newest heaviest, as written.
///
/// - **The walk, read in disassembly** (<c>materialsystem.dll 0x180035fa0</c>, run once a frame): nothing at all when the
///   frame time is not positive — a paused demo holds its exposure; the rate is <c>mat_hdr_manual_tonemap_rate</c> (1),
///   doubled under <c>mat_tonemap_algorithm 1</c>; walking DOWN it is raised toward
///   <c>rate · mat_accelerate_adjust_exposure_down</c> (3) by <c>(current − goal) · (fast − rate) · ⅔</c> and capped
///   there; times the frame time, capped at 1/64 a frame under algorithm 1, then
///   <c>current = (1 − r) · current + r · goal</c>. Then <c>SetToneMappingScaleLinear( current )</c>.
/// - **The colour space the histogram reads, read from published source**: <c>dev/lumcompare</c> is
///   <c>screenspace_general</c> (its shipped VMT), whose sampler 0 reads sRGB unless <c>$linearread_basetexture</c>
///   — which the VMT does not set — or the target is 16-bit (`screenspace_general.cpp:124-132`). The integer-HDR frame
///   is eight-bit, so the histogram is of LINEAR light.
/// - **Resets** to 1 (<c>ResetToneMapping</c>, `:1121`): level init, the local player's respawn
///   (<c>c_tf_player.cpp:7946</c>), and the local player's observer target changing (<c>c_baseplayer.cpp:611-614</c>).
/// </remarks>
public sealed class AutoExposure
{
    /// <summary><c>N_LUMINANCE_RANGES_NEW - 1</c>: the bins that hold pixels.</summary>
    public const int Bins = 16;

    /// <summary><c>mat_tonemap_percent_target</c>.</summary>
    public const float PercentTarget = 60f;

    /// <summary><c>mat_tonemap_percent_bright_pixels</c>.</summary>
    public const float PercentBrightPixels = 2f;

    /// <summary><c>mat_tonemap_min_avglum</c>.</summary>
    public const float MinAverageLuminance = 3f;

    /// <summary><c>mat_autoexposure_min</c>.</summary>
    public const float DefaultMin = 0.5f;

    /// <summary><c>mat_autoexposure_max</c>.</summary>
    public const float DefaultMax = 2f;

    /// <summary><c>mat_exposure_center_region_x</c>.</summary>
    public const float CentreRegionX = 0.9f;

    /// <summary><c>mat_exposure_center_region_y</c>.</summary>
    public const float CentreRegionY = 0.85f;

    /// <summary><c>mat_hdr_manual_tonemap_rate</c>, doubled as <c>mat_tonemap_algorithm 1</c> doubles it.</summary>
    public const float Rate = 2f;

    /// <summary><c>mat_accelerate_adjust_exposure_down</c>.</summary>
    public const float AccelerateDown = 3f;

    /// <summary>The per-frame cap on the walk under <c>mat_tonemap_algorithm 1</c>.</summary>
    public const float MaximumStep = 1f / 64f;

    private const int History = 10;

    /// <summary>The seventeen bin edges, computed once: a counter over a million pixels cannot afford the powers.</summary>
    private static readonly float[] Edges = [.. System.Linq.Enumerable.Range(0, Bins + 1).Select(i => MathF.Pow(i / (float)Bins, 1.5f))];

    private readonly float[] _history = new float[History];

    private int _inHistory;

    /// <summary>The scale the shaders use now, <c>GetToneMappingScaleLinear().x</c>.</summary>
    public float Current { get; private set; } = 1f;

    /// <summary>The goal the current scale walks toward.</summary>
    public float Goal { get; private set; } = 1f;

    /// <summary>Whether the scale has reached the goal of a full history — what a still capture waits for (B520, D192).</summary>
    /// <remarks>Ours, not Valve's: the engine has no such question, because it is never asked for one settled frame.</remarks>
    public bool Settled => _inHistory == History && MathF.Abs(Current - Goal) < 0.002f;

    /// <summary>Lower edge of bin <paramref name="bin"/>, <c>m_min_lum</c>.</summary>
    /// <param name="bin">0 to 15.</param>
    /// <returns>The edge.</returns>
    public static float BinMin(int bin) => MathF.Pow(bin / (float)Bins, 1.5f);

    /// <summary>Upper edge of bin <paramref name="bin"/>, <c>m_max_lum</c>.</summary>
    /// <param name="bin">0 to 15.</param>
    /// <returns>The edge.</returns>
    public static float BinMax(int bin) => MathF.Pow((bin + 1) / (float)Bins, 1.5f);

    /// <summary>Adds one pixel to the histogram as the occlusion queries count it.</summary>
    /// <param name="counts">Sixteen counts.</param>
    /// <param name="luminance">The pixel's luminance as drawn.</param>
    /// <remarks>
    /// <c>step(min, L) * step(L, max)</c> is inclusive at both edges, so a pixel exactly on an edge counts in both
    /// bins, as the queries count it; the top bin's ceiling is 10000, so everything clipped lands there.
    /// </remarks>
    public static void Count(Span<int> counts, float luminance)
    {
        for (int bin = 0; bin < Bins; bin++)
        {
            float max = bin == Bins - 1 ? 10000f : Edges[bin + 1];

            if (luminance >= Edges[bin] && luminance <= max)
            {
                counts[bin]++;
            }
        }
    }

    /// <summary><c>FindLocationOfPercentBrightPixels</c>: the luminance where the brightest percentage begins.</summary>
    /// <param name="counts">Sixteen counts.</param>
    /// <param name="percentBright">The percentage, 0-100.</param>
    /// <param name="snapTarget">A target percentage to hold at if the border is in its bin, or negative for none.</param>
    /// <returns>The location, 0-1, or -1 for an empty histogram.</returns>
    public static float LocationOfPercentBrightPixels(ReadOnlySpan<int> counts, float percentBright, float snapTarget = -1f)
    {
        long total = 0;

        foreach (int count in counts[..Bins])
        {
            total += count;
        }

        if (total == 0)
        {
            return -1f;
        }

        float rangeTested = 0f;
        float pixelsTested = 0f;

        for (int bin = Bins - 1; bin >= 0; bin--)
        {
            float needed = (percentBright / 100f) - pixelsTested;
            float share = counts[bin] / (float)total;
            float range = BinMax(bin) - BinMin(bin);

            if (share >= needed)
            {
                if (snapTarget >= 0f && BinMin(bin) <= snapTarget / 100f && BinMax(bin) >= snapTarget / 100f)
                {
                    return snapTarget / 100f;
                }

                float border = 1f - (rangeTested + (range * (needed / share)));

                return Math.Clamp(border, BinMin(bin), BinMax(bin));
            }

            pixelsTested += share;
            rangeTested += range;
        }

        return -1f;
    }

    /// <summary><c>GetTargetTonemapScalar</c>: the scale this histogram asks for, before clamping.</summary>
    /// <param name="counts">Sixteen counts of a frame drawn at <paramref name="lastScale"/>.</param>
    /// <param name="lastScale">The scale the frame was drawn at.</param>
    /// <returns>The target.</returns>
    public static float TargetScalar(ReadOnlySpan<int> counts, float lastScale)
    {
        float location = LocationOfPercentBrightPixels(counts, PercentBrightPixels, PercentTarget);

        if (location < 0f)
        {
            location = PercentTarget / 100f;
        }

        float target = (PercentTarget / 100f) / MathF.Max(0.0001f, location);
        float average = LocationOfPercentBrightPixels(counts, 50f);

        if (average > 0f)
        {
            target = MathF.Max(target, (MinAverageLuminance / 100f) / average);
        }

        return MathF.Max(0.001f, target * lastScale);
    }

    /// <summary><c>GetExposureRange</c>: the cvars, unless a controller set a positive value.</summary>
    /// <param name="useMin">The controller overrides the minimum.</param>
    /// <param name="min">Its minimum.</param>
    /// <param name="useMax">The controller overrides the maximum.</param>
    /// <param name="max">Its maximum.</param>
    /// <returns>The range, min never above max.</returns>
    public static (float Min, float Max) Range(bool useMin, float min, bool useMax, float max)
    {
        float low = useMin && min > 0f ? min : DefaultMin;
        float high = useMax && max > 0f ? max : DefaultMax;

        return (low, MathF.Max(low, high));
    }

    /// <summary>One frame of <c>DoPreBloomTonemapping</c> and <c>SetToneMapScale</c>, then the walk toward the goal.</summary>
    /// <param name="counts">The histogram of the frame just drawn at <see cref="Current"/>.</param>
    /// <param name="range">The exposure range.</param>
    /// <param name="seconds">Seconds since the last update.</param>
    public void Update(ReadOnlySpan<int> counts, (float Min, float Max) range, float seconds)
    {
        float target = Math.Clamp(TargetScalar(counts, Current), range.Min, range.Max);
        target = MathF.Max(0.001f, target);

        Goal = target;

        if (_inHistory < History)
        {
            _history[_inHistory++] = target;
        }
        else
        {
            Array.Copy(_history, 1, _history, 0, History - 1);
            _history[^1] = target;
        }

        if (_inHistory == History)
        {
            float sum = 0f;
            float weights = 0f;

            for (int i = 0; i < History; i++)
            {
                float weight = Math.Abs(i - (History / 2)) * (1f / (History / 2));
                weights += weight;
                sum += weight * _history[i];
            }

            Goal = Math.Clamp(sum / weights, range.Min, range.Max);
        }

        Walk(seconds);
    }

    /// <summary>The material system's per-frame step of <see cref="Current"/> toward <see cref="Goal"/>.</summary>
    /// <param name="seconds">The frame time; zero or less holds the scale, as a paused game's does.</param>
    public void Walk(float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        float rate = Rate;

        if (Goal < Current)
        {
            float fast = Rate * AccelerateDown;
            rate = MathF.Min(fast, ((Current - Goal) * (fast - Rate) * (2f / 3f)) + Rate);
        }

        float step = Math.Clamp(MathF.Min(rate * seconds, MaximumStep), 0f, 1f);
        float next = ((1f - step) * Current) + (step * Goal);

        Current = float.IsFinite(next) ? next : Goal;
    }

    /// <summary><c>ResetToneMapping</c>: a cut — the history empties and the scale jumps.</summary>
    /// <param name="value">The scale.</param>
    public void Reset(float value)
    {
        _inHistory = 0;
        Current = value;
        Goal = value;
    }
}
