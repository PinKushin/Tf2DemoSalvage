using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// A choreography ramp's value at a time — <c>CCurveData::GetIntensity</c> (<c>choreoevent.cpp:1603</c>) and the
/// interpolators it calls (<c>interpolatortypes.cpp:194</c>, <c>mathlib_base.cpp:2211-2792</c>), B513.
/// </summary>
/// <remarks>
/// **Every spline accumulates in the SDK's order**, term by term into an output that starts at zero, because the
/// float result of a sum depends on its order and the point of a port is the same number.
/// </remarks>
public static class ChoreoCurve
{
    /// <summary><c>CURVE_CATMULL_ROM_TO_CATMULL_ROM</c> — an event's and a scene's default (<c>choreoevent.cpp:1294</c>,
    /// <c>choreoscene.cpp:3668</c>).</summary>
    public const int CatmullRomToCatmullRom = 0x0101;

    private const int Default = 0;
    private const int CatmullRomNormalizeX = 1;
    private const int EaseIn = 2;
    private const int EaseOut = 3;
    private const int EaseInOut = 4;
    private const int BSplineType = 5;
    private const int Linear = 6;
    private const int KochanekBartels = 7;
    private const int KochanekBartelsEarly = 8;
    private const int KochanekBartelsLate = 9;
    private const int SimpleCubic = 10;
    private const int CatmullRom = 11;
    private const int CatmullRomNormalize = 12;
    private const int CatmullRomTangent = 13;
    private const int ExponentialDecayType = 14;
    private const int Hold = 15;

    /// <summary><c>M_PI</c>, a double, as the ease interpolators use it.</summary>
    private const double Pi = 3.14159265358979323846;

    /// <summary><c>CCurveData::GetIntensity</c>: the ramp's value at <paramref name="time"/> seconds into its owner.</summary>
    /// <param name="ramp">The samples; empty is full intensity.</param>
    /// <param name="time">Seconds into the event (or scene).</param>
    /// <param name="duration">The owner's duration — where the ramp's closing edge sits.</param>
    /// <param name="hasEndTime">Whether the owner has an end; a one-shot event's ramp is zero.</param>
    /// <param name="defaultCurve">What a sample of <c>CURVE_DEFAULT</c> means.</param>
    /// <returns>0 to 1.</returns>
    /// <remarks>
    /// **The binary restore carries no edge info**, so both edges are inactive: the out-of-range samples are value
    /// 0 with <c>CURVE_DEFAULT</c> at time 0 and at the duration (<c>choreoevent.cpp:3558</c>, <c>:4088-4108</c>).
    /// </remarks>
    public static float Intensity(
        IReadOnlyList<SceneCurveSample> ramp, float time, float duration, bool hasEndTime,
        int defaultCurve = CatmullRomToCatmullRom)
    {
        ArgumentNullException.ThrowIfNull(ramp);

        if (!hasEndTime)
        {
            return 0f;
        }

        int count = ramp.Count;

        if (count < 1)
        {
            return 1f;
        }

        (float Time, float Value) start = default;
        (float Time, float Value) end = default;

        // The SDK's own search, kept as it is: it probes outward from the middle and stops at the first bracket.
        int j = Math.Max(count / 2, 1);
        int i = j;

        while (i > -2 && i < count + 1)
        {
            start = Bounded(ramp, i, duration, out _);
            end = Bounded(ramp, i + 1, duration, out _);

            j = Math.Max(j / 2, 1);

            if (time < start.Time)
            {
                i -= j;
            }
            else if (time > end.Time)
            {
                i += j;
            }
            else
            {
                break;
            }
        }

        int previous = Math.Max(-1, i - 1);
        int next = Math.Min(i + 2, count);

        (float Time, float Value) pre = Bounded(ramp, previous, duration, out bool clampedPre);
        (float Time, float Value) after = Bounded(ramp, next, duration, out bool clampedNext);

        (float X, float Y) vPre = (clampedPre ? start.Time : pre.Time, pre.Value);
        (float X, float Y) vNext = (clampedNext ? end.Time : after.Time, after.Value);

        float dt = end.Time - start.Time;
        float f2 = dt > 0f ? (time - start.Time) / dt : 0f;
        f2 = Math.Clamp(f2, 0f, 1f);

        // Ramp samples are never given a curve type, so both are CURVE_DEFAULT and take the owner's default.
        int startCurve = defaultCurve;
        int endCurve = defaultCurve;

        return Clamp01(Between(
            startCurve, endCurve, vPre, (start.Time, start.Value), (end.Time, end.Value), vNext, f2));
    }

    /// <summary><c>CFlexAnimationTrack::GetIntensity( time, side )</c> (<c>choreoevent.cpp:882</c>), B513.</summary>
    /// <param name="track">The track.</param>
    /// <param name="start">The event's start, scene seconds.</param>
    /// <param name="end">Its end.</param>
    /// <param name="time">Scene seconds now.</param>
    /// <param name="side">0 for the left (and a mono track), 1 for the right.</param>
    /// <returns>The controller value, in the track's own range.</returns>
    public static float TrackIntensity(SceneFlexTrack track, float start, float end, float time, int side)
    {
        ArgumentNullException.ThrowIfNull(track);

        float magnitude = TrackInternal(track, start, end, time, 0);
        float scale = 1f;

        if (track.Combo)
        {
            float balance = TrackInternal(track, start, end, time, 1);

            if (side == 0 && balance > 0.5f)
            {
                scale = (1f - balance) / 0.5f;
            }
            else if (side == 1 && balance < 0.5f)
            {
                scale = balance / 0.5f;
            }
        }

        return magnitude * scale;
    }

    /// <summary><c>GetIntensityInternal</c>, <c>choreoevent.cpp:696</c>.</summary>
    private static float TrackInternal(SceneFlexTrack track, float start, float end, float time, int type)
    {
        float value;

        // Left edge before, right edge after — the same value with no edge info stored.
        if (time < start || time > end)
        {
            value = TrackZero(track, type);
        }
        else
        {
            value = TrackFraction(track, end - start, time - start, type);
        }

        // S1244: the SDK's own guard, `m_flMin != m_flMax`.
#pragma warning disable S1244
        if (type == 0 && track.Min != track.Max)
#pragma warning restore S1244
        {
            value = (value * (track.Max - track.Min)) + track.Min;
        }

        return value;
    }

    /// <summary><c>GetZeroValue</c> with no edge info (the binary restore sets none): 0.5 for the balance curve, else
    /// <c>GetDefaultEdgeZeroPos</c> — where zero sits in the track's range.</summary>
    private static float TrackZero(SceneFlexTrack track, int type)
    {
        if (type == 1)
        {
            return 0.5f;
        }

#pragma warning disable S1244 // `m_flMin != m_flMax`, as the SDK writes it.
        return track.Min != track.Max ? (0f - track.Min) / (track.Max - track.Min) : 0f;
#pragma warning restore S1244
    }

    /// <summary><c>GetFracIntensity</c>, <c>choreoevent.cpp:731</c>: its own search (with the <c>time == end</c> step),
    /// curve types taken as stored (15 bits), no <c>CURVE_DEFAULT</c> substitution and no clamped-X adjustment.</summary>
    private static float TrackFraction(SceneFlexTrack track, float duration, float time, int type)
    {
        IReadOnlyList<SceneFlexSample> samples = type == 0 ? track.Samples : track.Balance;
        float zero = TrackZero(track, type);
        int count = samples.Count;

        if (count < 1)
        {
            return zero;
        }

        (float Time, float Value, int Curve) Bounded(int index)
        {
            if (index < 0)
            {
                return (0f, zero, 0);
            }

            if (index >= count)
            {
                return (duration, zero, 0);
            }

            return (samples[index].Time, samples[index].Value, samples[index].CurveType & 0x7FFF);
        }

        (float Time, float Value, int Curve) start = default;
        (float Time, float Value, int Curve) end = default;

        int j = Math.Max(count / 2, 1);
        int i = j;

        while (i > -2 && i < count + 1)
        {
            start = Bounded(i);
            end = Bounded(i + 1);
            j = Math.Max(j / 2, 1);

            if (time < start.Time)
            {
                i -= j;
            }
            else if (time > end.Time)
            {
                i += j;
            }
            else
            {
#pragma warning disable S1244 // `time == esEnd->time`, the SDK's exact compare.
                if (time == end.Time)
#pragma warning restore S1244
                {
                    ++i;
                    start = Bounded(i);
                    end = Bounded(i + 1);
                }

                break;
            }
        }

        (float Time, float Value, int Curve) pre = Bounded(Math.Max(-1, i - 1));
        (float Time, float Value, int Curve) next = Bounded(Math.Min(i + 2, count));

        float dt = end.Time - start.Time;
        float f2 = dt > 0f ? (time - start.Time) / dt : 0f;
        f2 = Math.Clamp(f2, 0f, 1f);

        return Clamp01(Between(
            start.Curve, end.Curve, (pre.Time, pre.Value), (start.Time, start.Value), (end.Time, end.Value),
            (next.Time, next.Value), f2));
    }

    /// <summary>The part of <c>GetIntensity</c> shared with a flex track: the hold rules, then one or two curves.</summary>
    /// <param name="startCurve">The start sample's curve type; its RIGHT (outbound) interpolator is used.</param>
    /// <param name="endCurve">The end sample's; its LEFT (inbound) interpolator is used.</param>
    /// <param name="pre">The sample before the start.</param>
    /// <param name="start">The sample at or before the time.</param>
    /// <param name="end">The sample after it.</param>
    /// <param name="next">The one after that.</param>
    /// <param name="fraction">How far from start to end, 0 to 1.</param>
    /// <returns>The curve's Y, unclamped.</returns>
    public static float Between(
        int startCurve,
        int endCurve,
        (float X, float Y) pre,
        (float X, float Y) start,
        (float X, float Y) end,
        (float X, float Y) next,
        float fraction)
    {
        // `Interpolator_CurveInterpolatorsForType( start, dummy, earlypart )` takes GET_RIGHT_CURVE of the start, and
        // `( end, laterpart, dummy )` GET_LEFT_CURVE of the end (interpolatortypes.h:42-43, :147-151).
        int early = startCurve & 0xFF;
        int later = (endCurve >> 8) & 0xFF;

        if (early == Hold)
        {
            return start.Y;
        }

        if (later == Hold)
        {
            return end.Y;
        }

        if (early == later)
        {
            return Interpolate(later, pre, start, end, next, fraction).Y;
        }

        (float X, float Y) one = Interpolate(early, pre, start, end, next, fraction);
        (float X, float Y) two = Interpolate(later, pre, start, end, next, fraction);

        return one.Y + ((two.Y - one.Y) * fraction);
    }

    /// <summary><c>Interpolator_CurveInterpolate</c> in two dimensions — X is time, Y the value.</summary>
    /// <param name="type">An <c>INTERPOLATE_*</c>; an unknown one falls through to the default, as the SDK's does.</param>
    /// <param name="p1">The sample before.</param>
    /// <param name="p2">The start.</param>
    /// <param name="p3">The end.</param>
    /// <param name="p4">The sample after.</param>
    /// <param name="f">0 to 1.</param>
    /// <returns>The point on the curve.</returns>
    public static (float X, float Y) Interpolate(
        int type, (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4, float f)
    {
        switch (type)
        {
            case CatmullRom:
                return CatmullRomSpline(p1, p2, p3, p4, f);
            case CatmullRomNormalize:
                return CatmullRomNormalized(p1, p2, p3, p4, f);
            case CatmullRomTangent:
                return CatmullRomTangentOf(p1, p2, p3, p4, f);
            case EaseIn:
                return LerpPoint(p2, p3, (float)Math.Sin(Pi * f * 0.5f));
            case EaseOut:
                return LerpPoint(p2, p3, (float)(1.0f - Math.Sin((Pi * f * 0.5f) + (0.5f * Pi))));
            case EaseInOut:
                {
                    float squared = f * f;
                    return LerpPoint(p2, p3, (3 * squared) - (2 * squared * f));
                }

            case Linear:
                return LerpPoint(p2, p3, f);
            case KochanekBartels:
                return KochanekBartelsNormalizeX(0.77f, 0f, 0.77f, p1, p2, p3, p4, f);
            case KochanekBartelsEarly:
                return KochanekBartelsNormalizeX(0.77f, -1f, 0.77f, p1, p2, p3, p4, f);
            case KochanekBartelsLate:
                return KochanekBartelsNormalizeX(0.77f, 1f, 0.77f, p1, p2, p3, p4, f);
            case SimpleCubic:
                // Cubic_Spline_NormalizeX normalises p1 and p4 and then gives them no weight.
                return CubicSpline(p2, p3, f);

            case BSplineType:
                return BSpline(p1, p2, p3, p4, f);
            case ExponentialDecayType:
                {
                    float dt = p3.X - p2.X;

                    if (dt <= 0f)
                    {
                        return (0f, p2.Y);
                    }

                    // `1.0f - ExponentialDecay( 0.001, dt, f * dt )` = 1 - expf( logf( 0.001 ) / dt * ( f * dt ) ).
                    float value = 1f - MathF.Exp(MathF.Log(0.001f) / dt * (f * dt));
                    return (0f, p2.Y + (value * (p3.Y - p2.Y)));
                }

            case Hold:
                return (0f, p2.Y);
            case Default:
            case CatmullRomNormalizeX:
            default:
                {
                    ((float X, float Y) a, (float X, float Y) d) = Normalize(p1, p2, p3, p4);
                    return CatmullRomSpline(a, p2, p3, d, f);
                }
        }
    }

    /// <summary><c>Spline_Normalize</c>: stretch the outer points so their X spacing matches the middle span's.</summary>
    private static ((float X, float Y) P1, (float X, float Y) P4) Normalize(
        (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4)
    {
        float dt = p3.X - p2.X;
        (float X, float Y) p1n = p1;
        (float X, float Y) p4n = p4;

        // S1244 is a false positive: these exact comparisons are the SDK's own guards (mathlib_base.cpp:2224-2231).
#pragma warning disable S1244
        if (dt != 0.0)
        {
            if (p1.X != p2.X)
            {
                p1n = LerpPoint(p2, p1, dt / (p2.X - p1.X));
            }

            if (p4.X != p3.X)
            {
                p4n = LerpPoint(p3, p4, dt / (p4.X - p3.X));
            }
        }
#pragma warning restore S1244

        return (p1n, p4n);
    }

    /// <summary><c>Catmull_Rom_Spline</c>, summed row by row in the SDK's order.</summary>
    private static (float X, float Y) CatmullRomSpline(
        (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4, float t)
    {
        float tSqr = t * t * 0.5f;
        float tSqrSqr = t * tSqr;
        t *= 0.5f;

        return Sum(
            (p1, -tSqrSqr), (p2, tSqrSqr * 3), (p3, tSqrSqr * -3), (p4, tSqrSqr),
            (p1, tSqr * 2), (p2, tSqr * -5), (p3, tSqr * 4), (p4, -tSqr),
            (p1, -t), (p3, t),
            (p2, 1f));
    }

    /// <summary><c>Catmull_Rom_Spline_Tangent</c>.</summary>
    private static (float X, float Y) CatmullRomTangentOf(
        (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4, float t)
    {
        float tOne = 3 * t * t * 0.5f;
        float tTwo = 2 * t * 0.5f;
        const float tThree = 0.5f;

        return Sum(
            (p1, -tOne), (p2, tOne * 3), (p3, tOne * -3), (p4, tOne),
            (p1, tTwo * 2), (p2, tTwo * -5), (p3, tTwo * 4), (p4, -tTwo),
            (p1, -tThree), (p3, tThree));
    }

    /// <summary><c>Catmull_Rom_Spline_Normalize</c>: the outer points pulled to the middle span's LENGTH.</summary>
    private static (float X, float Y) CatmullRomNormalized(
        (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4, float t)
    {
        float dt = MathF.Sqrt(((p3.X - p2.X) * (p3.X - p2.X)) + ((p3.Y - p2.Y) * (p3.Y - p2.Y)));

        (float X, float Y) p1n = Unit((p1.X - p2.X, p1.Y - p2.Y));
        (float X, float Y) p4n = Unit((p4.X - p3.X, p4.Y - p3.Y));

        p1n = (p2.X + (dt * p1n.X), p2.Y + (dt * p1n.Y));
        p4n = (p3.X + (dt * p4n.X), p3.Y + (dt * p4n.Y));

        return CatmullRomSpline(p1n, p2, p3, p4n, t);
    }

    /// <summary><c>Kochanek_Bartels_Spline_NormalizeX</c>.</summary>
    private static (float X, float Y) KochanekBartelsNormalizeX(
        float tension,
        float bias,
        float continuity,
        (float X, float Y) p1,
        (float X, float Y) p2,
        (float X, float Y) p3,
        (float X, float Y) p4,
        float t)
    {
        ((float X, float Y) a, (float X, float Y) d) = Normalize(p1, p2, p3, p4);

        float ffa = (1.0f - tension) * (1.0f + continuity) * (1.0f + bias);
        float ffb = (1.0f - tension) * (1.0f - continuity) * (1.0f - bias);
        float ffc = (1.0f - tension) * (1.0f - continuity) * (1.0f + bias);
        float ffd = (1.0f - tension) * (1.0f + continuity) * (1.0f - bias);

        float tSqr = t * t * 0.5f;
        float tSqrSqr = t * tSqr;
        t *= 0.5f;

        return Sum(
            (a, tSqrSqr * -ffa), (p2, tSqrSqr * (4.0f + ffa - ffb - ffc)),
            (p3, tSqrSqr * (-4.0f + ffb + ffc - ffd)), (d, tSqrSqr * ffd),
            (a, tSqr * 2 * ffa), (p2, tSqr * (-6 - (2 * ffa) + (2 * ffb) + ffc)),
            (p3, tSqr * (6 - (2 * ffb) - ffc + ffd)), (d, tSqr * -ffd),
            (a, t * -ffa), (p2, t * (ffa - ffb)), (p3, t * ffb),
            (p2, 1f));
    }

    /// <summary><c>Cubic_Spline</c>: the outer points carry no weight once normalised.</summary>
    private static (float X, float Y) CubicSpline((float X, float Y) p2, (float X, float Y) p3, float t)
    {
        float tSqr = t * t;
        float tSqrSqr = t * tSqr;

        return Sum((p2, tSqrSqr * 2), (p3, tSqrSqr * -2), (p2, tSqr * -3), (p3, tSqr * 3), (p2, 1f));
    }

    /// <summary><c>BSpline</c> — called un-normalised (<c>interpolatortypes.cpp:299</c>).</summary>
    private static (float X, float Y) BSpline(
        (float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, (float X, float Y) p4, float t)
    {
        const float oneOver6 = 1.0f / 6.0f;

        float tSqr = t * t * oneOver6;
        float tSqrSqr = t * tSqr;
        t *= oneOver6;

        return Sum(
            (p1, -tSqrSqr), (p2, tSqrSqr * 3.0f), (p3, tSqrSqr * -3.0f), (p4, tSqrSqr),
            (p1, tSqr * 3.0f), (p2, tSqr * -6.0f), (p3, tSqr * 3.0f),
            (p1, t * -3.0f), (p3, t * 3.0f),
            (p1, oneOver6), (p2, 4.0f * oneOver6), (p3, oneOver6));
    }

    /// <summary>Scales each point and adds it to a running total, in the order given (<c>VectorScale</c> then
    /// <c>VectorAdd( term, output, output )</c>).</summary>
    private static (float X, float Y) Sum(params ((float X, float Y) Point, float Scale)[] terms)
    {
        float x = 0f;
        float y = 0f;

        foreach (((float X, float Y) point, float scale) in terms)
        {
            x = (point.X * scale) + x;
            y = (point.Y * scale) + y;
        }

        return (x, y);
    }

    /// <summary><c>VectorLerp( a, b, t )</c> = a + ( b - a ) * t.</summary>
    private static (float X, float Y) LerpPoint((float X, float Y) a, (float X, float Y) b, float t) =>
        (a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

    private static (float X, float Y) Unit((float X, float Y) v)
    {
        float length = MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));

        return length > 0f ? (v.X / length, v.Y / length) : v;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private static (float Time, float Value) Bounded(
        IReadOnlyList<SceneCurveSample> ramp, int index, float duration, out bool clamped)
    {
        if (index < 0)
        {
            clamped = true;
            return (0f, 0f);
        }

        if (index >= ramp.Count)
        {
            clamped = true;
            return (duration, 0f);
        }

        clamped = false;
        return (ramp[index].Time, ramp[index].Value);
    }
}
