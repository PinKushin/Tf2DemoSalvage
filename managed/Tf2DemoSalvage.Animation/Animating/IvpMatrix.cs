namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The double-precision transform IVP places a body with inside a step (B369).
/// </summary>
/// <remarks>
/// **The time-of-impact evaluators do not rotate through a quaternion.** `FUN_1800734e0` fills a 4×4 in
/// doubles from the body's interpolated rotation with `FUN_180071330`, and every point the evaluators
/// measure goes into the world through `FUN_180070bc0` (`docs/findings/51`, *The four routines under the
/// point-plane evaluator*). This is that matrix: nine rotation terms and a translation.
///
/// **Separate from <see cref="IvpQuaternion.Rotate"/> on purpose, for now.** That method reaches the same
/// rotation by a vector formula, whose own remarks record this fill as unread. Now that it is read, the
/// two differ only in rounding — but `Rotate` is used by every constraint and contact, so moving it onto
/// this route is a change of its own, measured on its own, not a side effect of the time-of-impact port.
/// </remarks>
/// <param name="M0">Row 0, column 0.</param>
/// <param name="M1">Row 0, column 1.</param>
/// <param name="M2">Row 0, column 2.</param>
/// <param name="M4">Row 1, column 0.</param>
/// <param name="M5">Row 1, column 1.</param>
/// <param name="M6">Row 1, column 2.</param>
/// <param name="M8">Row 2, column 0.</param>
/// <param name="M9">Row 2, column 1.</param>
/// <param name="M10">Row 2, column 2.</param>
/// <param name="Translation">Where the body's origin is.</param>
public readonly record struct IvpMatrix(
    double M0, double M1, double M2,
    double M4, double M5, double M6,
    double M8, double M9, double M10,
    (double X, double Y, double Z) Translation)
{
    /// <summary>Builds the transform from a rotation and a position — <c>FUN_180071330</c>.</summary>
    /// <param name="rotation">A unit quaternion, <c>(x, y, z, w)</c> with <c>w</c> the real part.</param>
    /// <param name="position">The body's position.</param>
    /// <returns>The transform.</returns>
    /// <remarks>
    /// <code>
    /// m0 = 1 − (z·2z + y·2y)   m1 = x·2y − w·2z        m2 = w·2y + x·2z
    /// m4 = w·2z + x·2y         m5 = 1 − (z·2z + x·2x)   m6 = y·2z − w·2x
    /// m8 = x·2z − w·2y         m9 = w·2x + y·2z         m10 = 1 − (y·2y + x·2x)
    /// </code>
    ///
    /// **Every term in doubles**, from a float rotation widened first, as the engine's rotation at
    /// `core+0x180` is stored in doubles. The indices skip 3, 7 and 11 because the engine's storage is a
    /// 4×4 and those are its unused fourth column.
    /// </remarks>
    public static IvpMatrix FromRotation(
        (float X, float Y, float Z, float W) rotation, (double X, double Y, double Z) position)
    {
        double x = rotation.X;
        double y = rotation.Y;
        double z = rotation.Z;
        double w = rotation.W;

        double twoZ = z + z;
        double twoY = y + y;
        double xTwoX = x * (x + x);
        double wTwoX = w * (x + x);

        return new IvpMatrix(
            M0: 1d - ((z * twoZ) + (y * twoY)),
            M1: (x * twoY) - (w * twoZ),
            M2: (w * twoY) + (x * twoZ),
            M4: (w * twoZ) + (x * twoY),
            M5: 1d - ((z * twoZ) + xTwoX),
            M6: (y * twoZ) - wTwoX,
            M8: (x * twoZ) - (w * twoY),
            M9: wTwoX + (y * twoZ),
            M10: 1d - ((y * twoY) + xTwoX),
            Translation: position);
    }

    /// <summary>Puts a point given in the body's frame into the world — <c>FUN_180070bc0</c>.</summary>
    /// <param name="local">The point, in the body's frame.</param>
    /// <returns>The point in the world.</returns>
    /// <remarks>
    /// **Summed in the engine's order, `z` term first**: `world[i] = p.z·m[i,2] + p.x·m[i,0] + p.y·m[i,1]
    /// + t[i]`. Doubles are not associative, so the order is carried rather than tidied.
    /// </remarks>
    public (double X, double Y, double Z) ToWorld((double X, double Y, double Z) local) =>
        ((local.Z * M2) + (local.X * M0) + (local.Y * M1) + Translation.X,
         (local.Z * M6) + (local.X * M4) + (local.Y * M5) + Translation.Y,
         (local.Z * M10) + (local.X * M8) + (local.Y * M9) + Translation.Z);
}
