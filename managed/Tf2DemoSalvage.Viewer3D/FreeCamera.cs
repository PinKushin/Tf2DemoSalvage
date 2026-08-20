using System;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// A camera anywhere in the world, looking anywhere, built the way the engine builds one.
/// </summary>
/// <remarks>
/// **Ported from Valve rather than derived.** The awkward part of a Source camera is not the
/// projection, it is the basis change: the engine's world axes are X forward, Y LEFT and Z up,
/// which is neither what clip space wants nor what any graphics text assumes, and a hand-rolled
/// camera goes wrong there and nowhere else. The permutation below is
/// <c>CClientShadowMgr::BuildWorldToShadowMatrix</c> (<c>clientshadowmgr.cpp:1971</c>) — including
/// its flip, which Valve's own comment calls "Bizarre vector flip inherited from earlier code,
/// WTF?" and which is nevertheless what the engine does:
///
/// <code>
/// matBasis.GetBasisVectors( vForward, vLeft, vUp );
/// matBasis.SetForward( vLeft );
/// matBasis.SetLeft( vUp );
/// matBasis.SetUp( vForward );
/// matWorldToShadow = matBasis.Transpose();
/// Vector3DMultiply( matWorldToShadow, origin, translation );
/// translation *= -1.0f;
/// </code>
///
/// The projection is <c>MatrixBuildPerspective</c> (<c>vmatrix.cpp:1048</c>), negated X and Y
/// included — Valve's comment there is "negate X and Y so that X points right, and Y points up".
///
/// **The one deliberate difference is the multiply convention**, and it is this project's, not a
/// disagreement about the maths. The shader does <c>mul(world, viewProjection)</c>, so vectors are
/// rows and the composed matrix is the transpose of Valve's column-vector form. It is transposed
/// once, at the end, in <see cref="ToMatrix"/>, rather than every operation being mirrored — one
/// place to be wrong instead of ten.
///
/// **Angles are Valve's QAngle order**: pitch about Y, yaw about Z, roll about X, in degrees.
/// </remarks>
internal sealed class FreeCamera
{
    /// <summary>Where the camera is, in world units.</summary>
    public (float X, float Y, float Z) Origin { get; init; }

    /// <summary>Pitch, yaw and roll in degrees, in Valve's order.</summary>
    public (float Pitch, float Yaw, float Roll) Angles { get; init; }

    /// <summary>Horizontal field of view in degrees.</summary>
    /// <remarks>
    /// TF2's default is 75 for a player and 90 for the SourceTV camera; the engine's own default
    /// <c>CViewSetup</c> is 75. Frame-makers change it, so it is a value rather than a constant.
    /// </remarks>
    public float FieldOfView { get; init; } = 75f;

    /// <summary>Nearest and furthest drawn distance.</summary>
    /// <remarks>
    /// **Near plane distance costs depth precision, so it is not set small "to be safe".** The
    /// engine uses 7 for a player view and this keeps that: at 3 units the same buffer resolves
    /// roughly half as finely far away, which is where a map's coplanar surfaces already fight.
    /// </remarks>
    public float NearZ { get; init; } = 7f;

    /// <summary>Furthest drawn distance.</summary>
    public float FarZ { get; init; } = 28_000f;

    /// <summary>Viewport width over height.</summary>
    public float Aspect { get; init; } = 16f / 9f;

    /// <summary>A camera a fixed distance from a point, looking at it.</summary>
    /// <param name="focus">What to look at, in world units.</param>
    /// <param name="pitch">Degrees below horizontal; 90 looks straight down.</param>
    /// <param name="yaw">Degrees counterclockwise about Z.</param>
    /// <param name="distance">How far back to sit.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A camera placed so that <paramref name="focus"/> is dead centre.</returns>
    /// <remarks>
    /// **Orbiting is placement, not a second kind of camera.** The position is the focus pushed
    /// backwards along the view direction, so everything below is the same
    /// <see cref="Angles"/>/<see cref="Origin"/> pair the engine's own camera takes — which keeps
    /// one thing to be wrong about instead of two, and lets a future free-fly camera set the same
    /// two properties directly.
    ///
    /// Pitch is clamped just inside vertical. At exactly 90 the forward vector is parallel to the
    /// world's up axis, the basis becomes degenerate and the picture collapses; the engine has the
    /// same limit and clamps player pitch to 89.
    /// </remarks>
    public static FreeCamera Orbiting(
        (float X, float Y, float Z) focus, float pitch, float yaw, float distance, float aspect)
    {
        float limited = Math.Clamp(pitch, -89f, 89f);

        (float sinPitch, float cosPitch) = MathF.SinCos(limited * (MathF.PI / 180f));
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * (MathF.PI / 180f));

        // AngleVectors' forward, which is where the camera looks; stepping back along it puts the
        // focus in the middle of the picture.
        (float X, float Y, float Z) forward =
            (cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);

        return new FreeCamera
        {
            Origin = (
                focus.X - (forward.X * distance),
                focus.Y - (forward.Y * distance),
                focus.Z - (forward.Z * distance)),
            Angles = (limited, yaw, 0f),
            Aspect = aspect,
        };
    }

    /// <summary>A camera where a demo says the recorder's eyes were.</summary>
    /// <param name="view">The recorded view, from the packet's <c>democmdinfo_t</c>.</param>
    /// <param name="playerClass">The recorder's class, which decides the eye height.</param>
    /// <param name="ducking">Whether they were crouched.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <returns>The camera.</returns>
    /// <remarks>
    /// **The demo gives the feet and the angles; the height is added here.** Both halves were
    /// established before this existed rather than assumed: the recorded view is the recorder's
    /// <c>GetAbsOrigin()</c>, measured across the corpus to agree with their networked origin to
    /// the hundredth at every tick (<c>docs/findings/01-container.md</c>), and the client adds
    /// <c>GetViewOffset()</c> when it draws.
    ///
    /// **The angles are used unchanged, deliberately.** They are what the recorder was looking at,
    /// already clamped by the engine that wrote them down — anything done to them here is an edit
    /// to the recording rather than a correction of it. That is also why this is a plain factory
    /// rather than something that smooths: <see cref="RecordedView.IsCut"/> exists so a caller can
    /// decide about interpolation, and inventing motion the demo does not describe is the opposite
    /// of what this viewer is for.
    /// </remarks>
    public static FreeCamera AtEye(
        RecordedView view, int playerClass, bool ducking, float aspect)
    {
        float height = ducking
            ? PlayerEye.Ducking(playerClass)
            : PlayerEye.Standing(playerClass);

        return new FreeCamera
        {
            Origin = (view.Origin.X, view.Origin.Y, view.Origin.Z + height),
            Angles = view.Angles,
            Aspect = aspect,
        };
    }

    /// <summary>A camera in the eyes of a player being spectated.</summary>
    /// <param name="origin">The player's origin, in world units.</param>
    /// <param name="pitch">Their eye pitch in degrees.</param>
    /// <param name="yaw">Their eye yaw in degrees.</param>
    /// <param name="ducking">Whether they are crouched.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <returns>The camera.</returns>
    /// <remarks>
    /// **Spectating uses a different height from a player's own view, and that is the engine's
    /// doing rather than an approximation.** <c>C_HLTVCamera::CalcInEyeCamView</c> adds the flat
    /// <c>VEC_VIEW</c> or <c>VEC_DUCK_VIEW</c>, where a player's own client adds
    /// <c>GetClassEyeHeight()</c> — so spectating a sniper puts the camera three units below where
    /// that sniper saw from, and a scout seven above. See <see cref="PlayerEye.Spectated"/>.
    ///
    /// **This is how a SourceTV demo gets a first-person view at all.** An STV recording has no
    /// local player and leaves <c>democmdinfo_t</c> zeroed, so there is no recorded camera to
    /// use — the view is built from the spectated player's own networked position and angles,
    /// which is exactly what the engine does when you spectate in game.
    ///
    /// **A dead player has no in-eye view.** The engine abandons first person and switches to the
    /// chase camera rather than dropping the eye to the floor, so that case belongs to the caller
    /// and is not expressible here.
    /// </remarks>
    public static FreeCamera SpectatingEye(
        (float X, float Y, float Z) origin, float pitch, float yaw, bool ducking, float aspect) =>
        new()
        {
            Origin = (origin.X, origin.Y, origin.Z + PlayerEye.Spectated(ducking)),
            Angles = (pitch, yaw, 0f),
            Aspect = aspect,
        };

    /// <summary>The view-projection the shader wants, row-major, translation in the last row.</summary>
    /// <returns>Sixteen floats for the camera constant buffer.</returns>
    public float[] ToMatrix()
    {
        // **The basis, as AngleVectors builds it**: forward down +X, left down +Y, up down +Z at
        // zero angles. Valve applies yaw about Z, then pitch about Y, then roll about X.
        (float sinPitch, float cosPitch) = MathF.SinCos(Angles.Pitch * (MathF.PI / 180f));
        (float sinYaw, float cosYaw) = MathF.SinCos(Angles.Yaw * (MathF.PI / 180f));
        (float sinRoll, float cosRoll) = MathF.SinCos(Angles.Roll * (MathF.PI / 180f));

        (float X, float Y, float Z) forward =
            (cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);

        // **AngleVectors returns RIGHT, and the basis this feeds wants LEFT.** VMatrix's
        // GetBasisVectors — which is what BuildWorldToShadowMatrix reads — gives forward, left and
        // up, and left is right negated. Transcribing AngleVectors' second vector under the name
        // "left" produces a camera that is correct in every respect except that the world is
        // mirrored, which is the sort of wrong that looks fine until something has writing on it.
        (float X, float Y, float Z) right = (
            (-sinRoll * sinPitch * cosYaw) + (-cosRoll * -sinYaw),
            (-sinRoll * sinPitch * sinYaw) + (-cosRoll * cosYaw),
            -sinRoll * cosPitch);

        (float X, float Y, float Z) left = (-right.X, -right.Y, -right.Z);

        (float X, float Y, float Z) up = (
            (cosRoll * sinPitch * cosYaw) + (-sinRoll * -sinYaw),
            (cosRoll * sinPitch * sinYaw) + (-sinRoll * cosYaw),
            cosRoll * cosPitch);

        // The flip: camera X is the world's LEFT, camera Y is the world's UP, camera Z is the
        // world's FORWARD. Valve's own comment on this is "Bizarre vector flip inherited from
        // earlier code, WTF?" — kept because matching the engine matters more than tidiness.
        //
        // Transposing a pure rotation is inverting it, so these become the ROWS of world-to-view.
        float[] view =
        [
            left.X, up.X, forward.X, 0f,
            left.Y, up.Y, forward.Y, 0f,
            left.Z, up.Z, forward.Z, 0f,
            0f, 0f, 0f, 1f,
        ];

        // translation = -(R * origin), which in this row-vector layout is the last ROW.
        view[12] = -((left.X * Origin.X) + (left.Y * Origin.Y) + (left.Z * Origin.Z));
        view[13] = -((up.X * Origin.X) + (up.Y * Origin.Y) + (up.Z * Origin.Z));
        view[14] = -((forward.X * Origin.X) + (forward.Y * Origin.Y) + (forward.Z * Origin.Z));

        // **MatrixBuildPerspective, transposed into this project's convention.** Valve writes
        // width and height from the near plane and both fields of view; the vertical one is the
        // horizontal one through the aspect ratio.
        float width = 2f * NearZ * MathF.Tan(FieldOfView * (MathF.PI / 180f) * 0.5f);
        float height = width / Aspect;

        float[] projection = new float[16];

        // **Valve's negateXY, and only the X half of it survives the convention change.** Their
        // last step is `negateXY[0][0] = -1; negateXY[1][1] = -1;` with the comment "negate X and Y
        // so that X points right, and Y points up" — correcting a camera basis whose X is the
        // world's LEFT and whose Y, after their flip, runs the other way from a screen's.
        //
        // This project's clip space already has Y upward, so negating it too would put the sky at
        // the bottom. Measured rather than reasoned: with both negated a point 40 units ABOVE the
        // camera lands below centre, and with neither a point 40 units to the world's LEFT lands on
        // the RIGHT of the screen — a mirrored world, which is the failure this camera is most
        // likely to have and least likely to be noticed having.
        projection[0] = -2f * NearZ / width;
        projection[5] = 2f * NearZ / height;
        projection[10] = -FarZ / (NearZ - FarZ);
        projection[11] = 1f;
        projection[14] = NearZ * FarZ / (NearZ - FarZ);

        return Multiply(view, projection);
    }

    /// <summary>Row-major four-by-four multiply, first applied then second.</summary>
    private static float[] Multiply(float[] first, float[] second)
    {
        float[] result = new float[16];

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float total = 0f;

                for (int step = 0; step < 4; step++)
                {
                    total += first[(row * 4) + step] * second[(step * 4) + column];
                }

                result[(row * 4) + column] = total;
            }
        }

        return result;
    }
}
