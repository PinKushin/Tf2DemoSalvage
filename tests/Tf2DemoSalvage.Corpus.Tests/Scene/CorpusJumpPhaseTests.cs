using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A real jump passes through its push-off and then its float.
/// </summary>
/// <remarks>
/// **The unit tests pick a phase from a number; this checks the number exists.** The engine measures
/// a jump from <c>m_flJumpStartTime</c>, set when the jump event arrives — and a demo carries no
/// such event, so <c>DemoTimeline</c> derives it from the tick <c>FL_ONGROUND</c> cleared. That
/// derivation is the part that can be wrong in ways a hand-built case cannot show: a clock that
/// never resets, an interval that is still zero when the first frames arrive, a flag that never
/// clears at all.
///
/// Measured on a recording made for it, where the owner jumped, crouch-jumped and rocket-jumped
/// deliberately.
/// </remarks>
public sealed class CorpusJumpPhaseTests
{
    private const string MovementDemo = "movement-test-stv-cp_process";

    [Test]
    public void JumpPhase_ARealJump_IsSeenInBothPhases()
    {
        string path = Corpus.Demo(MovementDemo);

        List<float> airborne =
        [
            .. TimelineCache.For(path)
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.AirborneSeconds is not null)
                .Select(player => player.AirborneSeconds!.Value),
        ];

        airborne.ShouldNotBeEmpty("somebody leaves the ground in this recording");

        // **Both sides of the half-second split**, because either alone is consistent with a clock
        // that is broken in a different way: only-small means it resets every tick, and only-large
        // means it never resets between jumps.
        airborne.ShouldContain(
            seconds => seconds <= PlayerActivityState.JumpStartSeconds,
            "a jump must be seen in its first half second");

        airborne.ShouldContain(
            seconds => seconds > PlayerActivityState.JumpStartSeconds,
            "and after it, or the clock is resetting every tick");

        // **The clock starts at zero rather than at the tick number.** The first airborne sample of
        // any jump is the moment the flag cleared, so the smallest reading in the whole recording
        // must be zero — a clock measured from the demo's start would report hundreds of seconds.
        airborne.Min().ShouldBe(0f);

        // And it is bounded by something sane. The longest airborne stretch here is a rocket jump
        // and a respawn, both seconds rather than minutes; a clock that never reset would grow to
        // the length of the recording.
        airborne.Max().ShouldBeLessThan(
            60f,
            "no jump in this recording lasts a minute; a larger reading means the clock never reset");
    }

    [Test]
    public void JumpPhase_RocketJumpVersusOrdinary_DiffersInAirWalk()
    {
        // **The threshold is chosen to separate exactly these two**, which is why both halves are
        // asserted on one recording. An ordinary TF2 jump leaves the ground at 268 units a second
        // and the air-walk needs more than 300, so a recording of plain jumps AND a rocket jump
        // must contain airborne players of both kinds.
        //
        // Without the second assertion a latch that never cleared would pass; without the first, a
        // threshold that never fired would.
        string path = Corpus.Demo(MovementDemo);

        List<ScenePlayer> airborne =
        [
            .. TimelineCache.For(path)
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying && player.AirborneSeconds is not null),
        ];

        airborne.ShouldContain(
            player => player.Airwalking,
            "the rocket jump rises fast enough to air-walk");

        airborne.ShouldContain(
            player => !player.Airwalking,
            "and an ordinary jump does not, at 268 units a second against a threshold of 300");
    }
}
