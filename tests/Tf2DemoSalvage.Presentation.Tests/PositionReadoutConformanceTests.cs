using System;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// <c>cl_showpos</c> — the three lines TF2 draws, and which of them says what.
/// </summary>
/// <remarks>
/// **Written off <c>game/client/vgui_fpspanel.cpp:316</c> before any of it existed**, so what it
/// asserts is the engine's readout rather than a description of what got built:
///
/// <code>
///   int nShowPosMode = cl_showpos.GetInt();
///   if ( nShowPosMode &gt; 0 )
///   {
///       Vector vecOrigin = MainViewOrigin();
///       QAngle angles    = MainViewAngles();
///       if ( nShowPosMode == 2 )
///       {
///           C_BasePlayer *pPlayer = C_BasePlayer::GetLocalPlayer();
///           if ( pPlayer )
///           {
///               vecOrigin = pPlayer-&gt;GetAbsOrigin();
///               angles    = pPlayer-&gt;GetAbsAngles();
///           }
///       }
///       ... "pos:  %.02f %.02f %.02f"
///       ... "ang:  %.02f %.02f %.02f"
///       ... "vel:  %.2f"
///   }
/// </code>
///
/// **The owner asked for this as an INSTRUMENT, and that is why it is worth being exact**: *"we
/// should display the position of the camera like cl_showpos in our viewer so we can SS that and
/// you can figure out the positions everything happens at and where you should be able to see
/// them"*. A readout whose numbers are rounded differently from the game's is a readout that cannot
/// be compared against the game, which is the whole use.
///
/// **Three details are easy to get wrong and each is pinned below.**
///
/// - The label separator is TWO spaces, not one, on all three lines.
/// - `pos` and `ang` use `%.02f` and `vel` uses `%.2f`. In C those print identically — the `0` is
///   a width, not a precision, and a width of 2 never pads a number this long. They are written
///   differently in Valve's source and mean the same thing; this reproduces the OUTPUT, and the
///   note is here so nobody "fixes" one to match the other and expects a change.
/// - **`vel` is the local player's speed in BOTH modes.** The mode switches `pos` and `ang` between
///   the view and the player; the velocity line is read from `GetLocalPlayer()` afterwards,
///   outside that branch, and is zero when there is no local player.
/// </remarks>
public sealed class PositionReadoutConformanceTests
{
    [Test]
    public void Lines_InViewMode_ReportTheCameraRatherThanThePlayer()
    {
        // `nShowPosMode == 1` takes MainViewOrigin/MainViewAngles, which for this viewer is
        // wherever the camera is — including the free camera, where there is no player to ask.
        PositionReadout readout = new()
        {
            Mode = PositionReadout.View,
            Camera = (1802f, -679.5f, 373.25f),
            CameraAngles = (11f, -139.5f, 0f),
            Player = (1f, 2f, 3f),
            PlayerAngles = (4f, 5f, 6f),
            Speed = 0f,
        };

        readout.Lines.ShouldBe(
        [
            "pos:  1802.00 -679.50 373.25",
            "ang:  11.00 -139.50 0.00",
            "vel:  0.00",
        ]);
    }

    [Test]
    public void Lines_InPlayerMode_ReportThePlayerRatherThanTheCamera()
    {
        // **The control for the mode, and without it "shows a position" and "shows the RIGHT
        // position" are the same observation.** The two subjects are given completely different
        // numbers so neither can stand in for the other.
        PositionReadout readout = new()
        {
            Mode = PositionReadout.Player2,
            Camera = (1802f, -679.5f, 373.25f),
            CameraAngles = (11f, -139.5f, 0f),
            Player = (64f, -32f, 8.5f),
            PlayerAngles = (0f, 90f, 0f),
            Speed = 0f,
        };

        readout.Lines.ShouldBe(
        [
            "pos:  64.00 -32.00 8.50",
            "ang:  0.00 90.00 0.00",
            "vel:  0.00",
        ]);
    }

    [Test]
    public void Lines_TheVelocityLine_IsThePlayersSpeedInBothModes()
    {
        // Read outside the `nShowPosMode == 2` branch, so the mode does not reach it. An
        // implementation that folded velocity into the mode switch would report zero for a moving
        // player whenever the camera is the subject, which is the ordinary case in this viewer.
        PositionReadout view = new()
        {
            Mode = PositionReadout.View,
            Camera = default,
            CameraAngles = default,
            Player = default,
            PlayerAngles = default,

            // **Deliberately not an exact midpoint, and the first draft was one.** `320.125f` is
            // representable exactly, so `%.2f` of it is decided by the rounding MODE: .NET and
            // IEEE round half to even and answer 320.12, while an older MSVC CRT rounded half away
            // and answered 320.13. Which one TF2's own build does has not been measured, so
            // asserting either would be pinning a guess about the engine — and a test that pins a
            // guess reads afterwards exactly like one that pins a citation.
            //
            // A non-midpoint has one answer under every mode, so it measures "two decimal places"
            // rather than a tie-break nobody has checked.
            Speed = 320.128f,
        };

        view.Lines[2].ShouldBe("vel:  320.13", "rounded to two places, as %.2f does");

        (view with { Mode = PositionReadout.Player2 }).Lines[2].ShouldBe("vel:  320.13");
    }

    [Test]
    public void Lines_WhenHidden_AreEmpty()
    {
        // `if ( nShowPosMode > 0 )`. Zero is the convar's own default
        // (`ConVar cl_showpos( "cl_showpos", "0", ... )`), and anything below it is off too — the
        // engine's test is a greater-than, not a non-zero.
        PositionReadout hidden = new()
        {
            Mode = PositionReadout.Hidden,
            Camera = (1f, 2f, 3f),
            CameraAngles = (4f, 5f, 6f),
            Player = default,
            PlayerAngles = default,
            Speed = 9f,
        };

        hidden.Lines.ShouldBeEmpty();

        (hidden with { Mode = -1 }).Lines.ShouldBeEmpty(
            "the engine's test is `> 0`, so a negative mode is off rather than on");
    }

    [Test]
    public void Lines_AModeAboveTwo_ShowsTheViewRatherThanNothing()
    {
        // **`> 0` opens the block and only `== 2` swaps the subject**, so `cl_showpos 3` draws the
        // VIEW. Worth pinning because the plausible reading — "an unknown mode is invalid" — would
        // make a mistyped convar silently draw nothing, and the engine draws something.
        PositionReadout readout = new()
        {
            Mode = 3,
            Camera = (7f, 8f, 9f),
            CameraAngles = default,
            Player = (1f, 1f, 1f),
            PlayerAngles = default,
            Speed = 0f,
        };

        readout.Lines[0].ShouldBe("pos:  7.00 8.00 9.00");
    }

    [Test]
    public void Lines_ANegativeNearZero_KeepsItsSignAsCDoes()
    {
        // **A float this close to zero is what a stationary camera actually holds**, and both C's
        // `%.02f` and .NET's `F2` keep the sign: `-0.001` prints as `-0.00`, and negative zero
        // prints as `-0.00` rather than `0.00`.
        //
        // **The third component was written as `0.00` on the first attempt and that was wrong.**
        // `-0f` in C# is negative zero — unary minus on a float zero — so the expectation, not the
        // code, was the error. Kept as a case because the value is real: a camera resting exactly
        // on an axis, or a subtraction that underflows from below, produces it constantly, and an
        // implementation that normalised it away would differ from the game on the most common
        // number there is.
        PositionReadout readout = new()
        {
            Mode = PositionReadout.View,
            Camera = (-0.001f, 0f, -0f),
            CameraAngles = default,
            Player = default,
            PlayerAngles = default,
            Speed = 0f,
        };

        readout.Lines[0].ShouldBe("pos:  -0.00 0.00 -0.00");
    }

    [Test]
    public void Lines_TheirNumbers_UseTheInvariantCultureSeparator()
    {
        // **The readout is compared against the game's**, and a machine whose locale writes
        // "1802,00" cannot be. `docs/memory/international-names-are-required.md` is the same rule
        // pointed the other way: culture belongs to the user's text, never to a wire or a
        // diagnostic format.
        System.Globalization.CultureInfo original =
            System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            PositionReadout readout = new()
            {
                Mode = PositionReadout.View,
                Camera = (1802.5f, 0f, 0f),
                CameraAngles = default,
                Player = default,
                PlayerAngles = default,
                Speed = 0f,
            };

            readout.Lines[0].ShouldBe("pos:  1802.50 0.00 0.00");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
