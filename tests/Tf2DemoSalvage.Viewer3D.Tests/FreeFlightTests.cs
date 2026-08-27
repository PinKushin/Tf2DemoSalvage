using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Flying the free camera from held keys and a frame's duration.
/// </summary>
/// <remarks>
/// **B97.** The camera moved once per <c>WM_KEYDOWN</c>, so Windows' auto-repeat set its speed:
/// nothing for the repeat delay, then fixed jumps at the repeat rate, and never two directions at
/// once because auto-repeat reports only the last key. Integrating per frame fixes all three, and
/// the movement being a pure function is what makes any of it testable without a window.
///
/// **These now drive the path the viewer actually runs (D69).** They used to call
/// <c>FreeFlight.Movement</c>, which took a <c>HashSet&lt;Keys&gt;</c> and a binding table. Once the
/// console took over the controls, `MainForm` stopped calling that method — and eleven of the twelve
/// tests here went on passing against code nothing ran. That is the failure
/// `docs/memory/output-level-assertion-or-it-is-not-done.md` describes, arriving from the other
/// direction: not an untested feature, but a thoroughly tested method that had quietly become dead.
///
/// So each one now presses keys into a <see cref="ConfigConsole"/> exactly as `ProcessCmdKey` does,
/// and asks <see cref="FreeFlightPath"/> for the movement exactly as the frame loop does. The
/// assertions are unchanged; only what they are pointed at moved.
/// </remarks>
public sealed class FreeFlightTests
{
    /// <summary>Looking along +X, level.</summary>
    private const float Yaw = 0f;

    private const float Pitch = 0f;

    /// <summary>One frame of flight with the given keys held, through the viewer's own path.</summary>
    /// <remarks>
    /// **The keys are pressed and then a frame is discarded**, because the frame a key goes down in
    /// only counts half — `CInput::KeyState` returns 0.5 for "pressed and held this frame" and 1.0
    /// thereafter. Measuring the second frame is what makes these assertions about speed rather than
    /// about which frame the press landed in. The discarded read is not a workaround; it is the
    /// first frame, and <see cref="Fly"/> exists so every test agrees about that.
    /// </remarks>
    private static (float X, float Y, float Z) Fly(
        Keys[] keys, double seconds, float pitch = Pitch, float yaw = Yaw, KeyBindings? bound = null)
    {
        ConfigConsole console = Console(bound);

        foreach (Keys key in keys)
        {
            console.KeyDown(KeyNames.NameOf(key));
        }

        console.Intent();

        return FreeFlightPath.Movement(
            console.Intent(), seconds, pitch, yaw, FreeFlightPath.Shipped);
    }

    /// <summary>A console bound as the viewer ships, or to a supplied table.</summary>
    private static ConfigConsole Console(KeyBindings? bound)
    {
        if (bound is null)
        {
            return ConfigConsole.WithDefaults();
        }

        ConfigConsole console = ConfigConsole.WithDefaults();

        foreach ((ViewerAction action, string key) in bound.All())
        {
            if (KeyBindings.Commands.TryGetValue(action, out string? command))
            {
                console.Load($"bind \"{key}\" \"{command}\"");
            }
        }

        return console;
    }

    [Test]
    public void Movement_NothingHeld_DoesNotMove()
    {
        Fly([], 0.016).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Movement_Forward_TravelsSpeedTimesDuration()
    {
        // The whole point of the change: distance is speed times time, so it no longer depends on
        // the keyboard's repeat rate or the frame rate. Half a second at 960 units a second is 480 —
        // 960 being `sv_maxspeed * sv_specspeed`, the spectator ceiling (B215).
        (float X, float Y, float Z) moved = Fly([Keys.W], 0.5);

        moved.X.ShouldBe(FreeFlightPath.SpeedPerSecond(FreeFlightPath.Shipped) * 0.5f, 0.01f);
        moved.Y.ShouldBe(0f, 0.01f);
        moved.Z.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void Movement_HalfTheFrame_TravelsHalfTheDistance()
    {
        // Frame-rate independence stated as a property rather than a single value.
        (float X, float Y, float Z) longer = Fly([Keys.W], 0.02);
        (float X, float Y, float Z) shorter = Fly([Keys.W], 0.01);

        longer.X.ShouldBe(shorter.X * 2f, 0.001f);
    }

    [Test]
    public void Movement_Shift_SlowsTheCameraDown()
    {
        // **Shift goes through the console now**, rather than being read off `Control.ModifierKeys`
        // and handed in as a `fast:` argument. That was the change most likely to break silently: a
        // modifier that never fires looks like a camera whose speed simply does not respond.
        //
        // **And it SLOWS the camera** (B215). Shift is bound to `+speed`, which is Source's walk key
        // — `IN_SPEED` divides the move factor by two in `FullNoClipMove`. This test asserted
        // quadrupling until 2026-08-26, and it passed the whole time, because it was written from
        // what the code did rather than from what the engine does.
        float normal = Fly([Keys.W], 0.1).X;
        float walking = Fly([Keys.W, Keys.ShiftKey], 0.1).X;

        walking.ShouldBeLessThan(normal, "+speed is the walk key");
        walking.ShouldBe(normal * FreeFlightPath.WalkMultiplier(FreeFlightPath.Shipped), 0.01f);
    }

    [Test]
    public void Movement_ShiftOnEitherSideOfTheKeyboard_IsTheSameSpeed()
    {
        // `LShiftKey` and `RShiftKey` are distinct codes and a config binds one name, `SHIFT`. The
        // collapsing happens in `KeyNames.NameOf`; without it the right-hand Shift would do nothing
        // and read as a dead key rather than as a mapping bug.
        float left = Fly([Keys.W, Keys.LShiftKey], 0.1).X;
        float right = Fly([Keys.W, Keys.RShiftKey], 0.1).X;

        right.ShouldBe(left, 0.01f);
        left.ShouldBeLessThan(Fly([Keys.W], 0.1).X, "and both walk, rather than doing nothing");
    }

    [Test]
    public void Movement_TwoKeys_MoveDiagonallyAtTheSameSpeedAsOne()
    {
        // **Auto-repeat could not do this at all**, because it reports one key. And the direction is
        // normalised, so a diagonal is not faster than a straight line — the mistake that makes
        // strafe-running quicker in a lot of homemade cameras.
        (float X, float Y, float Z) straight = Fly([Keys.W], 0.1);
        (float X, float Y, float Z) diagonal = Fly([Keys.W, Keys.D], 0.1);

        float straightLength = MathF.Sqrt(
            (straight.X * straight.X) + (straight.Y * straight.Y) + (straight.Z * straight.Z));

        float diagonalLength = MathF.Sqrt(
            (diagonal.X * diagonal.X) + (diagonal.Y * diagonal.Y) + (diagonal.Z * diagonal.Z));

        diagonalLength.ShouldBe(straightLength, 0.01f);

        // And it genuinely went both ways rather than picking one.
        diagonal.X.ShouldBeGreaterThan(0f);
        MathF.Abs(diagonal.Y).ShouldBeGreaterThan(0f);
    }

    [Test]
    public void Movement_OpposedKeys_Cancel()
    {
        // Holding W and S is a real thing a hand does, and it must not divide by a zero length.
        Fly([Keys.W, Keys.S], 0.1).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Movement_UpAndDown_FollowTheWorldNotTheCamera()
    {
        // Rising along a pitched view drifts sideways and reads as broken, so vertical is the
        // world's up axis. Pitched 60 degrees and yawed 45, the ascent must still be straight up.
        (float X, float Y, float Z) moved = Fly([Keys.OemQuotes], 0.1, pitch: 60f, yaw: 45f);

        moved.Z.ShouldBeGreaterThan(0f);
        moved.X.ShouldBe(0f, 0.01f);
        moved.Y.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void Movement_TheDescendKey_DropsStraightDown()
    {
        (float X, float Y, float Z) moved = Fly([Keys.OemQuestion], 0.1);

        moved.Z.ShouldBeLessThan(0f);
        moved.X.ShouldBe(0f, 0.01f);
        moved.Y.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void Movement_ARebindOntoAModifier_AnswersToBothSides()
    {
        // The sided-key concern kept where it now belongs. Windows reports left and right Control as
        // distinct codes, so a binding of "CTRL" that only matched `Keys.ControlKey` would work on
        // one side of the keyboard and not the other — which reads as a sticky key rather than as a
        // binding bug.
        KeyBindings rebound = new(new Dictionary<ViewerAction, string>
        {
            [ViewerAction.FlyDown] = "CTRL",
        });

        foreach (Keys side in new[] { Keys.ControlKey, Keys.LControlKey, Keys.RControlKey })
        {
            Fly([side], 0.1, bound: rebound).Z.ShouldBeLessThan(0f, $"{side} should descend");
        }
    }

    [Test]
    public void Movement_Forward_FollowsTheYaw()
    {
        // Turned ninety degrees, forward is +Y rather than +X. Without this the camera would fly
        // where it was first pointed for ever.
        (float X, float Y, float Z) moved = Fly([Keys.W], 0.1, yaw: 90f);

        moved.Y.ShouldBeGreaterThan(0f);
        moved.X.ShouldBe(0f, 0.01f);
    }

    [Test]
    public void Movement_AZeroLengthFrame_DoesNotMove()
    {
        // The first frame after a stall reports no elapsed time, and multiplying by it must not
        // produce a NaN through the normalisation.
        Fly([Keys.W], 0d).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void IsFlightKey_NonFlightKeys_AreNotTracked()
    {
        // The caller swallows flight keys; letting every key through would mean a held F or a held
        // Escape never reached the handlers that own them.
        FreeFlight.IsFlightKey(Keys.W).ShouldBeTrue();
        FreeFlight.IsFlightKey(Keys.OemQuotes).ShouldBeTrue();
        FreeFlight.IsFlightKey(Keys.OemQuestion).ShouldBeTrue();
        FreeFlight.IsFlightKey(Keys.ShiftKey).ShouldBeTrue("Shift is a bound key now, not a modifier");

        FreeFlight.IsFlightKey(Keys.F).ShouldBeFalse();
        FreeFlight.IsFlightKey(Keys.Escape).ShouldBeFalse();
        FreeFlight.IsFlightKey(Keys.F12).ShouldBeFalse();
    }
}
