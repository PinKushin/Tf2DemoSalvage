using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the user is asking the free camera to do, independent of which keys say so.</summary>
/// <param name="Forward">−1 back through +1 forward.</param>
/// <param name="Right">−1 left through +1 right.</param>
/// <param name="Up">−1 down through +1 up.</param>
/// <param name="Fast">Whether the speed multiplier applies.</param>
/// <remarks>
/// **This is the seam that got `Keys` out of the geometry.** `FreeFlight.Movement` used to take a
/// <c>IReadOnlySet&lt;Keys&gt;</c> and do two unrelated jobs in one function: decide that W means
/// forward and Ctrl means down, and then work out where that puts the camera given a pitch and a
/// yaw.
///
/// The first is a **view** concern — it is about a keyboard, and rebinding it changes nothing about
/// the movement. The second is geometry, and it is the half worth testing. Welded together, the
/// geometry could only be exercised by constructing WinForms key sets, which is why a function of
/// pure trigonometry had no direct tests.
/// </remarks>
public readonly record struct FlightInput(float Forward, float Right, float Up, bool Fast)
{
    /// <summary>Nothing held.</summary>
    public static FlightInput None => default;

    /// <summary>Whether any axis is asking for movement.</summary>
    /// <remarks>
    /// **Opposed axes count as idle, not as an error.** Holding W and S together cancels exactly,
    /// and that is a person resting two fingers rather than a fault to report.
    /// </remarks>
    public bool IsIdle => Forward == 0f && Right == 0f && Up == 0f;
}

/// <summary>
/// Where the free camera moves, given what the user asked for and where it is pointing.
/// </summary>
/// <remarks>
/// **Valve's axis convention throughout**: X forward, Y left, Z up, with yaw measured
/// counter-clockwise from +X and pitch positive downwards. That last one is the trap — a positive
/// pitch looks DOWN in Source, which is why the forward vector's Z is negated rather than added.
/// Getting it the other way round produces a camera that flies into the floor when the user is
/// looking at the sky, and it looks like an inverted-mouse setting rather than a sign error.
/// </remarks>
public static class FreeFlightPath
{
    /// <summary>Units travelled per second at normal speed.</summary>
    public const float SpeedPerSecond = 600f;

    /// <summary>How much faster the modifier makes it.</summary>
    public const float FastMultiplier = 4f;

    /// <summary>Where one frame of flight moves the camera.</summary>
    /// <param name="input">What is being asked for.</param>
    /// <param name="seconds">How long the frame took.</param>
    /// <param name="pitch">Camera pitch in degrees, positive downwards.</param>
    /// <param name="yaw">Camera yaw in degrees.</param>
    /// <returns>A world-space delta, zero when nothing is asked for.</returns>
    /// <remarks>
    /// **The result is normalised before it is scaled**, so travelling diagonally is not faster than
    /// travelling straight. Without it, holding forward and right covers 1.41 times the distance —
    /// the classic diagonal-speed bug, which feels like the camera being inconsistent rather than
    /// like a defect anyone can name.
    /// </remarks>
    public static (float X, float Y, float Z) Movement(
        FlightInput input, double seconds, float pitch, float yaw)
    {
        if (input.IsIdle || seconds <= 0)
        {
            return (0f, 0f, 0f);
        }

        (float X, float Y, float Z) forward = AngleVectors.Forward(pitch, yaw);
        (float X, float Y, float Z) right = AngleVectors.Right(yaw);

        float x = (forward.X * input.Forward) + (right.X * input.Right);
        float y = (forward.Y * input.Forward) + (right.Y * input.Right);
        float z = (forward.Z * input.Forward) + (right.Z * input.Right) + input.Up;

        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));

        // **An EPSILON, not `<= 0`, and the difference is a real defect this inherited.** The
        // original guard tested for exactly zero, which floating point almost never produces. Fly
        // forward and up while looking straight down and the two cancel — but `cos(90°)` is
        // −4.4e-8 rather than 0, so the length came out at 4.4e-8 and the normalisation scaled that
        // residue up to the FULL travel distance. The camera jumped 300 units sideways instead of
        // standing still.
        //
        // Reachable in practice: the mouse drag clamps pitch to ±89 precisely because the basis is
        // degenerate at 90, but `TF2DEMOSALVAGE_CAMERA` — which exists to reproduce an exact
        // viewpoint from the game for parity work — did not clamp at all.
        //
        // 1e-4 is far below any genuine input, since the axes are ±1 and the smallest real
        // resultant is of order one, and far above the residue a cancellation leaves.
        if (length <= 1e-4f)
        {
            // Opposed inputs cancelling after projection — looking straight down with forward and
            // up both held, for instance. Idle, not an error.
            return (0f, 0f, 0f);
        }

        float travel = (float)(SpeedPerSecond * (input.Fast ? FastMultiplier : 1f) * seconds);
        float scale = travel / length;

        return (x * scale, y * scale, z * scale);
    }
}
