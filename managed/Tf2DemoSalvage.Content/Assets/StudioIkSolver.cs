using System;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The two-bone inverse kinematics solver — Valve's <c>CIKSolver</c> and <c>Studio_SolveIK</c>.
/// </summary>
/// <remarks>
/// **This is what holds a hand on a weapon's grip and a foot on the ground.** Measured on TF2's own
/// content: every player model declares four chains — <c>rhand</c>, <c>lhand</c>, <c>rfoot</c>,
/// <c>lfoot</c>, three links each — and **705 of the scout's 1012 animations ask for IK**, 2035
/// rules in total (B296). It is not a corner case.
///
/// **The maths is Ken Perlin's, and Valve credits him in the source.** Given a two-link chain from
/// the origin to an end effector at <c>P</c>, with link lengths <c>a</c> and <c>b</c>, find a knee
/// <c>Q</c> with <c>|Q| = a</c> and <c>|P − Q| = b</c>. Rotate <c>P</c> onto the x axis, solve the
/// closed form there, rotate back.
///
/// **The knee is chosen by a PREFERENCE, not determined.** Two links reaching one point leave a
/// circle of valid knee positions, and which one is right is the difference between a knee that
/// bends forwards and one that bends backwards. The preference comes from the chain's own
/// <c>kneeDir</c>, or from where the animation already had the knee.
/// </remarks>
public static class StudioIkSolver
{
    /// <summary>How straight a leg may be before the knee cannot be placed — about one degree.</summary>
    /// <remarks>
    /// **<c>KNEEMAX_EPSILON</c>, <c>bone_setup.cpp:2708</c>**, with Valve's own comment saying what
    /// the number means. A chain reaching for a point further than its own length has no solution,
    /// and one reaching for exactly its own length has a knee whose position is numerically
    /// hopeless — so the reach is clamped just short of straight.
    /// </remarks>
    public const float StraightEnough = 0.9998f;

    /// <summary>Solves for a knee, given the two link lengths and a preferred direction.</summary>
    /// <param name="first">Length of the first link.</param>
    /// <param name="second">Length of the second link.</param>
    /// <param name="target">Where the end effector must reach, relative to the chain's root.</param>
    /// <param name="preferred">Roughly where the knee should end up, relative to the root.</param>
    /// <param name="knee">The solved knee position, relative to the root.</param>
    /// <returns>Whether the solution is a real bend rather than a degenerate one.</returns>
    /// <remarks>
    /// **<c>CIKSolver::solve</c>, <c>bone_setup.cpp:2601</c>**:
    ///
    /// <code>
    ///   defineM(P,D);
    ///   rot(Minv,P,R);
    ///   float r = length(R);
    ///   float d = findD(A,B,r);
    ///   float e = findE(A,d);
    ///   float S[3] = {d,e,0};
    ///   rot(Mfwd,S,Q);
    ///   return d &gt; (r - B) &amp;&amp; d &lt; A;
    /// </code>
    ///
    /// **The return value is not "did it work", it is "is the bend real".** <c>Q</c> is written
    /// either way; <c>false</c> means the knee landed outside the range where the two links can
    /// actually meet, and Valve's caller then leaves the bones alone rather than using the answer.
    ///
    /// **<c>findE</c> takes a square root that can go negative** when <c>d</c> exceeds <c>a</c>,
    /// which is exactly the case the return value reports. Reproduced with a guard that yields zero
    /// rather than a NaN, because a NaN here would propagate silently into a bone matrix and put a
    /// limb nowhere at all — the C would produce a NaN too, and the caller's test discards it, but
    /// only after it has been written.
    /// </remarks>
    public static bool Solve(
        float first, float second, Vector3 target, Vector3 preferred, out Vector3 knee)
    {
        // Minv defines a coordinate system whose x axis contains the target.
        Vector3 x = target;

        if (x.LengthSquared() <= 0f)
        {
            knee = default;
            return false;
        }

        x = Vector3.Normalize(x);

        // Its y axis is perpendicular to the target, in the half-plane the preference points into.
        Vector3 y = preferred - (Vector3.Dot(preferred, x) * x);

        y = y.LengthSquared() > 0f ? Vector3.Normalize(y) : Perpendicular(x);

        // **Valve's basis has a third axis and this does not, because it cannot contribute.**
        // `defineM` computes `Z = X cross Y` and `solve` then rotates `S = {d, e, 0}` — whose z
        // component is zero by construction — so `rot(Mfwd, S, Q)` is `d*X + e*Y` and Z is
        // multiplied by nothing. Dropping it is an identity, not a shortcut.
        //
        // `rot(Minv, P, R)` reduces the same way: R's y and z parts are zero, so `length(R)` is
        // just the target's own length.
        float reach = target.Length();

        float along = Find(first, second, reach);
        float across = Across(first, along);

        knee = (along * x) + (across * y);

        return along > reach - second && along < first;
    }

    /// <summary>How far along the reach the knee sits — Valve's <c>findD</c>.</summary>
    /// <remarks>
    /// <c>(c + (a*a - b*b) / c) / 2</c>, derived in the comment above the function from
    /// <c>d² + e² = a²</c> and <c>(c − d)² + e² = b²</c>.
    /// </remarks>
    private static float Find(float first, float second, float reach) =>
        reach <= 0f
            ? 0f
            : (reach + (((first * first) - (second * second)) / reach)) / 2f;

    /// <summary>How far off the reach the knee sits — Valve's <c>findE</c>.</summary>
    private static float Across(float first, float along)
    {
        float squared = (first * first) - (along * along);

        return squared > 0f ? MathF.Sqrt(squared) : 0f;
    }

    /// <summary>Any unit vector perpendicular to another, for a degenerate preference.</summary>
    /// <remarks>
    /// **Not Valve's, and it stands in for a division by zero.** `normalize(Y)` divides by a length
    /// the engine never checks, so a preference exactly parallel to the reach produces a NaN basis
    /// and a limb at no position at all. The knee is then arbitrary either way; an arbitrary
    /// perpendicular is at least a position. Flagged rather than hidden: this is a departure, taken
    /// because the alternative is not Valve's behaviour but undefined behaviour.
    /// </remarks>
    private static Vector3 Perpendicular(Vector3 along) =>
        Vector3.Normalize(
            MathF.Abs(along.X) < 0.9f
                ? Vector3.Cross(along, Vector3.UnitX)
                : Vector3.Cross(along, Vector3.UnitZ));

    /// <summary>Points a bone matrix's X axis along a vector — <c>Studio_AlignIKMatrix</c>.</summary>
    /// <param name="matrix">The bone matrix, row-major 3x4, rotated in place.</param>
    /// <param name="along">Where its X axis should point.</param>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not twelve floats.</exception>
    /// <remarks>
    /// **<c>bone_setup.cpp:2752</c>**, and the ORDER of the three columns is the whole of it:
    ///
    /// <code>
    ///   tmp1 = vAlignTo; VectorNormalize( tmp1 ); MatrixSetColumn( tmp1, 0, mMat );
    ///   MatrixGetColumn( mMat, 2, tmp3 );
    ///   tmp2 = tmp3.Cross( tmp1 ); VectorNormalize( tmp2 ); MatrixSetColumn( tmp2, 1, mMat );
    ///   tmp3 = tmp1.Cross( tmp2 ); MatrixSetColumn( tmp3, 2, mMat );
    /// </code>
    ///
    /// **Column two is read BEFORE it is written**, and that is not an accident: the bone's existing
    /// Z axis is what decides which way the limb rolls about its new direction. Writing X then Z
    /// then Y — the obvious tidy order — would read a Z that had already been replaced and lose the
    /// roll the animation had.
    ///
    /// **Valve leaves a note there saying to check for X being too near to Z**, and never does.
    /// When the new direction is parallel to the old Z the cross product vanishes and the engine's
    /// normalise divides by zero; here <see cref="Perpendicular"/> answers instead, for the same
    /// reason it does in <see cref="Solve"/> — the roll is arbitrary in that case either way, and
    /// an arbitrary axis is a position where a NaN is not. The matrix's position is untouched
    /// throughout.
    /// </remarks>
    public static void Align(Span<float> matrix, Vector3 along)
    {
        if (matrix.Length != 12)
        {
            throw new ArgumentException("A bone matrix is a matrix3x4_t of twelve floats.");
        }

        if (along.LengthSquared() <= 0f)
        {
            return;
        }

        Vector3 x = Vector3.Normalize(along);

        // **The OLD column two, read before anything is written — and this ordering is load
        // bearing.** Column one is `oldZ cross newX`, so the bone's existing Z is what decides
        // which way the limb rolls about its new direction. Reading it after column two has been
        // rebuilt takes the NEW Z instead and loses the roll the animation had, producing a
        // different pose that is still a valid rotation — which is why only a prediction from the
        // old Z can catch it.
        Vector3 oldZ = new(matrix[2], matrix[6], matrix[10]);

        Vector3 y = Vector3.Cross(oldZ, x);

        y = y.LengthSquared() > 0f ? Vector3.Normalize(y) : Perpendicular(x);

        Vector3 z = Vector3.Cross(x, y);

        matrix[0] = x.X;
        matrix[4] = x.Y;
        matrix[8] = x.Z;

        matrix[1] = y.X;
        matrix[5] = y.Y;
        matrix[9] = y.Z;

        matrix[2] = z.X;
        matrix[6] = z.Y;
        matrix[10] = z.Z;
    }
}
