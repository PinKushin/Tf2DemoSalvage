using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The inverse of a constraint solver's active block, kept up as contacts join and leave — the object at the solver's
/// <c>+0xa8</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The active set's inverse*). `+0x0` the epsilon, `+0x8` the running inverse
/// <see cref="Inverse"/> and `+0x10` the working copy <see cref="Working"/> the eliminations reduce to unit upper triangular,
/// `+0x28` <see cref="Right"/>, `+0x30` <see cref="Solution"/>, `+0x38` <see cref="Intermediate"/>, `+0x40` <see cref="Scratch"/>,
/// `+0x48` the stride and `+0x4c` the size. Both matrices share one stride, the full system's size.
/// </remarks>
internal sealed class IvpActiveInverse
{
    internal IvpActiveInverse(int stride)
    {
        Stride = stride;
        Inverse = new double[stride * stride];
        Working = new double[stride * stride];
        Right = new double[stride];
        Solution = new double[stride];
        Intermediate = new double[stride];
        Scratch = new double[stride];
    }

    /// <summary><c>0x3eb0c6f7a4000000</c>, what <c>FUN_1800a5e60</c> writes at <c>+0xa8</c>.</summary>
    internal double Epsilon { get; } = BitConverter.Int64BitsToDouble(0x3eb0c6f7a4000000);

    internal int Stride { get; }

    internal int Size { get; set; }

    internal double[] Inverse { get; }

    internal double[] Working { get; }

    internal double[] Right { get; }

    internal double[] Solution { get; }

    internal double[] Intermediate { get; }

    internal double[] Scratch { get; }

    /// <summary>
    /// <c>FUN_1800a7e80</c>'s second half: <see cref="Inverse"/> from the identity by elimination on <see cref="Working"/>, which
    /// the caller has gathered.
    /// </summary>
    internal bool Invert()
    {
        if (Size == 0)
        {
            return true;
        }

        for (int row = Size - 1; row >= 0; row--)
        {
            Array.Clear(Inverse, row * Stride, Size);
            Inverse[row * Stride + row] = 1d;
        }

        for (int step = 1; step < Size; step++)
        {
            int column = step - 1;

            PivotRows(column);

            if (!Normalize(column))
            {
                return false;
            }

            for (int row = Size - 1; row >= step; row--)
            {
                double factor = Working[row * Stride + column];

                if (!IvpLinearSystem.IsZeroOrUnordered(factor))
                {
                    Eliminate(column, row, factor);
                }
            }
        }

        return Normalize(Size - 1);
    }

    /// <summary><c>FUN_1800a8be0</c>: <see cref="Solution"/> for <see cref="Right"/>, through the inverse and then back substitution.</summary>
    internal void Solve()
    {
        Array.Copy(Right, Scratch, Size);
        ApplyInverse();

        for (int row = Size - 1; row >= 0; row--)
        {
            double sum = 0d;
            int start = row * Stride;
            int k = Size - 1;

            if (k - row >= 4)
            {
                for (int block = ((k - row - 4) >> 2) + 1; block > 0; block--, k -= 4)
                {
                    sum = IvpLinearSystem.Addsd(IvpLinearSystem.Mulsd(Intermediate[k], Working[start + k]), sum);
                    sum = IvpLinearSystem.Addsd(IvpLinearSystem.Mulsd(Intermediate[k - 1], Working[start + k - 1]), sum);
                    sum = IvpLinearSystem.Addsd(sum, IvpLinearSystem.Mulsd(Working[start + k - 2], Intermediate[k - 2]));
                    sum = IvpLinearSystem.Addsd(sum, IvpLinearSystem.Mulsd(Working[start + k - 3], Intermediate[k - 3]));
                }
            }

            for (; k > row; k--)
            {
                sum = IvpLinearSystem.Addsd(sum, IvpLinearSystem.Mulsd(Working[start + k], Intermediate[k]));
            }

            Intermediate[row] -= sum;
            Solution[row] = Intermediate[row];
        }
    }

    /// <summary>
    /// <c>FUN_1800a5b80</c>: the block grows by the row the caller left in <see cref="Working"/> and the column it left in
    /// <see cref="Right"/>.
    /// </summary>
    /// <returns>False when the new pivot vanishes.</returns>
    internal bool Grow()
    {
        Array.Copy(Right, Scratch, Size);
        ApplyInverse();

        int last = Size;

        for (int row = last - 1; row >= 0; row--)
        {
            Working[row * Stride + last] = Intermediate[row];
        }

        for (int row = last - 1; row >= 0; row--)
        {
            Inverse[row * Stride + last] = 0d;
        }

        Array.Clear(Inverse, last * Stride, last);
        Inverse[last * Stride + last] = 1d;
        Size = last + 1;

        for (int column = 0; column < last; column++)
        {
            Eliminate(column, last, Working[last * Stride + column]);
        }

        return Normalize(last);
    }

    /// <summary><c>FUN_1800a4870</c>: the contact at <paramref name="column"/> leaves the block, which shrinks by one either way.</summary>
    /// <returns>False when the moved row's pivot vanishes.</returns>
    internal bool Shrink(int column)
    {
        int last = Size - 1;

        for (int row = 0; row < Size; row++)
        {
            IvpLinearSystem.Swap(Inverse, row * Stride + column, row * Stride + last, 1);
        }

        for (int row = 0; row < Size; row++)
        {
            IvpLinearSystem.Swap(Working, row * Stride + column, row * Stride + last, 1);
        }

        for (int row = Size - 2; row > column; row--)
        {
            double factor = Working[row * Stride + column];

            if (!IvpLinearSystem.IsZeroOrUnordered(factor))
            {
                IvpLinearSystem.AddScaled(Inverse, row * Stride, Inverse, last * Stride, -factor, Size);
            }

            Working[row * Stride + column] = 0d;
        }

        if (!Normalize(column))
        {
            IvpLinearSystem.AddScaled(Inverse, column * Stride, Inverse, last * Stride, 1d, Size);
            Working[column * Stride + column] = 1d;
        }

        for (int step = column; step < last; step++)
        {
            double factor = Working[last * Stride + step];

            if (!IvpLinearSystem.IsZeroOrUnordered(factor))
            {
                Eliminate(step, last, factor);
            }
        }

        double diagonal = Inverse[last * Stride + last];

        if (!(Math.Abs(diagonal) >= Epsilon))
        {
            Size--;

            return false;
        }

        IvpLinearSystem.ScaleRun(Inverse, last * Stride, Size, 1d / diagonal);
        Inverse[last * Stride + last] = 1d;

        for (int row = last - 1; row >= 0; row--)
        {
            double factor = Inverse[row * Stride + last];

            if (IvpLinearSystem.IsZeroOrUnordered(factor))
            {
                continue;
            }

            IvpLinearSystem.AddScaled(Inverse, row * Stride, Inverse, last * Stride, -factor, Size);
            Inverse[row * Stride + last] = 0d;
        }

        Size--;

        return true;
    }

    /// <summary><c>FUN_1800a7870</c>: <see cref="Intermediate"/> = <see cref="Inverse"/> · <see cref="Scratch"/>, each row summed from its last column.</summary>
    private void ApplyInverse()
    {
        for (int row = Size - 1; row >= 0; row--)
        {
            double sum = 0d;

            for (int k = Size - 1; k >= 0; k--)
            {
                sum = IvpLinearSystem.Addsd(sum, IvpLinearSystem.Mulsd(Inverse[row * Stride + k], Scratch[k]));
            }

            Intermediate[row] = sum;
        }
    }

    /// <summary><c>FUN_1800a7ca0</c>: the row at or below with the strictly largest magnitude in the column, searched from the last, exchanged up in both matrices.</summary>
    private void PivotRows(int column)
    {
        double best = Math.Abs(Working[(Stride + 1) * column]);
        int pivot = column;

        for (int row = Size - 1; row > column; row--)
        {
            double size = Math.Abs(Working[row * Stride + column]);

            if (size > best)
            {
                best = size;
                pivot = row;
            }
        }

        if (pivot == column)
        {
            return;
        }

        IvpLinearSystem.Swap(Working, column * Stride + column, pivot * Stride + column, Size - column);
        IvpLinearSystem.Swap(Inverse, column * Stride, pivot * Stride, Size);
    }

    /// <summary><c>FUN_1800a7990</c>: a row divided by its pivot, which becomes exactly one.</summary>
    private bool Normalize(int column)
    {
        int start = column * Stride;
        double diagonal = Working[start + column];

        if (!(Math.Abs(diagonal) >= Epsilon))
        {
            return false;
        }

        double scale = 1d / diagonal;

        IvpLinearSystem.ScaleRun(Inverse, start, Size, scale);
        IvpLinearSystem.ScaleRun(Working, start + column + 1, Size - column - 1, scale);
        Working[start + column] = 1d;

        return true;
    }

    /// <summary><c>FUN_1800a4630</c>: a row loses <paramref name="factor"/> times the pivot row, in both matrices.</summary>
    private void Eliminate(int column, int row, double factor)
    {
        double negated = -factor;
        int pivotStart = column * Stride;
        int rowStart = row * Stride;

        IvpLinearSystem.AddScaled(Working, rowStart + column + 1, Working, pivotStart + column + 1, negated, Size - column - 1);
        IvpLinearSystem.AddScaled(Inverse, rowStart, Inverse, pivotStart, negated, Size);
        Working[rowStart + column] = 0d;
    }
}
