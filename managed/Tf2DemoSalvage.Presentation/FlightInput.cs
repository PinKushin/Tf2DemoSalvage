using System;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the user is asking the free camera to do, independent of which keys say so.</summary>
/// <param name="Forward">−1 back through +1 forward.</param>
/// <param name="Right">−1 left through +1 right.</param>
/// <param name="Up">−1 down through +1 up.</param>
/// <param name="Walk">Whether <c>+speed</c> is held, which makes the camera SLOWER (B215).</param>
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
public readonly record struct FlightInput(float Forward, float Right, float Up, bool Walk)
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
    /// <summary>The speeds Valve declares, for a recording whose server changed nothing.</summary>
    /// <remarks>
    /// **These were four `const float`s until D106.** A server that raised `sv_maxspeed` sends the
    /// new value, this project decodes it, and constants ignore it — which is silent, because the
    /// picture stays plausible. The owner: *"baked default is never the right answer i dont think,
    /// at least not if its not a baked default valve has"*.
    ///
    /// Passing a <see cref="ServerConVars"/> from the open demo is what makes a jump or surf server
    /// replay at its own speeds; this static one is the vanilla answer and the fallback.
    /// </remarks>
    public static readonly ServerConVars Shipped = new();

    /// <summary>Units travelled per second with nothing else held, under a given server.</summary>
    /// <param name="server">What the recording server had set, or <see cref="Shipped"/>.</param>
    /// <returns>The ceiling the spectator move clamps to.</returns>
    /// <remarks>
    /// **960 at Valve's defaults, and it is `sv_maxspeed * sv_specspeed` rather than a number
    /// anyone picked** (B215).
    ///
    /// TF2's roaming spectator runs `FullObserverMove` (`gamemovement.cpp:2144`), which delegates
    /// straight to `FullNoClipMove( sv_specspeed, sv_specaccelerate )` because `sv_specnoclip` ships
    /// `1` — so the clipped body below that branch is dead at shipped settings. `FullNoClipMove`
    /// opens with `float maxspeed = sv_maxspeed.GetFloat() * factor;` (`:2260`).
    ///
    /// The keys ask for more than this and never get it: forward is `cl_forwardspeed * factor` =
    /// 1350, clamped to 960. Vertical asks for `cl_upspeed(320) * 3` = 960 exactly, which is why one
    /// number can serve every axis here.
    ///
    /// **This viewer flew at 600 until 2026-08-26**, which was nobody's reading of the engine — it
    /// was reasoned from the keyboard-repeat defect it replaced (B97). The free camera was slower
    /// than the game's, while carrying a ×4 modifier that suggested the opposite.
    /// </remarks>
    public static float SpeedPerSecond(ServerConVars server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server.Number("sv_maxspeed") * server.Number("sv_specspeed");
    }

    /// <summary>Units travelled per second with <c>+speed</c> held, under a given server.</summary>
    /// <param name="server">What the recording server had set, or <see cref="Shipped"/>.</param>
    /// <returns>The walk speed, which is lower.</returns>
    /// <remarks>
    /// **675 at Valve's defaults, which is 70.3% of normal and NOT half of it.** `FullNoClipMove`
    /// halves `factor` (`:2265`) *after* computing `maxspeed` from the unhalved value, so the normal
    /// case is clamped to 960 and this one — `cl_forwardspeed * 1.5` = 675 — stays under the ceiling
    /// untouched. Reading `factor /= 2.0f` as "halves the camera" gives 480 and is wrong by 20
    /// points.
    ///
    /// **It is clamped like the other**, which matters once a server is in play rather than the
    /// defaults: at Valve's values `cl_forwardspeed * 1.5` happens to sit under the ceiling, and on
    /// a server that raised `cl_forwardspeed` without raising `sv_maxspeed` it would not.
    /// </remarks>
    public static float WalkSpeed(ServerConVars server)
    {
        ArgumentNullException.ThrowIfNull(server);

        float asked = server.Number("cl_forwardspeed") * (server.Number("sv_specspeed") / 2f);

        return Math.Min(asked, SpeedPerSecond(server));
    }

    /// <summary>What <c>+speed</c> multiplies the speed by: 0.703125 at Valve's defaults.</summary>
    /// <param name="server">What the recording server had set, or <see cref="Shipped"/>.</param>
    /// <returns>The ratio of walk speed to normal speed.</returns>
    /// <remarks>
    /// **Source's <c>+speed</c> is the WALK key — it slows you down.** `IN_SPEED` divides the move
    /// factor by two in both `FullObserverMove` and `FullNoClipMove`. This viewer bound Shift to
    /// `+speed` and then made it a ×4 accelerator, so a pasted config (D69) did the opposite of what
    /// its author meant: a key held for precise positioning quadrupled the speed instead.
    /// </remarks>
    public static float WalkMultiplier(ServerConVars server) =>
        WalkSpeed(server) / SpeedPerSecond(server);

    /// <summary>Where one frame of flight moves the camera.</summary>
    /// <param name="input">What is being asked for.</param>
    /// <param name="seconds">How long the frame took.</param>
    /// <param name="pitch">Camera pitch in degrees, positive downwards.</param>
    /// <param name="yaw">Camera yaw in degrees.</param>
    /// <param name="server">What the recording server had set, or <see cref="Shipped"/>.</param>
    /// <returns>A world-space delta, zero when nothing is asked for.</returns>
    /// <remarks>
    /// **The result is normalised before it is scaled**, so travelling diagonally is not faster than
    /// travelling straight. Without it, holding forward and right covers 1.41 times the distance —
    /// the classic diagonal-speed bug, which feels like the camera being inconsistent rather than
    /// like a defect anyone can name.
    /// </remarks>
    public static (float X, float Y, float Z) Movement(
        FlightInput input, double seconds, float pitch, float yaw, ServerConVars server)
    {
        ArgumentNullException.ThrowIfNull(server);

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

        float speed = input.Walk ? WalkSpeed(server) : SpeedPerSecond(server);
        float travel = (float)(speed * seconds);
        float scale = travel / length;

        return (x * scale, y * scale, z * scale);
    }
}
