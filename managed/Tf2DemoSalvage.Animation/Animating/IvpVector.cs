using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The double-precision vector routines IVP builds a face's plane with before its time-of-impact search
/// measures a vertex against it (B369).
/// </summary>
/// <remarks>
/// **Every expression is carried in the disassembly's instruction order**, not the decompiler's — the two
/// disagreed about a sum's grouping in `FUN_180070bc0`, and doubles are not associative (`docs/findings/51`,
/// *The four routines under the point-plane evaluator*).
/// </remarks>
public static class IvpVector
{
    /// <summary><c>DAT_1800f4f20</c>: the squared length below which a vector has no direction.</summary>
    internal const double DirectionThreshold = 1e-19d;

    /// <summary><c>DAT_1800ee388</c>.</summary>
    private const double Half = 0.5d;

    /// <summary><c>DAT_1800ea9b8</c>.</summary>
    private const double One = 1.0d;

    private const int InfinityHighWord = 0x7ff00000;
    private const int GuessBias = 0x1ff00000;
    private const int NewtonSteps = 4;

    /// <summary>A triangle's normal, not scaled to unit length — <c>FUN_18007b940</c>.</summary>
    /// <param name="first">The face's own point.</param>
    /// <param name="second">The start of the next edge of the same triangle.</param>
    /// <param name="third">The start of the edge after that.</param>
    /// <returns><c>(P₁ − P₀) × (P₂ − P₀)</c>.</returns>
    /// <remarks>
    /// **The points are widened to double before they are subtracted** (`CVTPS2PD`, then `SUBSD`), so no
    /// float rounding enters the edges. The engine reaches the second and third points through the half-edge
    /// offset tables `DAT_180124fb8` and `DAT_180124fc8`, which land on the triangle's next and previous
    /// edges — its stored winding, the order these three are given in.
    /// </remarks>
    public static (double X, double Y, double Z) FaceNormal(
        (float X, float Y, float Z) first, (float X, float Y, float Z) second, (float X, float Y, float Z) third)
    {
        double originX = first.X;
        double originY = first.Y;
        double originZ = first.Z;

        double alongX = second.X - originX;
        double alongY = second.Y - originY;
        double alongZ = second.Z - originZ;

        double acrossX = third.X - originX;
        double acrossY = third.Y - originY;
        double acrossZ = third.Z - originZ;

        return (
            (alongY * acrossZ) - (alongZ * acrossY),
            (alongZ * acrossX) - (alongX * acrossZ),
            (alongX * acrossY) - (alongY * acrossX));
    }

    /// <summary>Scales a vector to unit length if it has a direction — <c>FUN_18006e080</c>.</summary>
    /// <param name="vector">The vector, scaled in place when long enough.</param>
    /// <returns>Whether it was scaled.</returns>
    /// <remarks>
    /// `COMISD` then `JNC`, so the scaling branch is taken at or above the threshold and **not for NaN**,
    /// which `>=` reproduces. Below it the vector is left exactly as it was.
    /// </remarks>
    public static bool TryScaleToUnitLength(ref (double X, double Y, double Z) vector) =>
        TryScaleToUnitLength(ref vector, NewtonSteps);

    /// <summary>The same scaling with a given number of Newton steps — <c>FUN_18006f730</c> takes five.</summary>
    /// <param name="vector">The vector, scaled in place when long enough.</param>
    /// <param name="steps">How many steps the reciprocal root takes.</param>
    /// <returns>Whether it was scaled.</returns>
    internal static bool TryScaleToUnitLength(ref (double X, double Y, double Z) vector, int steps)
    {
        double squared = (vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z);

        if (squared >= DirectionThreshold)
        {
            double scale = ReciprocalSquareRoot(squared, steps);
            vector = (vector.X * scale, vector.Y * scale, vector.Z * scale);
            return true;
        }

        return false;
    }

    /// <summary>The cross product — <c>FUN_18006dd30</c>.</summary>
    /// <param name="first">The left operand.</param>
    /// <param name="second">The right operand.</param>
    /// <returns><c>first × second</c>, every term read before any is written.</returns>
    public static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) first, (double X, double Y, double Z) second) =>
        ((first.Y * second.Z) - (first.Z * second.Y),
         (first.Z * second.X) - (first.X * second.Z),
         (first.X * second.Y) - (first.Y * second.X));

    /// <summary>A vector perpendicular to another — <c>FUN_18006db60</c>.</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>The swapped vector crossed with <paramref name="vector"/>.</returns>
    /// <remarks>
    /// **The largest-magnitude component, checked `z`, `y`, `x` with a strict `&gt;`** — so a tie keeps the one checked
    /// first and a NaN is never largest — is swapped with the component before it cyclically, the one moved up
    /// negated, and the result crossed with the original. Not scaled.
    /// </remarks>
    public static (double X, double Y, double Z) Perpendicular((double X, double Y, double Z) vector)
    {
        double[] moved = [vector.X, vector.Y, vector.Z];
        double largest = 0d;
        int index = 0;

        for (int component = 2; component >= 0; component--)
        {
            double magnitude = Math.Abs(moved[component]);
            double before = largest;

            if (magnitude > largest)
            {
                largest = magnitude;
            }

            if (magnitude > before)
            {
                index = component;
            }
        }

        int other = index == 0 ? 2 : index - 1;
        double negated = -moved[index];
        moved[index] = moved[other];
        moved[other] = negated;

        return Cross((moved[0], moved[1], moved[2]), vector);
    }

    /// <summary><c>(1 − t)·a + t·b</c> — <c>FUN_180070050</c>.</summary>
    /// <param name="from">Where <paramref name="fraction"/> zero lands.</param>
    /// <param name="to">Where one lands.</param>
    /// <param name="fraction">The fraction, as the caller widened it.</param>
    /// <returns>The point between.</returns>
    public static (double X, double Y, double Z) Lerp(
        (double X, double Y, double Z) from, (double X, double Y, double Z) to, double fraction)
    {
        double rest = 1d - fraction;

        return ((rest * from.X) + (fraction * to.X),
                (rest * from.Y) + (fraction * to.Y),
                (rest * from.Z) + (fraction * to.Z));
    }

    /// <summary><c>1/√x</c> the engine's way — <c>FUN_18006ecf0</c>.</summary>
    /// <param name="square">The value to take the reciprocal root of.</param>
    /// <returns>Four Newton steps from a guess built out of the value's exponent bits.</returns>
    /// <remarks>
    /// **Not converged, and not meant to be.** The guess halves the exponent through the high word alone —
    /// `((0x7ff00000 − high) SAR 1) + 0x1ff00000`, with the low word of `1.0`, which is zero — so it can be
    /// several percent off, and four steps of `r · ((0.5 − (r·r)·(x·0.5)) + 1.0)` leave some inputs a few
    /// ulps from `1/√x`. Those are the engine's bits, so they are these.
    /// </remarks>
    public static double ReciprocalSquareRoot(double square) => ReciprocalSquareRoot(square, NewtonSteps);

    /// <summary>The same guess and step, taken a given number of times — for the engine's inlined copies.</summary>
    /// <param name="square">The value to take the reciprocal root of.</param>
    /// <param name="steps">How many Newton steps the copy takes.</param>
    /// <returns>The root after that many steps.</returns>
    /// <remarks>
    /// `FUN_180099d60` inlines the routine with FIVE steps where `FUN_18006ecf0` takes four; the guess and the
    /// step's instruction order are the same in both.
    /// </remarks>
    internal static double ReciprocalSquareRoot(double square, int steps)
    {
        int high = (int)(BitConverter.DoubleToInt64Bits(square) >> 32);
        int guessHigh = ((InfinityHighWord - high) >> 1) + GuessBias;
        double root = BitConverter.Int64BitsToDouble((long)((ulong)(uint)guessHigh << 32));
        double halfSquare = square * Half;

        for (int step = 0; step < steps; step++)
        {
            root *= (Half - (root * root * halfSquare)) + One;
        }

        return root;
    }

    /// <summary><c>1/√x</c> for a float — <c>FUN_18006edb0</c>.</summary>
    /// <param name="square">The value to take the reciprocal root of.</param>
    /// <returns>The double routine's answer, narrowed.</returns>
    /// <remarks>
    /// **Six instructions: `CVTSS2SD`, a call to `FUN_18006ecf0`, `CVTSD2SS`.** The edge evaluator's fill scales
    /// its direction by this.
    /// </remarks>
    public static float ReciprocalSquareRoot(float square) => (float)ReciprocalSquareRoot((double)square);
}
