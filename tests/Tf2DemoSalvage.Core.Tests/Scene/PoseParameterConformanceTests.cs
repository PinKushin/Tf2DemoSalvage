using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The pose parameters the wire sends, which this project dropped entirely.
/// </summary>
/// <remarks>
/// **<c>CBaseAnimating</c> networks the whole array** — <c>baseanimating.cpp:243</c>:
///
/// <code>
/// SendPropArray3( SENDINFO_ARRAY3(m_flPoseParameter),
///     SendPropFloat( SENDINFO_ARRAY(m_flPoseParameter),
///                    ANIMATION_POSEPARAMETER_BITS, 0, 0.0f, 1.0f ) ),
/// </code>
///
/// so every value on the wire is NORMALISED to 0..1, and the client stores it that way:
/// <c>C_BaseAnimating::GetPoseParameters</c> (<c>c_baseanimating.cpp:1401</c>) copies
/// <c>m_flPoseParameter[i]</c> straight into the array <c>StandardBlendingRules</c> blends with.
///
/// **Players are the exception and this project was already right about them.**
/// <c>tf_player.cpp:769</c> is <c>SendPropExclude( "DT_BaseAnimating", "m_flPoseParameter" )</c>, so
/// a player sends none and the client computes them in <c>CBasePlayerAnimState</c> — which is what
/// <c>EntityModelSet.PoseValues</c> does. Measured on the 2013 SourceTV foundry demo, the flattened
/// <c>CTFPlayer</c> baseline carries **0** of the 24 elements and <c>CObjectSentrygun</c> carries
/// **24**, so the exclusion still holds at protocol 24 and the split is a fact about the file
/// rather than a reading of the SDK.
///
/// **What dropping them looked like, and it is not what it first appeared.** The obvious prediction
/// — an unsupplied parameter sits at a normalised zero, which is the START of its range — is wrong
/// here, because <c>EntityModelSet.Filled</c> leaves an uncomputed parameter at a RAW zero and
/// normalises afterwards. `models/buildables/sentry3.mdl` declares <c>aim_pitch</c> over −50..50
/// and <c>aim_yaw</c> over −180..180, both symmetric, so a dropped value landed dead CENTRE: every
/// sentry gun in every demo drew level and straight ahead. A plausible pose, never the right one,
/// and quiet for exactly that reason.
/// </remarks>
public sealed class PoseParameterConformanceTests
{
    /// <summary>The array's own table name, which is how the engine names an array's elements.</summary>
    private const string Table = "m_flPoseParameter";

    [Test]
    public void PoseParameters_AnEntitySendingTheArray_ReadsEveryElementInOrder()
    {
        EntityState state = Animating((0, 0.25f), (1, 0.75f));

        IReadOnlyList<float> values = state.PoseParameters();

        values.Count.ShouldBe(2);
        values[0].ShouldBe(0.25f);
        values[1].ShouldBe(0.75f);
    }

    /// <remarks>
    /// **The control, and it is the one that keeps players working.** A player's array is excluded
    /// at the send table, so nothing arrives — and the answer must be "none", not "24 zeroes".
    /// Twenty-four zeroes would be indistinguishable from a sentry aimed at its lower-left corner,
    /// and would override the values <c>CBasePlayerAnimState</c> computes.
    /// </remarks>
    [Test]
    public void PoseParameters_AnEntitySendingNone_ReadsNone()
    {
        new EntityState(1, 0, 0, "CTFPlayer").PoseParameters().ShouldBeEmpty();
    }

    /// <remarks>
    /// **A hole in the array is a real case, not a malformed one.** A delta names only the elements
    /// that changed, and the baseline supplies the rest — but an entity created mid-demo can reach
    /// us having sent element 3 and not element 1. The engine's array is a fixed 24 floats with
    /// every unsent slot at its last value, so the count must follow the HIGHEST index seen rather
    /// than however many keys happen to be present: returning two values for elements 0 and 3 would
    /// hand the blend grid element 3's value under element 1's name.
    /// </remarks>
    [Test]
    public void PoseParameters_WithAGapInTheArray_KeepsEveryIndexInItsOwnSlot()
    {
        EntityState state = Animating((0, 0.5f), (3, 0.9f));

        IReadOnlyList<float> values = state.PoseParameters();

        values.Count.ShouldBe(4);
        values[0].ShouldBe(0.5f);
        values[1].ShouldBe(0f);
        values[2].ShouldBe(0f);
        values[3].ShouldBe(0.9f);
    }

    /// <remarks>
    /// **The function was already here, written for the animation cycle** — the same
    /// <c>LoopingLerp&lt;float&gt;</c> the engine reaches for in both places, so these cases pin the
    /// behaviour a looping pose parameter needs from a helper that already existed rather than
    /// adding a second copy of it. <c>game/client/lerp_functions.h</c>, used by
    /// <c>CInterpolatedVarArray::_Interpolate</c> (<c>interpolatedvar.h:1333</c>) for any element
    /// whose model marks it looping:
    ///
    /// <code>
    /// if ( fabs( flTo - flFrom ) >= 0.5f )
    /// {
    ///     if (flFrom &lt; flTo) flFrom += 1.0f; else flTo += 1.0f;
    /// }
    /// float s = flTo * flPercent + flFrom * (1.0f - flPercent);
    /// s = s - (int)(s);
    /// if (s &lt; 0.0f) s = s + 1.0f;
    /// return s;
    /// </code>
    ///
    /// The 0.5 is on the NORMALISED value, so it is half the parameter's whole range — for a
    /// sentry's <c>aim_yaw</c> that is 180 degrees, and crossing it is what a sentry does every time
    /// it tracks a target past due south.
    /// </remarks>
    [Test]
    public void LoopingLerp_AcrossTheWrap_TakesTheShortWayRound()
    {
        // 0.9 to 0.1 is 0.2 the short way and 0.8 the long way. Halfway must be 0.0, which is the
        // wrap itself — the linear answer is 0.5, the far side of the range.
        ScenePropTrack.LoopingLerp(from: 0.9f, to: 0.1f, fraction: 0.5f).ShouldBe(0f, 1e-5f);
    }

    [Test]
    public void LoopingLerp_WithinHalfTheRange_IsPlainInterpolation()
    {
        // 0.3 to 0.6 is 0.3 apart, under the 0.5 threshold, so nothing wraps and halfway is 0.45.
        ScenePropTrack.LoopingLerp(from: 0.3f, to: 0.6f, fraction: 0.5f).ShouldBe(0.45f, 1e-5f);
    }

    /// <remarks>
    /// **Exactly 0.5 wraps**, because Valve's test is <c>&gt;=</c>. A parameter half a range apart
    /// is genuinely ambiguous — both directions are the same distance — and the engine picks the
    /// wrapping one. Asserted so that a later "tidy" to <c>&gt;</c> reddens something.
    /// </remarks>
    [Test]
    public void LoopingLerp_AtExactlyHalfTheRange_Wraps()
    {
        // 0.25 to 0.75 wrapped: from becomes 1.25, halfway is 1.0, which reduces to 0.
        ScenePropTrack.LoopingLerp(from: 0.25f, to: 0.75f, fraction: 0.5f).ShouldBe(0f, 1e-5f);
    }

    /// <remarks>
    /// The engine wraps the RESULT back into 0..1 with <c>s - (int)s</c> and a negative fixup, so a
    /// blend that lands on or past 1 comes back at the bottom rather than off the end of the grid.
    /// </remarks>
    [Test]
    public void LoopingLerp_LandingPastOne_ComesBackIntoRange()
    {
        // 0.8 to 0.2 wrapped puts `to` at 1.2; three quarters of the way is 1.1, which reduces to
        // 0.1 rather than staying above the top of the range.
        ScenePropTrack.LoopingLerp(from: 0.8f, to: 0.2f, fraction: 0.75f).ShouldBe(0.1f, 1e-5f);
    }

    /// <remarks>
    /// **The payoff of the loop flags, and the case the whole seam exists for.** A sentry pointing
    /// near due south sends a yaw either side of the wrap: 179 degrees normalises to 0.997 and −179
    /// to 0.003, two degrees apart on the model and 0.994 apart as numbers. Interpolated plainly,
    /// the barrel sweeps 358 degrees the wrong way over a whole interpolation window.
    /// </remarks>
    [Test]
    public void At_ALoopingPoseParameterAcrossTheWrap_TakesTheShortWay()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry3.mdl")
        {
            // aim_pitch does not loop; aim_yaw does. Both stated, because a fixture that flagged
            // everything could not tell "used the model's answer" from "wrapped unconditionally".
            PoseParameterLoops = [false, true],
        };

        track.Add(0, new ScenePose { PoseParameters = [0.5f, 0.997f] });
        track.Add(10, new ScenePose { PoseParameters = [0.5f, 0.003f] });

        // **Sampled a full interpolation delay past the midpoint.** A client draws `cl_interp`
        // behind the present (B267), so asking at tick 5 asks about a moment before the first
        // keyframe and gets it back whole — a sample that measures nothing.
        ScenePose between = track.At(13d)!.Value;

        // Halfway across a two-degree gap that straddles the wrap is the wrap itself: 180 degrees,
        // which is 1.0 reduced to 0. The plain answer would be 0.5 — dead ahead, the far side.
        between.PoseParameters[1].ShouldBe(0f, 0.01f);
    }

    /// <remarks>
    /// **The control**: the same two keyframes with the model saying nothing loops must give the
    /// plain answer. Without this the test above passes against code that wraps everything, which
    /// would be wrong for <c>aim_pitch</c> — a barrel that reached its −50 limit would leap to +50.
    /// </remarks>
    [Test]
    public void At_ANonLoopingPoseParameterAcrossTheSameGap_Interpolates()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose { PoseParameters = [0.997f] });
        track.Add(10, new ScenePose { PoseParameters = [0.003f] });

        ScenePose between = track.At(13d)!.Value;

        between.PoseParameters[0].ShouldBe(0.5f, 0.01f);
    }

    /// <remarks>
    /// Two keyframes carrying the same values hand the same array back rather than allocating a
    /// copy, which is the common case for every entity whose parameters are not moving. Asserted on
    /// the REFERENCE, since the values would match either way and the allocation is the point.
    /// </remarks>
    [Test]
    public void At_TwoKeyframesWithTheSameParameters_ReuseTheArray()
    {
        float[] values = [0.25f, 0.75f];

        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry3.mdl");

        track.Add(0, new ScenePose { PoseParameters = values });
        track.Add(10, new ScenePose { X = 100f, PoseParameters = values });

        track.At(13d)!.Value.PoseParameters.ShouldBeSameAs(values);
    }

    /// <summary>An animating entity that sent the given elements of the array.</summary>
    private static EntityState Animating(params (int Index, float Value)[] elements)
    {
        EntityState state = new(1, 0, 0, "CObjectSentrygun");

        foreach ((int index, float value) in elements)
        {
            // **Three zero-padded digits, which is the engine's own naming of an array element**
            // and not a convention chosen here: a demo's `SendTables` declares the sub-table
            // `m_flPoseParameter` with children `000` through `023`.
            state.Set(
                $"{Table}.{index.ToString("000", CultureInfo.InvariantCulture)}",
                PropertyValue.FromFloat(value));
        }

        return state;
    }
}
