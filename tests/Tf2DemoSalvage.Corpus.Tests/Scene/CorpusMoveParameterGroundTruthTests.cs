using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>move_x</c> against the buttons that produced it, in a demo recorded to settle exactly this.
/// </summary>
/// <remarks>
/// **B101, and the reason it needed a POV recording.** Moving players played the backward run —
/// "as long as they are moving they are running backwards according to the animation, but if they
/// sit still, they do properly stand". Three divergences from
/// <c>CMultiPlayerAnimState::ComputePoseParam_MoveYaw</c> were found by reading and none of them
/// inverts a direction, which is the signal to stop reading and measure.
///
/// **A POV demo carries the recorder's own input.** <c>dem_usercmd</c> holds the <c>CUserCmd</c>
/// that was sent to the server — view angles, the movement vector and the button mask — so
/// "was this player running forward" has an answer that does not depend on any of the code under
/// test. <c>forwardmove 450, sidemove 0</c> with <c>IN_FORWARD</c> held is running dead forward,
/// and that is ground truth in the strict sense: it is the input, not a reconstruction of it.
///
/// Everything else here is derived from positions, so a test that inferred the direction from the
/// same track it is checking would be measuring the code against itself.
///
/// **Sampled from the middle of a long unbroken run, never at its edges.** The parameter comes from
/// a differenced position over a tenth of a second and is drawn a further <c>cl_interp</c> behind,
/// so the value at a tick describes motion from slightly before it. Inside a run of hundreds of
/// consecutive forward ticks that lag is irrelevant, and at the boundary it is the whole answer —
/// which is the kind of off-by-a-window that would make this test report a defect that is not there.
/// </remarks>
public sealed class CorpusMoveParameterGroundTruthTests
{
    /// <summary>The POV half of a pair recorded to exercise movement.</summary>
    private const string MovementDemo = "movement-test-pov-cp_process";

    /// <summary><c>IN_FORWARD</c>, <c>in_buttons.h</c>.</summary>
    private const uint InForward = 1 << 3;

    /// <summary>Full forward movement; <c>cl_forwardspeed</c> is 450 and TF2 does not lower it.</summary>
    private const float FullForward = 400f;

    /// <summary>How many consecutive forward ticks a run needs before its middle is safe to sample.</summary>
    /// <remarks>
    /// Comfortably more than the seven-tick interpolation delay plus the roughly seven ticks the
    /// heading window spans, so the middle of a qualifying run is describing forward motion however
    /// those two stack up.
    /// </remarks>
    private const int SettledRun = 60;

    [Test]
    public void RunningForward_DrivesMoveXPositive()
    {
        string path = Corpus.Demo(MovementDemo);

        byte[] file = File.ReadAllBytes(path);

        List<int> forward = [.. ForwardTicks(file)];

        // **The control.** A recording without a long forward run cannot answer the question, and
        // without this the assertion below would pass by never executing.
        forward.Count.ShouldBeGreaterThan(
            SettledRun,
            "this recording must contain sustained forward running, or nothing is measured");

        List<int> sampled = [.. SettledMiddles(forward)];

        sampled.ShouldNotBeEmpty($"no unbroken run of {SettledRun} forward ticks was found");

        DemoTimeline timeline = DemoTimeline.Build(file);

        // **The recorder is the only player in this recording**, which is what makes it usable as
        // ground truth: it is a solo session recorded to exercise movement, so the player whose
        // buttons were just read is the only one there is. Asserted below rather than assumed.
        //
        // Identifying them by m_fFlags was tried first and does not work — that send prop lives in
        // DT_LocalPlayerExclusive and should reach the recorder, but it arrives null here for
        // everyone including them. That is a real gap and is filed separately; it is not this
        // test's question, and hanging the selection on it would have made this measurement depend
        // on an unrelated defect.
        List<(int Tick, float MoveX, float MoveY)> measured = [];

        foreach (int tick in sampled)
        {
            // **The double overload, and the integer one is a trap here.** PlayersAt(int) returns
            // the stored frame, in which Speed, MoveX and MoveY are all still at their defaults —
            // they are derived from the surrounding track and filled in by the interpolating
            // overload. Reading the integer one measures zero for every player and reports it as a
            // parameter of zero rather than as an unasked question.
            List<ScenePlayer> present = [];
            timeline.PlayersAt((double)tick, present);

            List<ScenePlayer> playing = [.. present.Where(player => player.IsPlaying && player.Drawn)];

            playing.Count.ShouldBe(
                1,
                $"tick {tick} should hold exactly the recorder, but holds " +
                string.Join(
                    "; ",
                    playing.Select(player =>
                        $"#{player.EntityIndex.ToString(CultureInfo.InvariantCulture)} " +
                        $"speed={player.Speed:0.0}")));

            ScenePlayer who = playing[0];

            if (who.Speed > 0.5f)
            {
                measured.Add((tick, who.MoveX, who.MoveY));
            }
        }

        measured.ShouldNotBeEmpty(
            "the recorder must be moving at the sampled ticks, or the parameter is not defined there");

        // **The measurement.** Running dead forward is move_x = +1 in the body's own frame:
        // ComputePoseParam_MoveYaw sets x = cos(flYaw), and flYaw is zero when the direction of
        // travel matches the body's yaw. Negative means the legs are being driven by the backward
        // corner of the blend grid, which is what was seen.
        List<(int Tick, float MoveX, float MoveY)> backwards =
            [.. measured.Where(sample => sample.MoveX < 0f)];

        backwards.ShouldBeEmpty(
            $"{backwards.Count} of {measured.Count} sampled ticks report a negative move_x while " +
            "the recorded input says forwardmove 450 with IN_FORWARD held. All samples: " +
            string.Join(
                "; ",
                measured.Select(sample =>
                    $"tick {sample.Tick} move_x {sample.MoveX:0.000} move_y {sample.MoveY:0.000}")));
    }

    /// <summary>Every demo tick whose recorded command is pure forward movement.</summary>
    /// <param name="file">The whole demo.</param>
    private static IEnumerable<int> ForwardTicks(ReadOnlyMemory<byte> file)
    {
        // Past the header, which the command reader does not skip for itself — handed the whole
        // file it reads the first byte of "HL2DEMO" as a command type and throws.
        foreach (DemoCommand command in DemoCommandReader.Read(file[DemoHeader.SizeBytes..]))
        {
            if (command.Type is not DemoCommandType.UserCmd)
            {
                continue;
            }

            UserCommand user = UserCommand.Decode(command.Payload.Span);

            // Sidemove excluded rather than merely small: a strafing player is moving diagonally
            // and move_x is legitimately less than one there, so including them would weaken the
            // prediction from "+1" to "positive-ish".
            if (user.ForwardMove > FullForward &&
                MathF.Abs(user.SideMove) < 1f &&
                (user.Buttons & InForward) != 0)
            {
                yield return command.Tick;
            }
        }
    }

    /// <summary>The middle tick of every unbroken run of forward ticks long enough to have settled.</summary>
    /// <param name="ticks">Forward ticks, in stream order.</param>
    private static IEnumerable<int> SettledMiddles(List<int> ticks)
    {
        int start = 0;

        for (int index = 1; index <= ticks.Count; index++)
        {
            bool broken = index == ticks.Count || ticks[index] > ticks[index - 1] + 1;

            if (!broken)
            {
                continue;
            }

            if (ticks[index - 1] - ticks[start] >= SettledRun)
            {
                yield return (ticks[start] + ticks[index - 1]) / 2;
            }

            start = index;
        }
    }
}
