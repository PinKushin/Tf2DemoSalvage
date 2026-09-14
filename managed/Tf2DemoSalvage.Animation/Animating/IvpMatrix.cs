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
    /// **Every term in doubles**, as the engine's rotation at `core+0x180` is stored in doubles. The indices skip 3, 7 and 11
    /// because the engine's storage is a 4×4 and those are its unused fourth column.
    /// </remarks>
    public static IvpMatrix FromRotation(
        (double X, double Y, double Z, double W) rotation, (double X, double Y, double Z) position)
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
    /// **Grouped as the disassembly adds**: `world[i] = ((p.x·m[i,0] + p.y·m[i,1]) + p.z·m[i,2]) + t[i]` —
    /// the `x` and `y` products first, then `z`, then the translation. Doubles are not associative, so the
    /// grouping is carried rather than tidied. **The decompiler printed this `z` first and it was ported that
    /// way**, which put the `z` term into the first addition and moved the last bit of some sums.
    /// </remarks>
    public (double X, double Y, double Z) ToWorld((double X, double Y, double Z) local)
    {
        (double X, double Y, double Z) turned = Rotate(local);

        return (turned.X + Translation.X, turned.Y + Translation.Y, turned.Z + Translation.Z);
    }

    /// <summary>Turns a direction given in the body's frame into the world, without moving it — <c>FUN_1800709f0</c>.</summary>
    /// <param name="direction">The direction, in the body's frame.</param>
    /// <returns>The direction in the world.</returns>
    /// <remarks>
    /// **Each row `(x·m[i,0] + y·m[i,1]) + z·m[i,2]`.** The edge evaluator calls it; the point-plane evaluator
    /// inlines the same arithmetic for its normal as `(n.y·m[i,1] + n.x·m[i,0]) + n.z·m[i,2]`, which is the
    /// same bits because swapping two addends is exact.
    /// </remarks>
    public (double X, double Y, double Z) Rotate((double X, double Y, double Z) direction) =>
        ((direction.X * M0) + (direction.Y * M1) + (direction.Z * M2),
         (direction.X * M4) + (direction.Y * M5) + (direction.Z * M6),
         (direction.X * M8) + (direction.Y * M9) + (direction.Z * M10));

    /// <summary>Turns a direction given in the world into the body's frame — <c>FUN_1800706c0</c>.</summary>
    /// <param name="direction">The direction, in the world.</param>
    /// <returns>The direction in the body's frame.</returns>
    /// <remarks>
    /// **The transpose, each column `(x·m[0,j] + y·m[1,j]) + z·m[2,j]`**, which is the inverse only because the
    /// rotation is orthonormal. No translation, as for any direction.
    /// </remarks>
    public (double X, double Y, double Z) RotateInverse((double X, double Y, double Z) direction) =>
        ((direction.X * M0) + (direction.Y * M4) + (direction.Z * M8),
         (direction.X * M1) + (direction.Y * M5) + (direction.Z * M9),
         (direction.X * M2) + (direction.Y * M6) + (direction.Z * M10));

    /// <summary>Turns a float direction given in the world into the body's frame, narrowed — <c>FUN_180070620</c>.</summary>
    /// <param name="direction">The direction, in the world.</param>
    /// <returns>The direction in the body's frame, each component narrowed.</returns>
    /// <remarks>
    /// **Widened, turned by <see cref="RotateInverse"/>'s columns, and narrowed.** `FUN_180070620` groups the first column
    /// `(y·m[1,0] + x·m[0,0]) + z·m[2,0]`, which is the same bits because swapping two addends is exact. The impact solver's
    /// push inlines the same arithmetic (`FUN_18008f1c0`).
    /// </remarks>
    public (float X, float Y, float Z) RotateInverseNarrowed((float X, float Y, float Z) direction)
    {
        (double X, double Y, double Z) turned = RotateInverse((direction.X, direction.Y, direction.Z));

        return ((float)turned.X, (float)turned.Y, (float)turned.Z);
    }

    /// <summary>Puts a point given in the world into the body's frame — <c>FUN_180080670</c>.</summary>
    /// <param name="world">The point, in the world.</param>
    /// <returns>The point in the body's frame.</returns>
    /// <remarks>
    /// **The translation off first, then the transpose** — each difference in double, then
    /// <see cref="RotateInverse"/>'s columns. The minimize's helpers inline the same arithmetic when they carry a
    /// point from one body into the other (`FUN_18007ba70`, `FUN_18007d480`).
    /// </remarks>
    public (double X, double Y, double Z) ToObject((double X, double Y, double Z) world) =>
        RotateInverse((world.X - Translation.X, world.Y - Translation.Y, world.Z - Translation.Z));
}
