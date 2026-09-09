using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A pose that was RESTATED gives the spline a third sample equal to the second (B382).
/// </summary>
/// <remarks>
/// **The engine appends an entry per update, never collapsing**, so a door held open for seconds has
/// several history entries carrying the SAME open position at different changetimes
/// (`interpolatedvar.h:649` — `AddToHead` is unconditional inside `NoteChanged`, and the
/// "differs/identical" result is only a hint to skip interpolation work).
///
/// So when that door starts to close, `GetInterpolationInfo` picks:
///
/// <code>
///   newer  = the closing entry
///   older  = the newest OPEN entry
///   oldest = the entry before it — OPEN AGAIN, the same position
///   dt2 = older_change_time - oldest_change_time  →  非zero, so m_bHermite is true
/// </code>
///
/// Two identical older samples mean the curve leaves the open position with **zero incoming velocity**.
///
/// **This project collapses restatements into one keyframe** and reconstructs the hold with
/// `_heldUntil`. That is faithful for the PAIR, and wrong for the third sample: `_keyframes[index - 1]`
/// is the previous DISTINCT pose, which for a door is a mid-opening position. The spline then starts
/// the close with a large incoming velocity from the opening motion — so it leaves its start slowly and
/// can move against the direction of travel first.
///
/// **That is the owner's report**: *"doors in this demo from 13 seem to be maybe animating too slow"*
/// and *"the garage doors in 'garage' are not animating or just glitch opening then closing again"*.
/// </remarks>
public sealed class RestatedPoseSplineConformanceTests
{
    /// <summary>The track's own render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    [Test]
    public void At_ADoorClosingAfterBeingHeldOpen_NeverMovesAgainstItsTravel()
    {
        // **A door's real life, in the shape granary's actually have.** It opens over ticks 100-124,
        // is restated open while it waits, then closes over 300-324. The restatements are what the
        // engine keeps as separate same-valued entries and what this project collapses.
        ScenePropTrack track = new(entityIndex: 7, "*1");

        track.Add(0, Shut(0f), appliedAt: 0);

        // Opening: 111 units up over 24 ticks, which is `speed 300` on granary.
        for (int tick = 100; tick <= 124; tick += 4)
        {
            track.Add(tick, Shut(111f * ((tick - 100) / 24f)), appliedAt: tick);
        }

        // **Held open, and RESTATED** — the same pose arriving repeatedly, which is what gives the
        // engine its identical older samples and this project its `_heldUntil`.
        foreach (int tick in (int[])[160, 200, 240, 280])
        {
            track.Add(tick, Shut(111f), appliedAt: tick);
        }

        // Closing, the same speed back down.
        for (int tick = 300; tick <= 324; tick += 4)
        {
            track.Add(tick, Shut(111f - (111f * ((tick - 300) / 24f))), appliedAt: tick);
        }

        // **The assertion: while closing, the drawn height never RISES.** A spline handed a
        // mid-opening third sample carries upward velocity into the close, so the door lifts before
        // it drops — which is both the stutter and the slow start, and is what no per-pair check sees.
        float? highest = null;
        int rose = 0;

        for (int step = 0; step <= 400; step++)
        {
            double at = 300d + Delay + (step / 10d);

            if (at > 324d + Delay + 10) { break; }

            if (track.At(at) is not { } pose) { continue; }

            if (highest is { } before && pose.Z > before + 0.01f)
            {
                rose++;
            }

            highest = pose.Z;
        }

        rose.ShouldBe(
            0,
            "a closing door must not rise: the spline's third sample is the same OPEN pose, so the " +
            "curve leaves it with no incoming velocity");
    }

    [Test]
    public void At_ADoorOpeningFromAHeldShutPose_NeverDipsBelowShut()
    {
        // **The mirror, and the control.** The same fault on the way up drives the door DOWN into its
        // own frame first — which the owner has reported before, recorded in `ScenePropTrack.Add`'s
        // own comment: *"on the way back, sinking below its own frame into the floor"*.
        ScenePropTrack track = new(entityIndex: 7, "*1");

        track.Add(0, Shut(111f), appliedAt: 0);

        foreach (int tick in (int[])[40, 80, 120, 160])
        {
            track.Add(tick, Shut(111f), appliedAt: tick);
        }

        for (int tick = 200; tick <= 224; tick += 4)
        {
            track.Add(tick, Shut(111f - (111f * ((tick - 200) / 24f))), appliedAt: tick);
        }

        float lowest = float.MaxValue;

        for (int step = 0; step <= 400; step++)
        {
            double at = 200d + Delay + (step / 10d);

            if (at > 224d + Delay + 10) { break; }

            if (track.At(at) is { } pose)
            {
                lowest = Math.Min(lowest, pose.Z);
            }
        }

        lowest.ShouldBeGreaterThanOrEqualTo(
            -0.01f, "the door must not overshoot past its own shut position");
    }

    /// <summary>A door at a height, and nothing else — one axis, so the curve is readable.</summary>
    private static ScenePose Shut(float height) => new() { Z = height };
}
