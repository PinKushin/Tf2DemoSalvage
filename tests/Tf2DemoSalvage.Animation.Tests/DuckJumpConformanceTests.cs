using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The duck-jump interpolation — <c>C_TFPlayer::BuildTransformations</c> (B314).
/// </summary>
/// <remarks>
/// **Ducking in mid-air moves the player's ORIGIN**, because the hull shrinks from 82 units to 62
/// (`tf_gamerules.cpp:1313`), and the model would teleport upward with it. The engine draws the
/// skeleton twenty units low at that instant and eases the correction to zero over 0.15 seconds
/// (`c_tf_player.cpp:8764`).
///
/// **So the absence of this is a twenty-unit pop on every crouch jump** — a quarter of a player's
/// height — and roughly a fifth of sampled player states in `z1800` are airborne and ducking.
///
/// **A state machine over TIME, which is why it is its own type.** The engine keeps three members:
/// whether it is interpolating, when the duck began, and when it last held. None of them is
/// networked — the client derives all three from the flags — so there is no decoded field whose
/// absence anyone could have noticed.
/// </remarks>
public sealed class DuckJumpConformanceTests
{
    private const double Tolerance = 1e-4;

    /// <remarks>
    /// **On the ground, nothing happens at all** — the whole block is inside
    /// `if ( GetGroundEntity() == NULL )`. A crouch while standing is an ordinary animation, not a
    /// correction, so applying the offset there would sink every crouching player into the floor.
    /// </remarks>
    [Test]
    public void Offset_WhileOnTheGround_IsZero()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: false, seconds: 0d).ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **At the instant of ducking the correction is FULL**, because `flRatio` is zero and the
    /// interpolation is `1 - flRatio`. That is the frame the origin jumps on, so it is the frame
    /// needing the whole twenty units.
    /// </remarks>
    [Test]
    public void Offset_AtTheMomentOfDuckingInAir_IsTheWholeHullDifference()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d)
            .ShouldBe(1f, Tolerance, "1 - 0/0.15, the full correction");
    }

    /// <remarks>
    /// **It decays over 0.15 seconds and the ramp is linear**, so halfway through is half — a
    /// reader that eased or clamped early would be right at both ends and wrong in between, which
    /// is the shape a test with only endpoints cannot see.
    /// </remarks>
    [Test]
    public void Offset_PartWayThroughTheRamp_IsProportional()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d);

        duck.Update(ducking: true, airborne: true, seconds: 10.075d)
            .ShouldBe(0.5f, Tolerance, "half of 0.15 elapsed");
    }

    [Test]
    public void Offset_OnceTheRampHasRun_IsZeroAgain()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d);

        duck.Update(ducking: true, airborne: true, seconds: 10.2d)
            .ShouldBe(0f, Tolerance, "MIN(0.15, elapsed) clamps, so it does not go negative");
    }

    /// <remarks>
    /// **Releasing the duck in mid-air runs the ramp the other way, NEGATIVE**, from
    /// `m_flDuckJumpInterp = -(1 - flRatio)`. The origin moves back down, so the correction has to
    /// push the model the opposite way — and a reader that took the absolute value would be right
    /// for the duck and draw the release twice as wrong.
    /// </remarks>
    [Test]
    public void Offset_AfterReleasingTheDuckInAir_RunsTheOtherWay()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d);

        duck.Update(ducking: false, airborne: true, seconds: 10d)
            .ShouldBe(-1f, Tolerance, "-(1 - 0), the mirror of the entry ramp");
    }

    /// <remarks>
    /// **The release ramp is measured from when the duck LAST held, not from when it began.** The
    /// engine stamps `m_flLastDuckJumpInterp` on every ducking frame, so a player who held the
    /// crouch for a second still gets a full 0.15 to come out of it. Measuring from the start would
    /// make a long crouch release instantly.
    /// </remarks>
    [Test]
    public void Offset_AfterALongDuck_StillGetsAFullReleaseRamp()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d);
        duck.Update(ducking: true, airborne: true, seconds: 11d);

        duck.Update(ducking: false, airborne: true, seconds: 11.075d)
            .ShouldBe(-0.5f, Tolerance, "half of 0.15 since the duck last held, not since 10");
    }

    /// <remarks>
    /// **Landing ends it outright** — `else if ( m_bDuckJumpInterp ) m_bDuckJumpInterp = false;`,
    /// with no ramp. A player who touches the ground mid-correction stops being corrected, because
    /// the origin they are being corrected against is no longer moving.
    /// </remarks>
    [Test]
    public void Offset_OnLanding_StopsImmediatelyRatherThanEasingOut()
    {
        DuckJump duck = new();

        duck.Update(ducking: true, airborne: true, seconds: 10d);

        duck.Update(ducking: true, airborne: false, seconds: 10.01d)
            .ShouldBe(0f, Tolerance, "the ground branch clears the state, it does not ramp");

        duck.Update(ducking: false, airborne: true, seconds: 10.02d)
            .ShouldBe(0f, Tolerance, "and having been cleared, there is nothing to come out of");
    }
}
