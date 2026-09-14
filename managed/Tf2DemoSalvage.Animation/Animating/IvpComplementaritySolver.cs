using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// IVP's constraint solver for a heap of contacts: pushes <c>x ≥ 0</c> with residuals <c>w = Mx − b ≥ 0</c> and <c>xᵢwᵢ = 0</c> —
/// <c>FUN_1800a5e60</c> and everything under it (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The constraint solver's setup* and *loop*). A principal pivoting loop:
/// positions `[0, ActiveCount)` of <see cref="Order"/> are pushing with their residual held at zero, `[ActiveCount, Settled)`
/// have settled at no push, the one at <see cref="Settled"/> is being brought in and the rest wait. The active block's inverse
/// is kept by <see cref="IvpActiveInverse"/>; when that bookkeeping fails <see cref="InverseState"/> marks it stale and the
/// block is eliminated from scratch until a later rebuild succeeds.
///
/// **One aliasing is kept on purpose**: the residual check writes into the inverse's <c>+0x38</c>, because <c>FUN_1800a5e60</c>
/// points the solver's <c>+0x30</c> at the same array.
/// </remarks>
internal sealed class IvpComplementaritySolver
{
    /// <summary><c>(double)1e-7f</c>, the solver's <c>+0x0</c> and its views' epsilon.</summary>
    private const double Epsilon = (double)1e-7f;

    /// <summary><c>+0x18</c>: a step longer than this restarts.</summary>
    private const double Longest = 10000d;

    /// <summary><c>DAT_1800a8200</c>'s bound on steps and restarts together.</summary>
    private const int MostSteps = 250;

    /// <summary>How many steps pass between checks of the whole system.</summary>
    private const int StepsBetweenChecks = 7;

    /// <summary><c>+0x10</c>: <c>0x3f1a36e2f0400000</c>, not a widened float.</summary>
    private static readonly double Tolerance = BitConverter.Int64BitsToDouble(0x3f1a36e2f0400000);

    /// <summary><c>DAT_1800eedc0</c>: the step when the entering contact alone would not bound it.</summary>
    private static readonly double Unbounded = BitConverter.Int64BitsToDouble(0x54e6dc186ef9f45c);

    private readonly double[] _values;
    private readonly double[] _rightHandSide;
    private readonly double[] _result;
    private readonly int _size;
    private readonly double[] _directionX;
    private readonly double[] _directionW;
    private readonly double[] _trialX;
    private readonly double[] _trialW;
    private readonly IvpActiveInverse _inverse;
    private readonly IvpLinearSystem _view;
    private readonly IvpLinearSystem _full;

    internal IvpComplementaritySolver(double[] values, double[] rightHandSide, double[] result, int size)
    {
        _values = values;
        _rightHandSide = rightHandSide;
        _result = result;
        _size = size;
        _directionX = new double[size];
        _directionW = new double[size];
        _trialX = new double[size];
        _trialW = new double[size];
        Residual = new double[size];
        Order = new int[size];
        Position = new int[size];
        _inverse = new IvpActiveInverse(size);
        _view = new IvpLinearSystem(size, size) { Epsilon = Epsilon };
        _full = new IvpLinearSystem(values, _trialX, _trialW, size, size) { Epsilon = Epsilon };
    }

    private enum Outcome
    {
        Top,
        Restart,
        Solved,
        Failed,
    }

    /// <summary><c>+0x48</c>: each contact's residual.</summary>
    internal double[] Residual { get; }

    /// <summary><c>+0x68</c>: the contact at each position.</summary>
    internal int[] Order { get; }

    /// <summary><c>+0x70</c>: each contact's position.</summary>
    internal int[] Position { get; }

    /// <summary><c>+0x80</c>.</summary>
    internal int ActiveCount { get; private set; }

    /// <summary><c>+0x88</c>.</summary>
    internal int Settled { get; private set; }

    /// <summary><c>+0xa4</c>: zero while the inverse is current, one or two while it is stale.</summary>
    internal int InverseState { get; private set; }

    /// <summary><c>+0xf4</c>.</summary>
    internal int InverseSize => _inverse.Size;

    /// <summary><c>+0x90</c>: how many directions were eliminated from scratch.</summary>
    internal int Eliminations { get; private set; }

    /// <summary><c>+0x94</c>, <c>+0x98</c>, <c>+0x9c</c> and <c>+0xa0</c>: the shuffle's four counters.</summary>
    internal (int ActiveFirst, int ActiveSecond, int WaitingFirst, int WaitingSecond) Shuffles { get; private set; }

    /// <summary><c>FUN_1800a5e60</c> from its <c>FUN_1800a4700</c> allocation on: every push starts at zero, the first <paramref name="warm"/> contacts start active.</summary>
    /// <returns>True when the loop finished, false when it gave up.</returns>
    internal bool Solve(int warm)
    {
        for (int i = 0; i < _size; i++)
        {
            Order[i] = i;
            Position[i] = i;
            _result[i] = 0d;
            Residual[i] = -_rightHandSide[i];
        }

        Warm(warm);

        return Pivot();
    }

    /// <summary><c>FUN_1800a9010</c>.</summary>
    private void Warm(int count)
    {
        ActiveCount = count;
        Settled = count;
        _inverse.Size = count;
        Array.Clear(_trialX, 0, _size);

        if (!Rebuild())
        {
            ActiveCount = 0;
            Settled = 0;
            _inverse.Size = 0;

            return;
        }

        InverseState = 0;

        while (true)
        {
            _inverse.Solve();

            for (int j = 0; j < ActiveCount; j++)
            {
                _trialX[Order[j]] = _inverse.Solution[j];
            }

            MeasureResidual();

            int found = ActiveCount - 1;

            while (found >= 0 && !(0d > _result[Order[found]]))
            {
                found--;
            }

            if (found < 0)
            {
                return;
            }

            int index = Order[found];

            _result[index] = 0d;
            _trialX[index] = 0d;
            Settled--;

            int last = ActiveCount - 1;

            ActiveCount = last;
            Exchange(last, found);

            if (InverseState == 0)
            {
                if (!_inverse.Shrink(found))
                {
                    InverseState = 2;
                }
            }
            else
            {
                _inverse.Size--;
            }

            if (InverseState > 0)
            {
                Array.Clear(Residual, 0, _size);
                Array.Clear(_result, 0, _size);
                ActiveCount = 0;
                Settled = 0;
                _inverse.Size = 0;

                return;
            }

            for (int j = 0; j < ActiveCount; j++)
            {
                _inverse.Right[j] = _rightHandSide[Order[j]];
            }
        }
    }

    /// <summary><c>FUN_1800a8200</c>.</summary>
    private bool Pivot()
    {
        int total = 0;
        int small = 0;
        int countdown = StepsBetweenChecks;
        bool stepped = false;
        Outcome outcome = Outcome.Top;

        while (true)
        {
            if (outcome == Outcome.Top)
            {
                if (stepped)
                {
                    total++;
                }

                if (total > MostSteps)
                {
                    return false;
                }

                if (countdown != 0)
                {
                    countdown--;
                }
                else if (Verify())
                {
                    countdown = StepsBetweenChecks;
                }
                else
                {
                    outcome = Outcome.Restart;
                    continue;
                }
            }
            else if (outcome == Outcome.Restart)
            {
                total++;

                if (total > MostSteps)
                {
                    return false;
                }

                small = 0;

                if (!Restart())
                {
                    return false;
                }

                countdown = StepsBetweenChecks;
            }

            outcome = Next(ref stepped, ref small, ref countdown);

            if (outcome is Outcome.Solved or Outcome.Failed)
            {
                return outcome == Outcome.Solved;
            }
        }
    }

    /// <summary>One pass of <c>FUN_1800a8200</c> from the contact at <see cref="Settled"/>.</summary>
    private Outcome Next(ref bool stepped, ref int small, ref int countdown)
    {
        int next = Settled;

        if (next >= _size)
        {
            return Outcome.Solved;
        }

        int entering = Order[next];

        if (InverseState > 0)
        {
            if (InverseState == 1 && stepped)
            {
                Shuffle();

                if (Rebuild())
                {
                    InverseState = 0;
                }
            }
            else
            {
                InverseState = 1;
            }
        }

        double residual = Residual[entering];

        if (!(Math.Abs(residual) >= Epsilon))
        {
            if (!(Math.Abs(_result[entering]) >= Epsilon))
            {
                Settle(ref stepped, ref countdown);
            }
            else
            {
                Join(next, ref countdown);
            }

            return Outcome.Top;
        }

        if (residual >= 0d)
        {
            Settle(ref stepped, ref countdown);

            return Outcome.Top;
        }

        if (!Direction())
        {
            return Outcome.Restart;
        }

        _directionX[entering] = 1d;
        MeasureDeltas();

        for (int j = 0; j < ActiveCount; j++)
        {
            _directionW[Order[j]] = 0d;
        }

        residual = Residual[entering];

        double change = _directionW[entering];
        double step;
        int best;

        if (!(-residual >= IvpMath.Mulsd(change, Longest)))
        {
            step = IvpMath.Mulsd(-1d / change, residual);
            best = next;
        }
        else
        {
            step = Unbounded;
            best = -1;
        }

        double shortest = 0d;
        bool immediate = false;

        for (int i = 0; i < ActiveCount; i++)
        {
            int index = Order[i];
            double along = _directionX[index];

            if (along >= -Epsilon)
            {
                continue;
            }

            double pushed = _result[index];
            double ratio = IvpMath.Mulsd(-1d / along, pushed);

            if (!(Math.Abs(ratio) >= Epsilon) && !(pushed >= Epsilon))
            {
                best = i;
                shortest = ratio;
                immediate = true;

                break;
            }

            if (!(ratio >= IvpMath.Addsd(step, Epsilon)))
            {
                step = ratio;
                best = i;
            }
        }

        if (!immediate)
        {
            for (int i = ActiveCount; i < Settled; i++)
            {
                int index = Order[i];
                double along = _directionW[index];

                if (along >= -Epsilon)
                {
                    continue;
                }

                double ratio = IvpMath.Mulsd(-1d / along, Residual[index]);

                if (!(ratio >= step - Epsilon))
                {
                    step = ratio;
                    best = i;
                }
            }

            shortest = IvpLinearSystem.Maxsd(step, 0d);

            if (best < 0)
            {
                return Outcome.Failed;
            }
        }

        if (shortest > Longest)
        {
            return Outcome.Restart;
        }

        if (shortest >= Epsilon)
        {
            small = 0;
        }
        else if (++small > (_size >> 1) + 2)
        {
            return Outcome.Restart;
        }

        stepped = true;
        Take(shortest);

        if (best < ActiveCount)
        {
            _result[Order[best]] = 0d;

            int last = ActiveCount - 1;

            ActiveCount = last;
            Exchange(last, best);
            Release(best);

            return Outcome.Top;
        }

        if (best < Settled)
        {
            if (shortest > Epsilon)
            {
                DropReleased();
            }

            Residual[Order[best]] = 0d;
            Exchange(ActiveCount, best);
            Extend(Admit());

            return Outcome.Top;
        }

        Join(best, ref countdown);

        return Outcome.Top;
    }

    /// <summary>The step along the direction, and the clamps after it.</summary>
    private void Take(double shortest)
    {
        for (int i = 0; i < _size; i++)
        {
            Residual[i] = IvpMath.Addsd(IvpMath.Mulsd(shortest, _directionW[i]), Residual[i]);
            _result[i] = IvpMath.Addsd(IvpMath.Mulsd(shortest, _directionX[i]), _result[i]);
        }

        for (int i = ActiveCount; i < Settled; i++)
        {
            int index = Order[i];

            if (0d > Residual[index])
            {
                Residual[index] = 0d;
            }
        }

        for (int i = 0; i < ActiveCount; i++)
        {
            int index = Order[i];

            if (0d > _result[index])
            {
                _result[index] = 0d;
            }
        }
    }

    /// <summary>The contact at <paramref name="position"/> becomes active; the waiting count advances.</summary>
    private void Join(int position, ref int countdown)
    {
        DropReleased();

        int index = Order[position];

        Residual[index] = 0d;

        if (0d > _result[index])
        {
            _result[index] = 0d;
        }

        Exchange(ActiveCount, position);
        Settled++;

        int joined = Admit();

        if (Settled >= _size)
        {
            countdown = 0;

            return;
        }

        Extend(joined);
    }

    /// <summary>The contact at <see cref="Settled"/> settles at no push.</summary>
    private void Settle(ref bool stepped, ref int countdown)
    {
        stepped = false;

        int index = Order[Settled];

        _result[index] = 0d;

        if (0d > Residual[index])
        {
            Residual[index] = 0d;
        }

        Settled++;

        if (Settled >= _size)
        {
            countdown = 0;
        }
    }

    /// <summary>Every active contact whose push fell below epsilon leaves, its push zeroed.</summary>
    private void DropReleased()
    {
        int i = 0;

        while (i < ActiveCount)
        {
            int index = Order[i];

            if (!(Epsilon > _result[index]))
            {
                i++;
                continue;
            }

            _result[index] = 0d;

            int last = ActiveCount - 1;

            Exchange(i, last);
            ActiveCount = last;
            Release(i);
        }
    }

    /// <summary>The inverse loses the block's row and column at <paramref name="position"/>, or goes on counting while stale.</summary>
    private void Release(int position)
    {
        if (InverseState != 0)
        {
            _inverse.Size--;
        }
        else if (!_inverse.Shrink(position))
        {
            InverseState = 2;
        }
    }

    /// <summary>The contact just past the active block joins it.</summary>
    /// <returns>That contact.</returns>
    private int Admit()
    {
        int joined = Order[ActiveCount];

        ActiveCount++;

        return joined;
    }

    /// <summary>The inverse grows by <paramref name="joined"/>'s row and column, or goes on counting while stale.</summary>
    private void Extend(int joined)
    {
        if (InverseState != 0)
        {
            _inverse.Size++;

            return;
        }

        int row = _inverse.Size * _inverse.Stride;

        for (int r = 0; r < ActiveCount; r++)
        {
            _inverse.Working[row + r] = _values[joined * _size + Order[r]];
        }

        for (int r = 0; r < ActiveCount - 1; r++)
        {
            _inverse.Right[r] = _values[Order[r] * _size + joined];
        }

        if (!_inverse.Grow())
        {
            InverseState = 2;
        }
    }

    /// <summary><c>FUN_1800a5740</c>: how the active pushes change per unit push of the entering contact.</summary>
    private bool Direction()
    {
        Array.Clear(_directionX, 0, _size);

        if (ActiveCount == 0)
        {
            return true;
        }

        int entering = Order[Settled];
        bool solved;

        if (InverseState == 0)
        {
            for (int r = 0; r < ActiveCount; r++)
            {
                _inverse.Right[r] = -_values[Order[r] * _size + entering];
            }

            _inverse.Solve();
            solved = true;
        }
        else
        {
            Eliminations++;
            _view.Rows = ActiveCount;
            _view.Columns = ActiveCount;

            for (int r = 0; r < ActiveCount; r++)
            {
                int index = Order[r];

                _view.RightHandSide[r] = -_values[index * _size + entering];

                for (int c = 0; c < ActiveCount; c++)
                {
                    _view.Values[r * ActiveCount + c] = _values[index * _size + Order[c]];
                }
            }

            solved = _view.Solve();
            Array.Copy(_view.Result, _inverse.Solution, ActiveCount);
        }

        for (int r = 0; r < ActiveCount; r++)
        {
            _directionX[Order[r]] = _inverse.Solution[r];
        }

        _directionX[entering] = 1d;

        return solved;
    }

    /// <summary><c>FUN_1800a7530</c>: how every inactive residual changes per unit step.</summary>
    private void MeasureDeltas()
    {
        int entering = Order[Settled];

        for (int i = ActiveCount; i < _size; i++)
        {
            int index = Order[i];
            int row = index * _size;
            double sum = 0d;

            for (int j = 0; j < ActiveCount; j++)
            {
                int column = Order[j];

                sum = IvpMath.Addsd(sum, IvpMath.Mulsd(_directionX[column], _values[row + column]));
            }

            _directionW[index] = IvpMath.Addsd(sum, _values[row + entering]);
        }
    }

    /// <summary><c>FUN_1800a59e0</c>: the residuals at the trial pushes, which become the pushes.</summary>
    private void MeasureResidual()
    {
        _full.Multiply();

        for (int i = 0; i < _size; i++)
        {
            _trialW[i] -= _rightHandSide[i];
        }

        for (int j = 0; j < ActiveCount; j++)
        {
            _trialW[Order[j]] = 0d;
        }

        for (int i = _size - 1; i >= 0; i--)
        {
            Residual[i] = _trialW[i];
            _result[i] = _trialX[i];
        }
    }

    /// <summary><c>FUN_1800a7af0</c>: whether the pushes and residuals agree with the whole system.</summary>
    private bool Verify()
    {
        double[] measured = _inverse.Intermediate;

        for (int i = 0; i < _size; i++)
        {
            double sum = 0d;
            int row = i * _size;

            for (int k = _size - 1; k >= 0; k--)
            {
                sum = IvpMath.Addsd(sum, IvpMath.Mulsd(_values[row + k], _result[k]));
            }

            measured[i] = sum - _rightHandSide[i];
        }

        for (int j = 0; j < ActiveCount; j++)
        {
            if (Math.Abs(measured[Order[j]]) > Tolerance)
            {
                return false;
            }
        }

        for (int j = ActiveCount; j < _size; j++)
        {
            int index = Order[j];

            if (Math.Abs(measured[index] - Residual[index]) > Tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>FUN_1800a52d0</c>: the active pushes solved again from the current set until none pushes backwards.</summary>
    private bool Restart()
    {
        do
        {
            _inverse.Size = ActiveCount;
            Array.Clear(_trialX, 0, _size);
            Shuffle();

            if (Rebuild())
            {
                InverseState = 0;
                _inverse.Solve();

                for (int r = 0; r < ActiveCount; r++)
                {
                    _trialX[Order[r]] = _inverse.Solution[r];
                }
            }
            else
            {
                InverseState = 2;
                SortActive();
                Shuffle();
                _view.Rows = ActiveCount;
                _view.Columns = ActiveCount;

                for (int r = 0; r < ActiveCount; r++)
                {
                    int index = Order[r];

                    _view.RightHandSide[r] = _rightHandSide[index];

                    for (int c = 0; c < ActiveCount; c++)
                    {
                        _view.Values[r * ActiveCount + c] = _values[index * _size + Order[c]];
                    }
                }

                if (!_view.Solve())
                {
                    return false;
                }

                for (int r = 0; r < ActiveCount; r++)
                {
                    _trialX[Order[r]] = _view.Result[r];
                }
            }

            MeasureResidual();
        }
        while (Backwards() > 0);

        return true;
    }

    /// <summary><c>FUN_1800a7e80</c>: the active block gathered into the inverse and inverted.</summary>
    private bool Rebuild()
    {
        for (int r = 0; r < ActiveCount; r++)
        {
            int index = Order[r];

            _inverse.Right[r] = _rightHandSide[index];

            for (int c = 0; c < ActiveCount; c++)
            {
                _inverse.Working[r * _inverse.Stride + c] = _values[index * _size + Order[c]];
            }
        }

        return _inverse.Invert();
    }

    /// <summary><c>FUN_1800a4be0</c>: two positions exchanged in the active block, and two among the waiting, to break a cycle.</summary>
    private void Shuffle()
    {
        (int activeFirst, int activeSecond, int waitingFirst, int waitingSecond) = Shuffles;
        int active = ActiveCount;

        if (active < 2)
        {
            return;
        }

        activeFirst = Reduce(activeFirst + 1, active);
        activeSecond = Reduce(activeSecond + 2, active);
        Exchange(activeFirst, activeSecond);
        waitingFirst++;
        waitingSecond += 2;

        int waiting = _size - Settled - 1;

        if (waiting >= 2)
        {
            waitingFirst = Reduce(waitingFirst, waiting);
            waitingSecond = Reduce(waitingSecond, waiting);
            Exchange(Settled + waitingFirst + 1, Settled + waitingSecond + 1);
        }

        Shuffles = (activeFirst, activeSecond, waitingFirst, waitingSecond);
    }

    /// <summary><c>FUN_1800a6160</c>: the active positions insertion-sorted by push, largest first.</summary>
    private void SortActive()
    {
        for (int i = 1; i < ActiveCount; i++)
        {
            for (int j = i; j > 0 && _result[Order[j]] > _result[Order[j - 1]]; j--)
            {
                Exchange(j - 1, j);
            }
        }
    }

    /// <summary><c>FUN_1800a5520</c>: active contacts whose pushes went to zero or backwards leave; settled ones pulling move to the end.</summary>
    /// <returns>How many pushed backwards.</returns>
    private int Backwards()
    {
        int backwards = 0;
        int i = 0;

        while (i < ActiveCount)
        {
            int index = Order[i];
            double pushed = _result[index];

            if (pushed >= Epsilon)
            {
                i++;
                continue;
            }

            if (pushed > -Epsilon)
            {
                _result[index] = 0d;
                Exchange(i, ActiveCount - 1);
            }
            else
            {
                MoveToEnd(i);
                backwards++;
                Settled--;
            }

            ActiveCount--;
            InverseState = 1;
            _inverse.Size--;
        }

        while (i < Settled)
        {
            if (0d > Residual[Order[i]])
            {
                MoveToEnd(i);
                Settled--;
            }
            else
            {
                i++;
            }
        }

        return backwards;
    }

    private static int Reduce(int value, int bound)
    {
        while (value >= bound)
        {
            value -= bound;
        }

        return value;
    }

    private void MoveToEnd(int position)
    {
        for (int r = position + 1; r < _size; r++)
        {
            Exchange(r - 1, r);
        }
    }

    private void Exchange(int first, int second)
    {
        int atFirst = Order[first];
        int atSecond = Order[second];

        Order[first] = atSecond;
        Order[second] = atFirst;
        Position[atSecond] = first;
        Position[atFirst] = second;
    }
}
