using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Crossing between Source space and the physics engine's (B58).
/// </summary>
/// <remarks>
/// **Source's physics is IVP — Ipion Virtual Physics — and it does not share Source's conventions.**
/// `FUN_180002cc0` in `vphysics.dll` is where every position, rotation and axis index crosses, and
/// it changes three things at once:
///
/// - **Axes.** `Source (x, y, z)` becomes `IVP (x, −z, y)`. Source is Z-up; IVP is Y-up.
/// - **Units.** Translations are multiplied by <see cref="MetresPerInch"/>.
/// - **Storage.** The 4×4 is written transposed, with the translation in the last ROW.
///
/// **This project already keeps two matrix conventions deliberately and crosses between them once**
/// (`docs/memory/two-matrix-conventions-on-purpose.md`). IVP is a third, and it is the dangerous
/// kind: getting the axis swap right while missing the transpose, or the units while missing the
/// sign, gives a corpse that settles at a plausible angle in the wrong place rather than something
/// obviously broken. So the conversion lives here, in one place, and everything physical goes
/// through it.
///
/// **Read from the binary rather than the SDK, which does not contain it** —
/// `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` carries the twelve assignments and the
/// dumped constants, and D142 records why a decompiler is the right tool for this.
/// </remarks>
public static class IvpTransform
{
    /// <summary>What a Source unit is worth in the physics engine's.</summary>
    /// <remarks>
    /// **Dumped from `18011f000` as `0x3cd013a9`.** Source measures in inches and IVP in metres, so
    /// every length crossing the boundary is scaled — including gravity, velocities and any
    /// distance threshold, which is why a solver run in Source units with Source constants is not
    /// the same simulation.
    /// </remarks>
    public const float MetresPerInch = 0.0254f;

    /// <summary>The reverse scale, as the binary spells it.</summary>
    /// <remarks>
    /// **`39.37`, the dword adjacent to <see cref="MetresPerInch"/>**, and deliberately not
    /// `1f / MetresPerInch` — which is 39.3700787, a different number. Valve stores the pair, so the
    /// pair is carried.
    ///
    /// **Confirmed in use, in `CPhysicsEnvironment::SetGravity`** (`1800150f0`), which converts a
    /// tolerance back for its own log line with `dVar4 * DAT_18011f004` — the 39.37 dword, not a
    /// reciprocal of the other. This doc said the reverse constant had not been read; it has, and
    /// it is this one.
    /// </remarks>
    public const float InchesPerMetre = 39.37f;

    /// <summary>Where a Source point is, in the physics engine's space.</summary>
    /// <param name="x">Source X, in inches.</param>
    /// <param name="y">Source Y, in inches.</param>
    /// <param name="z">Source Z, in inches.</param>
    /// <returns>The point in metres, Y-up.</returns>
    /// <remarks>
    /// The translation column of `FUN_180002cc0`: `(x, −z, y) × 0.0254`.
    ///
    /// **`CPhysicsEnvironment::SetGravity` does the same three lines on a bare vector**
    /// (`1800150f0`), which is where this conversion was confirmed a second time in a function that
    /// shares no code with the matrix one:
    ///
    /// <code>
    ///   local_58 = (double)(0.0254f * g[0]);
    ///   local_50 = (double)(float)((uint)(0.0254f * g[2]) ^ 0x80000000);
    ///   local_48 = (double)(0.0254f * g[1]);
    /// </code>
    /// </remarks>
    public static (float X, float Y, float Z) Position(float x, float y, float z) =>
        (x * MetresPerInch, -z * MetresPerInch, y * MetresPerInch);

    /// <summary>Where a physics point is, back in Source space.</summary>
    /// <param name="x">IVP X, in metres.</param>
    /// <param name="y">IVP Y, in metres.</param>
    /// <param name="z">IVP Z, in metres.</param>
    /// <returns>The point in inches, Z-up.</returns>
    /// <remarks>
    /// The inverse of <see cref="Position"/>: `IVP (x, y, z)` is `Source (x, z, −y)`, since the
    /// forward map sends Source Y to IVP +Z and Source Z to IVP −Y.
    /// </remarks>
    public static (float X, float Y, float Z) SourcePosition(float x, float y, float z) =>
        (x * InchesPerMetre, z * InchesPerMetre, -y * InchesPerMetre);

    /// <summary>Which physics axis a Source axis index becomes.</summary>
    /// <param name="axis">A Source axis, 0 to 3.</param>
    /// <returns>The physics axis, or 0 for an index outside the table.</returns>
    /// <remarks>
    /// **A four-byte table in the binary at `18011f014`, holding `00 02 01 03`**, read through
    ///
    /// <code>
    ///   int FUN_180002bb0(int axis)
    ///   {
    ///       if (axis &lt; 4) { return (int)(char)(&amp;DAT_18011f014)[axis]; }
    ///       return 0;
    ///   }
    /// </code>
    ///
    /// It is the same permutation <see cref="Position"/> applies, stated as indices because a
    /// joint's three limits are remapped by index rather than by transforming anything. A joint
    /// built without it has its twist and its swing on each other's axes — limits of the right size
    /// about the wrong bones.
    ///
    /// **The guard answers 0 rather than reading past the table**, and it is carried rather than
    /// assumed unreachable: a `.phy` is a stranger's file (D32).
    /// </remarks>
    public static int Axis(int axis) =>
        axis switch
        {
            0 => 0,
            1 => 2,
            2 => 1,
            3 => 3,
            _ => 0,
        };

    /// <summary>A Source rotation in the physics engine's axes.</summary>
    /// <param name="source">A Source <c>matrix3x4_t</c>: twelve floats, row-major.</param>
    /// <returns>Nine floats, row-major — the rotation alone.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not twelve floats.</exception>
    /// <remarks>
    /// **`M' = P M Pᵀ`** with `P = [[1,0,0],[0,0,−1],[0,1,0]]`, which is what produces the sign
    /// pattern in the binary's nine rotation assignments: a minus appears exactly where one of the
    /// row or column index passes through the negated axis and the other does not.
    ///
    /// Written out as the nine assignments rather than as a matrix multiply, so the transcription
    /// can be compared line by line against `FUN_180002cc0` — the same reason
    /// `StudioBones.FromQuaternion` is written out rather than called through a vector library.
    /// </remarks>
    public static float[] Rotation(ReadOnlySpan<float> source)
    {
        if (source.Length != 12)
        {
            throw new ArgumentException("A Source matrix3x4_t is twelve floats.", nameof(source));
        }

        return
        [
            source[0], -source[2], source[1],
            -source[8], source[10], -source[9],
            source[4], -source[6], source[5],
        ];
    }

    /// <summary>A Source transform as the physics engine stores it.</summary>
    /// <param name="source">A Source <c>matrix3x4_t</c>: twelve floats, row-major.</param>
    /// <returns>Sixteen floats — the rotation transposed, the translation in the last row.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not twelve floats.</exception>
    /// <remarks>
    /// **`FUN_18000ca70`**, which takes the converted rows and writes them into the COLUMNS of a
    /// 4×4, then puts the translation in the last row:
    ///
    /// <code>
    ///   param_2[0] = r0.x;  param_2[4] = r0.y;  param_2[8]  = r0.z;
    ///   param_2[12] = t.x;  param_2[13] = t.y;  param_2[14] = t.z;
    ///   param_2[3] = param_2[7] = param_2[0xb] = 0.0;  param_2[0xf] = 1.0;
    /// </code>
    ///
    /// So the column that holds a translation in Source's own layout is zeroed here, and a
    /// transcription that kept Source's storage while converting the axes correctly would place
    /// every body at the origin with the right orientation.
    /// </remarks>
    public static float[] Matrix(ReadOnlySpan<float> source)
    {
        float[] rotation = Rotation(source);

        (float x, float y, float z) = Position(source[3], source[7], source[11]);

        return
        [
            rotation[0], rotation[3], rotation[6], 0f,
            rotation[1], rotation[4], rotation[7], 0f,
            rotation[2], rotation[5], rotation[8], 0f,
            x, y, z, 1f,
        ];
    }
}
