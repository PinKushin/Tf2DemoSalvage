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
    public static bool TryScaleToUnitLength(ref (double X, double Y, double Z) vector)
    {
        double squared = (vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z);

        if (squared >= DirectionThreshold)
        {
            double scale = ReciprocalSquareRoot(squared);
            vector = (vector.X * scale, vector.Y * scale, vector.Z * scale);
            return true;
        }

        return false;
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
    public static double ReciprocalSquareRoot(double square)
    {
        int high = (int)(BitConverter.DoubleToInt64Bits(square) >> 32);
        int guessHigh = ((InfinityHighWord - high) >> 1) + GuessBias;
        double root = BitConverter.Int64BitsToDouble((long)((ulong)(uint)guessHigh << 32));
        double halfSquare = square * Half;

        for (int step = 0; step < NewtonSteps; step++)
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
