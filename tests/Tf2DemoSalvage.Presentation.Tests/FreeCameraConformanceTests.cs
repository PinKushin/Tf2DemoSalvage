using System;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>How fast the engine's free camera flies, and by which of its two free cameras.</summary>
/// <remarks>
/// **Written before the implementation, from the SDK, per `docs/CONFORMANCE.md`** — B215.
///
/// ## Which camera is the reference, and why it is not the obvious one
///
/// Source has **two** free cameras and they are nothing like each other.
///
/// `CalcDemoViewOverride` (`game/client/view.cpp:141-163`) is the demo-specific one:
/// `absoluteframetime * cl_demoviewoverride * 320`, four directions, no vertical, and the convar is
/// simultaneously the enable flag and the scale — it ships `0`, so the camera is **off**.
///
/// `FullObserverMove` (`game/shared/gamemovement.cpp:2144`) is the roaming spectator. It is the one
/// this viewer is imitating, on the owner's reading, 2026-08-26:
///
/// > *"the correct speeds to use are spectator speeds, im pretty sure, idk what the demo cam speed
/// > is even actually for becasue ive never seen a tf2 server which has spectators off really"*
///
/// **And `FullObserverMove` immediately delegates**, which is the step that decides these numbers:
///
/// ```cpp
/// if ( sv_specnoclip.GetBool() )   // ships "1"
/// {
///     FullNoClipMove( sv_specspeed.GetFloat(), sv_specaccelerate.GetFloat() );
///     return;
/// }
/// ```
///
/// So the clipped free-roam body below that `return` is **dead at shipped settings**, and the real
/// reference is `FullNoClipMove( 3, 5 )` (`:2254`).
///
/// ## The arithmetic
///
/// Every input is a shipped convar, read from `tf/cvarlist.log` (finding 40) and cross-checked
/// against its registration where one exists.
///
/// ```cpp
/// float maxspeed = sv_maxspeed.GetFloat() * factor;   // 320 * 3 = 960  -- BEFORE the halving
/// if ( mv->m_nButtons &amp; IN_SPEED ) factor /= 2.0f;    // 3 -> 1.5
/// float fmove = mv->m_flForwardMove * factor;         // cl_forwardspeed 450 * factor
/// wishvel[2] += mv->m_flUpMove * factor;              // cl_upspeed 320 * factor -- vertical IS here
/// if ( wishspeed > maxspeed ) ... clamp to maxspeed
/// ```
///
/// | | wish | ceiling | result |
/// |---|---:|---:|---:|
/// | normal | `450 * 3` = 1350 | 960 | **960** — clamped |
/// | `+speed` held | `450 * 1.5` = 675 | 960 | **675** — under the ceiling |
///
/// **So `+speed` gives 70.3% of normal, not 50%**, and that is not a rounding artefact: `maxspeed` is
/// computed from the *unhalved* factor on the line above the halving, so the fast case is clamped and
/// the slow case is not. Reading `factor /= 2.0f` as "halves the camera speed" is wrong by 20 points.
///
/// **Two things this retires.** Valve's spectator has a **vertical axis** — `wishvel[2] +=
/// m_flUpMove * factor` — so ours is not an extension; and `+speed` is Source's **walk** key, so a ×4
/// on Shift was inverted, not merely oversized.
///
/// **Evidence class: read from published source, with shipped-data defaults; the two results are
/// arithmetic.** Not measured against a running game — nothing here observes TF2 flying.
/// </remarks>
public sealed class FreeCameraConformanceTests
{
    /// <summary>`sv_maxspeed`, shipped `320`.</summary>
    private const float ValveMaxSpeed = 320f;

    /// <summary>`sv_specspeed`, shipped `3` — `movevars_shared.cpp:48`.</summary>
    private const float ValveSpectatorScale = 3f;

    /// <summary>`cl_forwardspeed` and `cl_sidespeed`, shipped `450` — `in_main.cpp:78`.</summary>
    private const float ValveForwardSpeed = 450f;

    /// <summary>`cl_upspeed`, shipped `320` — `in_main.cpp:77`.</summary>
    private const float ValveUpSpeed = 320f;

    /// <summary>What `FullNoClipMove` clamps to: `sv_maxspeed * factor`, 960.</summary>
    private const float ValveCeiling = ValveMaxSpeed * ValveSpectatorScale;

    /// <summary>Normal flight: the 1350 wish clamped to the 960 ceiling.</summary>
    private const float ValveNormalSpeed = ValveCeiling;

    /// <summary>`+speed` flight: 450 × 1.5, which is under the ceiling and so is not clamped.</summary>
    private const float ValveWalkSpeed = ValveForwardSpeed * (ValveSpectatorScale / 2f);

    /// <summary>One second of travel, so a distance reads directly as a speed.</summary>
    private const double OneSecond = 1d;

    private static float SpeedOf(FlightInput input)
    {
        (float X, float Y, float Z) moved =
            FreeFlightPath.Movement(input, OneSecond, pitch: 0f, yaw: 0f);

        return MathF.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y) + (moved.Z * moved.Z));
    }

    [Test]
    public void Movement_HoldingForward_TravelsTheSpectatorCeiling()
    {
        // 960, because the 1350 the keys ask for is clamped by `sv_maxspeed * sv_specspeed`. Our
        // 600 was well under it -- the free camera was SLOWER than the game's, which is the opposite
        // of what the ×4 modifier suggested anyone believed.
        SpeedOf(new FlightInput(Forward: 1f, Right: 0f, Up: 0f, Walk: false))
            .ShouldBe(ValveNormalSpeed, 0.5f);
    }

    [Test]
    public void Movement_WithSpeedHeld_IsSlowerThanNormal()
    {
        // **The direction of the effect, asserted on its own**, because it is the half that was
        // backwards. `+speed` is Source's walk key -- `factor /= 2.0f`, `gamemovement.cpp:2265` --
        // and this viewer made it a ×4 accelerator. A magnitude assertion alone would still pass if
        // someone later "fixed" it to a number that was fast again.
        float normal = SpeedOf(new FlightInput(1f, 0f, 0f, Walk: false));
        float walking = SpeedOf(new FlightInput(1f, 0f, 0f, Walk: true));

        walking.ShouldBeLessThan(normal);
    }

    [Test]
    public void Movement_WithSpeedHeld_Travels675NotHalfOf960()
    {
        // The clamp interaction, pinned as its own prediction. `maxspeed` is computed from the
        // UNHALVED factor, so the normal case is clamped to 960 and the walking case is not clamped
        // at all -- 70.3%, not 50%. A reader who takes `factor /= 2.0f` at face value writes 480.
        SpeedOf(new FlightInput(1f, 0f, 0f, Walk: true)).ShouldBe(ValveWalkSpeed, 0.5f);

        ValveWalkSpeed.ShouldNotBe(ValveNormalSpeed / 2f, "the naive reading of the halving");
    }

    [Test]
    public void Movement_StraightUp_MatchesForwardBecauseBothClamp()
    {
        // `cl_upspeed` is 320 rather than 450, so vertical asks for 320*3 = 960 -- exactly the
        // ceiling. Forward asks for 1350 and is clamped to the same 960. They coincide, and the
        // coincidence is why a single speed constant can serve every axis here.
        SpeedOf(new FlightInput(0f, 0f, Up: 1f, Walk: false))
            .ShouldBe(ValveNormalSpeed, 0.5f);

        (ValveUpSpeed * ValveSpectatorScale).ShouldBe(ValveCeiling);
    }

    [Test]
    public void Movement_ForwardAndSideTogether_IsNotFasterThanOneAxis()
    {
        // Valve gets this from the clamp -- the diagonal wish is 1909 and `maxspeed` cuts it to 960
        // -- and we get it by normalising before scaling. Different mechanisms, same observable, and
        // the observable is what parity means here.
        SpeedOf(new FlightInput(1f, 1f, 0f, Walk: false))
            .ShouldBe(SpeedOf(new FlightInput(1f, 0f, 0f, Walk: false)), 0.5f);
    }
}
