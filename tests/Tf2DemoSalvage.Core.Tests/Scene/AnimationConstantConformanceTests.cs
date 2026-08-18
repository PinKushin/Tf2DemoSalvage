using System.Globalization;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The remaining hand-transcribed animation constants, against their declarations.
/// </summary>
/// <remarks>
/// **The third and last batch found by auditing implemented code against parity tests.** After the
/// gesture enums (<c>GestureConformanceTests</c>) and the feet-yaw literals
/// (<c>FeetYawConformanceTests</c>), these four were what remained: every one decides when a player
/// switches animation, and every one was typed from Valve's source with nothing checking it.
///
/// They come from three different places, which is the reason they had been missed — there is no
/// single header to check:
///
/// <list type="bullet">
/// <item><c>MOVING_MINIMUM_SPEED</c> is a <c>#define</c> in <c>base_playeranimstate.h</c>;</item>
/// <item>the airwalk rise speed and the jump-phase split are inline literals in
/// <c>tf_playeranimstate.cpp</c>;</item>
/// <item><c>DEFAULT_TICK_INTERVAL</c> is a <c>#define</c> in <c>public/const.h</c>.</item>
/// </list>
///
/// **<c>MOVING_MINIMUM_SPEED</c> is declared in a class TF2 does not inherit from, and that is
/// deliberate rather than an error here.** <c>CTFPlayerAnimState</c> derives from
/// <c>CMultiPlayerAnimState</c>, which is standalone — the same correction recorded in
/// <c>PlayerAnimation</c>'s remarks about playback rate. The threshold is still the value this
/// project uses and still the value that header declares, so it is checked; what would be wrong is
/// concluding anything else about that class from it.
/// </remarks>
public sealed class AnimationConstantConformanceTests
{
    private const string TfAnimState = "src/game/shared/tf/tf_playeranimstate.cpp";
    private const string BaseAnimState = "src/game/shared/base_playeranimstate.h";
    private const string Const = "src/public/const.h";

    [Test]
    public void TheAirwalkRiseSpeedIsTheLiteralGuardingTheAirwalkBranch()
    {
        // Matched with its neighbours - the class check and the duck check - because the number
        // alone says nothing: 300 appears in the file for unrelated reasons, and it is this
        // comparison that makes it the airwalk threshold.
        Text(TfAnimState).ShouldMatch(
            @"bValidAirWalkClass\s*&&\s*\(\s*vecVelocity\.z\s*>\s*" +
            Regex.Escape(Literal(PlayerActivityState.AirwalkRiseSpeed)));
    }

    [Test]
    public void TheJumpPhaseSplitIsTheLiteralComparedAgainstTheJumpStart()
    {
        // **Written without an `f` suffix in the SDK, unlike its neighbours**, so the pattern
        // accepts either. That is not pedantry: `0.5` and `0.5f` are the same value here, and a
        // test that demanded the suffix would fail on a formatting choice rather than a behaviour.
        Text(TfAnimState).ShouldMatch(
            @"curtime\s*-\s*m_flJumpStartTime\s*>\s*" +
            Regex.Escape(Bare(PlayerActivityState.JumpStartSeconds)) +
            @"f?");
    }

    [Test]
    public void TheMovingThresholdIsTheDefineItWasTakenFrom()
    {
        Text(BaseAnimState).ShouldMatch(
            @"#define\s+MOVING_MINIMUM_SPEED\s+" +
            Regex.Escape(Bare(PlayerActivityState.MovingMinimumSpeed)) + @"f?");
    }

    [Test]
    public void TheTickIntervalIsTheEnginesDefault()
    {
        // The value every per-tick duration in the scene layer is derived from, so a wrong one
        // scales every animation's timing at once rather than breaking anything visibly.
        Text(Const).ShouldMatch(
            @"#define\s+DEFAULT_TICK_INTERVAL\s*\(\s*" +
            Regex.Escape(Bare(PlaybackClock.DefaultIntervalPerTick)) + @"\s*\)");
    }

    /// <summary>The constant as the SDK writes it with a suffix: <c>300f</c> to <c>300.0f</c>.</summary>
    private static string Literal(float value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + "f";

    /// <summary>The constant with no suffix and no trailing zero: <c>0.5f</c> to <c>0.5</c>.</summary>
    private static string Bare(float value) =>
        value.ToString("0.0###", CultureInfo.InvariantCulture);

    private static string Text(string path)
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore("the Source SDK is not available");
        }

        return SourceSdk.Text(path).ShouldNotBeNull(path);
    }
}
