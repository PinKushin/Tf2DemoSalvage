using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Recovers where the camera is from the matrix it already produced.
/// </summary>
/// <remarks>
/// **A reflection needs the eye position and nothing in this renderer was passing one.** The obvious
/// fix is a parameter threaded from every camera down through <c>SetCamera</c> — four call sites,
/// two unrelated camera types, and one of them (<see cref="TopDownCamera"/>) does not hold a
/// position at all, it computes one on the way to a matrix.
///
/// The matrix already contains the answer, so none of that is necessary. That keeps the property
/// the free camera was built on: **the geometry is in map coordinates and only the view changes**,
/// so a camera is sixty-four bytes and not a pipeline.
///
/// **The derivation.** With this project's row-vector convention, <c>clip = world * VP</c>. In a
/// perspective projection the clip <c>w</c> is the view-space depth, which is zero at the eye, and
/// the eye is on the view axis so its clip <c>x</c> and <c>y</c> are zero too. Its clip <c>z</c> is
/// the projection's constant term, which is non-zero. So the eye is the world point mapping to
/// <c>(0, 0, k, 0)</c> — a point at infinity in clip space along <c>+z</c> — and running that
/// backwards through the inverse gives it:
///
/// <code>
/// eye = (0, 0, 1, 0) * VP⁻¹,   then divide by the resulting w
/// </code>
///
/// The <c>k</c> falls out in the homogeneous divide, which is why any non-zero value works.
///
/// **Why this is checkable rather than merely plausible:** <see cref="FreeCamera"/> holds an
/// <c>Origin</c> and produces a matrix from it, so the two are independent recordings of one point
/// and a round trip must return it. That is the test, and it is the reason this is not being
/// trusted on the strength of the algebra above.
/// </remarks>
public static class EyePosition
{
    /// <summary>Where the camera producing this matrix is, in world units.</summary>
    /// <param name="viewProjection">A row-major view-projection matrix, sixteen floats.</param>
    /// <returns>The eye position, or null when the matrix cannot be inverted.</returns>
    /// <exception cref="ArgumentNullException">The matrix is null.</exception>
    /// <exception cref="ArgumentException">The matrix is not sixteen floats.</exception>
    /// <remarks>
    /// **Null rather than a guess for a singular matrix.** A degenerate projection — a zero field
    /// of view, a near plane equal to the far — has no camera position, and returning the origin
    /// would put every reflection on the map at map centre. The caller draws matte instead, which
    /// is what it did before this existed.
    /// </remarks>
    public static (float X, float Y, float Z)? From(float[] viewProjection)
    {
        ArgumentNullException.ThrowIfNull(viewProjection);

        if (viewProjection.Length != 16)
        {
            throw new ArgumentException(
                "A view-projection matrix is sixteen floats.", nameof(viewProjection));
        }

        if (Invert(viewProjection) is not { } inverse)
        {
            return null;
        }

        // (0, 0, 1, 0) * inverse, which selects the third row.
        float x = inverse[8];
        float y = inverse[9];
        float z = inverse[10];
        float w = inverse[11];

        // A w of zero means the eye is itself at infinity, which an orthographic projection gives:
        // its rays are parallel and converge nowhere. Reported as no position rather than as a
        // division by zero producing infinities that then propagate into the shader.
        if (w == 0f || !float.IsFinite(w))
        {
            return null;
        }

        return (x / w, y / w, z / w);
    }

    /// <summary>Inverts a row-major 4x4, or returns null when it is singular.</summary>
    /// <remarks>
    /// Cofactor expansion, written out. A general-purpose linear algebra dependency for one 4x4
    /// once a frame is not worth the reference, and the expansion is checkable against the identity
    /// and against a known camera.
    /// </remarks>
    private static float[]? Invert(float[] m)
    {
        float[] inverse = new float[16];

        inverse[0] = (m[5] * m[10] * m[15]) - (m[5] * m[11] * m[14]) - (m[9] * m[6] * m[15]) +
            (m[9] * m[7] * m[14]) + (m[13] * m[6] * m[11]) - (m[13] * m[7] * m[10]);
        inverse[4] = (-m[4] * m[10] * m[15]) + (m[4] * m[11] * m[14]) + (m[8] * m[6] * m[15]) -
            (m[8] * m[7] * m[14]) - (m[12] * m[6] * m[11]) + (m[12] * m[7] * m[10]);
        inverse[8] = (m[4] * m[9] * m[15]) - (m[4] * m[11] * m[13]) - (m[8] * m[5] * m[15]) +
            (m[8] * m[7] * m[13]) + (m[12] * m[5] * m[11]) - (m[12] * m[7] * m[9]);
        inverse[12] = (-m[4] * m[9] * m[14]) + (m[4] * m[10] * m[13]) + (m[8] * m[5] * m[14]) -
            (m[8] * m[6] * m[13]) - (m[12] * m[5] * m[10]) + (m[12] * m[6] * m[9]);

        inverse[1] = (-m[1] * m[10] * m[15]) + (m[1] * m[11] * m[14]) + (m[9] * m[2] * m[15]) -
            (m[9] * m[3] * m[14]) - (m[13] * m[2] * m[11]) + (m[13] * m[3] * m[10]);
        inverse[5] = (m[0] * m[10] * m[15]) - (m[0] * m[11] * m[14]) - (m[8] * m[2] * m[15]) +
            (m[8] * m[3] * m[14]) + (m[12] * m[2] * m[11]) - (m[12] * m[3] * m[10]);
        inverse[9] = (-m[0] * m[9] * m[15]) + (m[0] * m[11] * m[13]) + (m[8] * m[1] * m[15]) -
            (m[8] * m[3] * m[13]) - (m[12] * m[1] * m[11]) + (m[12] * m[3] * m[9]);
        inverse[13] = (m[0] * m[9] * m[14]) - (m[0] * m[10] * m[13]) - (m[8] * m[1] * m[14]) +
            (m[8] * m[2] * m[13]) + (m[12] * m[1] * m[10]) - (m[12] * m[2] * m[9]);

        inverse[2] = (m[1] * m[6] * m[15]) - (m[1] * m[7] * m[14]) - (m[5] * m[2] * m[15]) +
            (m[5] * m[3] * m[14]) + (m[13] * m[2] * m[7]) - (m[13] * m[3] * m[6]);
        inverse[6] = (-m[0] * m[6] * m[15]) + (m[0] * m[7] * m[14]) + (m[4] * m[2] * m[15]) -
            (m[4] * m[3] * m[14]) - (m[12] * m[2] * m[7]) + (m[12] * m[3] * m[6]);
        inverse[10] = (m[0] * m[5] * m[15]) - (m[0] * m[7] * m[13]) - (m[4] * m[1] * m[15]) +
            (m[4] * m[3] * m[13]) + (m[12] * m[1] * m[7]) - (m[12] * m[3] * m[5]);
        inverse[14] = (-m[0] * m[5] * m[14]) + (m[0] * m[6] * m[13]) + (m[4] * m[1] * m[14]) -
            (m[4] * m[2] * m[13]) - (m[12] * m[1] * m[6]) + (m[12] * m[2] * m[5]);

        inverse[3] = (-m[1] * m[6] * m[11]) + (m[1] * m[7] * m[10]) + (m[5] * m[2] * m[11]) -
            (m[5] * m[3] * m[10]) - (m[9] * m[2] * m[7]) + (m[9] * m[3] * m[6]);
        inverse[7] = (m[0] * m[6] * m[11]) - (m[0] * m[7] * m[10]) - (m[4] * m[2] * m[11]) +
            (m[4] * m[3] * m[10]) + (m[8] * m[2] * m[7]) - (m[8] * m[3] * m[6]);
        inverse[11] = (-m[0] * m[5] * m[11]) + (m[0] * m[7] * m[9]) + (m[4] * m[1] * m[11]) -
            (m[4] * m[3] * m[9]) - (m[8] * m[1] * m[7]) + (m[8] * m[3] * m[5]);
        inverse[15] = (m[0] * m[5] * m[10]) - (m[0] * m[6] * m[9]) - (m[4] * m[1] * m[10]) +
            (m[4] * m[2] * m[9]) + (m[8] * m[1] * m[6]) - (m[8] * m[2] * m[5]);

        float determinant = (m[0] * inverse[0]) + (m[1] * inverse[4]) +
            (m[2] * inverse[8]) + (m[3] * inverse[12]);

        if (determinant == 0f || !float.IsFinite(determinant))
        {
            return null;
        }

        float scale = 1f / determinant;

        for (int index = 0; index < 16; index++)
        {
            inverse[index] *= scale;
        }

        return inverse;
    }
}
