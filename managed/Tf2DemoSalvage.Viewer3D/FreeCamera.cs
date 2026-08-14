using System;

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
