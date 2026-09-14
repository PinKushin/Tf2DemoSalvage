using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A dense linear system — the <c>0x28</c> bytes <c>FUN_1800a4600</c> sets up: an epsilon at <c>+0x0</c>, the row and column
/// counts at <c>+0x8</c>/<c>+0xc</c>, and the values, right-hand side and result at <c>+0x10</c>/<c>+0x18</c>/<c>+0x20</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The many-contact normal solve itself* and its helpers). The engine takes
/// the arrays from the environment's arena; here they are owned, and <see cref="RightHandSide"/> and <see cref="Result"/> can be
/// pointed at other arrays, as the constraint solver points its views. Every loop keeps the binary's order and grouping, SSE
/// pairs included, because the sums are not associative — and every addition and multiplication goes through
/// <see cref="IvpMath.Addsd"/> or <see cref="IvpMath.Mulsd"/> with the binary's destination first, because which NaN survives two NaNs is the
/// destination's, and the JIT would otherwise choose it.
/// </remarks>
internal sealed class IvpLinearSystem
{
    /// <summary><c>0x3e112e0be826d695</c>: what <c>FUN_1800a4600</c> writes at <c>+0x0</c>.</summary>
    internal const double DefaultEpsilon = 1e-9;

    /// <summary><c>DAT_1800f4f20</c>: below this a largest element counts as none.</summary>
    private const double Tiny = 1e-19;

    /// <summary><c>DAT_1800fe8c8</c>: how many epsilons of residual a vanished pivot may leave before the solve gives up.</summary>
    private const double Leftover = 1000d;

    /// <summary><c>DAT_1800fdf68</c>: the share of the right-hand side <see cref="Holds"/> lets a row fall short by.</summary>
    private const double Slack = (double)1e-5f;

    internal IvpLinearSystem(int rows, int columns)
        : this(new double[rows * columns], new double[rows], new double[rows], rows, columns)
    {
    }

    internal IvpLinearSystem(double[] values, double[] rightHandSide, double[] result, int rows, int columns)
    {
        Values = values;
        RightHandSide = rightHandSide;
        Result = result;
        Rows = rows;
        Columns = columns;
    }

    internal double Epsilon { get; set; } = DefaultEpsilon;

    internal int Rows { get; set; }

    internal int Columns { get; set; }

    internal double[] Values { get; }

    internal double[] RightHandSide { get; set; }

    internal double[] Result { get; set; }

    /// <summary><c>d[k] = f·s[k] + d[k]</c> — <c>FUN_1800a5150</c> with its alignment argument zero.</summary>
    internal static void AddScaled(double[] target, int targetStart, double[] source, int sourceStart, double factor, int count)
    {
        for (int k = 0; k < count; k++)
        {
            target[targetStart + k] = IvpMath.Addsd(IvpMath.Mulsd(factor, source[sourceStart + k]), target[targetStart + k]);
        }
    }

    /// <summary>
    /// A run of a matrix multiplied by one factor the way <c>FUN_1800a7990</c> and <c>FUN_1800a4870</c> vectorise it: the whole
    /// blocks of four with each element as <c>MULPD</c>'s destination, the rest with the factor as <c>MULSD</c>'s.
    /// </summary>
    internal static void ScaleRun(double[] values, int start, int count, double factor)
    {
        int whole = count >= 4 ? (count >> 2) * 4 : 0;
        int k = 0;

        for (; k < whole; k++)
        {
            values[start + k] = IvpMath.Mulsd(values[start + k], factor);
        }

        for (; k < count; k++)
        {
            values[start + k] = IvpMath.Mulsd(factor, values[start + k]);
        }
    }

    /// <summary><c>FUN_1800a5210</c>: two runs of a matrix exchanged.</summary>
    internal static void Swap(double[] values, int first, int second, int count)
    {
        for (int k = 0; k < count; k++)
        {
            (values[first + k], values[second + k]) = (values[second + k], values[first + k]);
        }
    }

    /// <summary><c>MAXSD(x, y)</c>: <c>x</c> only when it is strictly greater, so a NaN on either side answers <c>y</c>.</summary>
    internal static double Maxsd(double x, double y) => x > y ? x : y;

    /// <summary>What <c>UCOMISD</c> against zero skips on: zero of either sign, or NaN.</summary>
    internal static bool IsZeroOrUnordered(double value) => !(Math.Abs(value) > 0d);

    /// <summary>
    /// <c>FUN_1800aa2c0</c>: the values scaled by the largest diagonal element's reciprocal and the right-hand side by the largest
    /// magnitude's.
    /// </summary>
    /// <returns>The factor a result of the scaled system is multiplied by — what the engine stores at the solver's <c>+0x28</c>.</returns>
    internal double Equilibrate()
    {
        int rows = Rows;
        int columns = Columns;
        double largest = 0d;
        int k = rows - 1;

        if (rows >= 4)
        {
            for (int block = rows >> 2; block > 0; block--, k -= 4)
            {
                largest = Maxsd(largest, Values[(columns + 1) * k]);
                largest = Maxsd(largest, Values[(columns + 1) * (k - 1)]);
                largest = Maxsd(Values[(columns + 1) * (k - 2)], largest);
                largest = Maxsd(largest, Values[(columns + 1) * (k - 3)]);
            }
        }

        for (; k >= 0; k--)
        {
            largest = Maxsd(Values[(columns + 1) * k], largest);
        }

        double scale = largest > Tiny ? 1d / largest : 1d;
        double peak = 0d;

        for (int row = rows - 1; row >= 0; row--)
        {
            peak = Maxsd(Math.Abs(RightHandSide[row]), peak);
        }

        double shrink;

        if (peak > Tiny)
        {
            shrink = 1d / peak;
        }
        else
        {
            peak = 1d;
            shrink = 1d;
        }

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                Values[row * Columns + column] = IvpMath.Mulsd(scale, Values[row * Columns + column]);
            }
        }

        for (int row = Rows - 1; row >= 0; row--)
        {
            RightHandSide[row] = IvpMath.Mulsd(shrink, RightHandSide[row]);
        }

        return IvpMath.Mulsd(scale, peak);
    }

    /// <summary>
    /// <c>FUN_1800a4d40</c>: this system becomes the rows and columns of <paramref name="source"/> that <paramref name="active"/>
    /// names.
    /// </summary>
    internal void Gather(IvpLinearSystem source, int[] active, int count)
    {
        if (count == 0)
        {
            return;
        }

        if (active[count - 1] == count - 1)
        {
            for (int row = 0; row < count; row++)
            {
                Array.Copy(source.Values, source.Columns * active[row], Values, Columns * row, Columns);
                RightHandSide[row] = source.RightHandSide[active[row]];
            }

            return;
        }

        for (int row = 0; row < count; row++)
        {
            for (int column = 0; column < count; column++)
            {
                Values[Columns * row + column] = source.Values[source.Columns * active[row] + active[column]];
            }

            RightHandSide[row] = source.RightHandSide[active[row]];
        }
    }

    /// <summary>
    /// <c>FUN_1800a80a0</c>: elimination with partial pivoting, a column whose pivot is below <see cref="Epsilon"/> left alone,
    /// then <see cref="BackSubstitute"/>.
    /// </summary>
    /// <returns>False when a vanished pivot leaves too much residual; the result is then zeroed.</returns>
    internal bool Solve()
    {
        for (int column = 0; column < Rows; column++)
        {
            Pivot(column);

            double pivot = Values[(Columns + 1) * column];

            if (!(Math.Abs(pivot) >= Epsilon))
            {
                continue;
            }

            double scale = -1d / pivot;

            for (int row = column + 1; row < Rows; row++)
            {
                int at = Columns * row + column;
                double factor = Values[at];

                if (Math.Abs(factor) > Epsilon)
                {
                    factor = IvpMath.Mulsd(factor, scale);
                    AddScaled(Values, at, Values, (Columns + 1) * column, factor, Rows - column);
                    RightHandSide[row] = IvpMath.Addsd(IvpMath.Mulsd(factor, RightHandSide[column]), RightHandSide[row]);
                }
            }
        }

        return BackSubstitute();
    }

    /// <summary><c>FUN_1800a7270</c>: whether a row's residual at the result is not short of its right-hand side.</summary>
    internal bool Holds(int row)
    {
        double sum = Sum(Values, row * Columns, Result, Rows);
        double right = RightHandSide[row];

        return IvpMath.Addsd(Math.Abs(IvpMath.Mulsd(right, Slack)), sum) >= right;
    }

    /// <summary><c>FUN_1800a76c0</c>: the result becomes the values times the right-hand side, each row as long as the row count.</summary>
    internal void Multiply()
    {
        for (int row = 0; row < Rows; row++)
        {
            Result[row] = Sum(Values, row * Columns, RightHandSide, Rows);
        }
    }

    /// <summary>
    /// A row dotted with a vector the way <c>FUN_1800a7270</c> and <c>FUN_1800a76c0</c> vectorise it: from four elements up, the
    /// whole blocks in two SSE pairs <c>((Σ k≡0 + Σ k≡2) + (Σ k≡1 + Σ k≡3))</c>, then the rest one by one — each product with the
    /// vector's element as its destination, each sum with the running total as its.
    /// </summary>
    private static double Sum(double[] values, int start, double[] factors, int count)
    {
        double sum = 0d;
        int k = 0;

        if (count >= 4)
        {
            double zero = 0d;
            double one = 0d;
            double two = 0d;
            double three = 0d;
            int blocks = count - count % 4;

            for (; k < blocks; k += 4)
            {
                zero = IvpMath.Addsd(zero, IvpMath.Mulsd(factors[k], values[start + k]));
                one = IvpMath.Addsd(one, IvpMath.Mulsd(factors[k + 1], values[start + k + 1]));
                two = IvpMath.Addsd(two, IvpMath.Mulsd(factors[k + 2], values[start + k + 2]));
                three = IvpMath.Addsd(three, IvpMath.Mulsd(factors[k + 3], values[start + k + 3]));
            }

            sum = IvpMath.Addsd(IvpMath.Addsd(zero, two), IvpMath.Addsd(one, three));
        }

        for (; k < count; k++)
        {
            sum = IvpMath.Addsd(sum, IvpMath.Mulsd(factors[k], values[start + k]));
        }

        return sum;
    }

    /// <summary><c>FUN_1800a4f20</c>: the row below with the strictly largest magnitude in the column, searched from the last, exchanged up.</summary>
    private void Pivot(int column)
    {
        double best = Math.Abs(Values[(Columns + 1) * column]);
        int pivot = -1;

        for (int row = Rows - 1; row > column; row--)
        {
            double size = Math.Abs(Values[Columns * row + column]);

            if (size > best)
            {
                best = size;
                pivot = row;
            }
        }

        if (pivot < 0)
        {
            return;
        }

        Swap(Values, Columns * column, Columns * pivot, Rows);
        (RightHandSide[column], RightHandSide[pivot]) = (RightHandSide[pivot], RightHandSide[column]);
    }

    /// <summary><c>FUN_1800a8c90</c>: back substitution into the right-hand side, then copied to the result.</summary>
    /// <remarks>
    /// With four or more columns right of the diagonal the binary takes them four at a time from the last, and the third of each
    /// four holds the value rather than the right-hand side as its product's destination; the rest go one at a time.
    /// </remarks>
    private bool BackSubstitute()
    {
        for (int row = Rows - 1; row >= 0; row--)
        {
            double sum = RightHandSide[row];
            int start = row * Columns;
            int k = Rows - 1;

            if (k - row >= 4)
            {
                for (int block = ((k - row - 4) >> 2) + 1; block > 0; block--, k -= 4)
                {
                    sum -= IvpMath.Mulsd(RightHandSide[k], Values[start + k]);
                    sum -= IvpMath.Mulsd(RightHandSide[k - 1], Values[start + k - 1]);
                    sum -= IvpMath.Mulsd(Values[start + k - 2], RightHandSide[k - 2]);
                    sum -= IvpMath.Mulsd(RightHandSide[k - 3], Values[start + k - 3]);
                }
            }

            for (; k > row; k--)
            {
                sum -= IvpMath.Mulsd(RightHandSide[k], Values[start + k]);
            }

            double diagonal = Values[start + row];
            double solved;

            if (Math.Abs(diagonal) >= Epsilon)
            {
                solved = sum / diagonal;
            }
            else if (Math.Abs(sum) >= IvpMath.Mulsd(Epsilon, Leftover))
            {
                Array.Clear(Result, 0, Rows);

                return false;
            }
            else
            {
                solved = 0d;
            }

            RightHandSide[row] = solved;
        }

        Array.Copy(RightHandSide, Result, Rows);

        return true;
    }
}
