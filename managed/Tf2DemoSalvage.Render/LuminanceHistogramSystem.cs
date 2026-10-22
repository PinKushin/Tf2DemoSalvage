using System;

namespace Tf2DemoSalvage.Render;

/// <summary>The device half of the histogram: one occlusion query per entry, counting the pixels in a luminance range.</summary>
public interface IHistogramQueries
{
    /// <summary><c>CHistogram_entry_t::IssueQuery</c>: count this frame's region pixels whose luminance is in range.</summary>
    /// <param name="entry">The entry, 0 to 16.</param>
    /// <param name="min">The lowest luminance counted, inclusive.</param>
    /// <param name="max">The highest, inclusive.</param>
    public void Issue(int entry, float min, float max);

    /// <summary><c>OcclusionQuery_GetNumPixelsRendered</c>, not waiting: the count, or null while the device has none.</summary>
    /// <param name="entry">An entry issued earlier.</param>
    /// <returns>The count, or null — the engine's -1.</returns>
    public int? TryRead(int entry);
}

/// <summary><c>CLuminanceHistogramSystem</c> under <c>mat_tonemap_algorithm 1</c>, without a device (B575).</summary>
/// <remarks>
/// *Read from published source*, `viewpostprocess.cpp:382-613`, `:814-896`. Seventeen entries — sixteen bins and one
/// spanning 0 to 100000 that algorithm 1 never reads but still queues. Each frame takes the count of every entry in
/// flight that is past <c>m_frame_queued + 2</c> and has answered, then issues ONE query (<c>MAX_QUERIES_PER_FRAME</c>):
/// the first never-issued entry, else the finished entry queued longest ago. So in the steady state each bin is
/// re-counted every seventeen frames and its count is three to nineteen frames old, each bin from a different frame.
///
/// Bin states survive <c>ResetToneMapping</c> (`:1121-1126` resets only the scale and its average); only an algorithm
/// change resets them (`:856-861`), which this viewer never makes.
/// </remarks>
public sealed class LuminanceHistogramSystem
{
    /// <summary><c>N_LUMINANCE_RANGES_NEW</c>.</summary>
    public const int Entries = AutoExposure.Bins + 1;

    private const int MaxQueriesPerFrame = 1;

    private readonly State[] _state = new State[Entries];

    private readonly int[] _queuedIn = new int[Entries];

    private readonly int[] _counts = new int[Entries];

    /// <summary>Whether each bin has answered a query issued after <see cref="MarkStale"/>.</summary>
    private readonly bool[] _fresh = new bool[Entries];

    private int _frame;

    private int _staleFrom;

    private enum State
    {
        Initial,
        FirstQueryInFlight,
        QueryInFlight,
        QueryDone,
    }

    /// <summary><c>m_npixels_in_range</c> of each entry, as last answered.</summary>
    public ReadOnlySpan<int> Counts => _counts;

    /// <summary>
    /// Frames since every bin last answered a query issued after <see cref="MarkStale"/>; zero until they have. Ours, for
    /// a still capture (B520): the engine never asks for one settled frame.
    /// </summary>
    public int FramesFresh { get; private set; }

    /// <summary><c>ContainsValidData</c>: QUERY_DONE or QUERY_IN_FLIGHT.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Whether its count is a measurement.</returns>
    public bool IsValid(int entry) => _state[entry] is State.QueryDone or State.QueryInFlight;

    /// <summary>The scale was reset: counts measured before now stop being fresh.</summary>
    public void MarkStale()
    {
        Array.Clear(_fresh);
        FramesFresh = 0;
        _staleFrom = _frame;
    }

    /// <summary><c>CLuminanceHistogramSystem::Update</c>, once a frame.</summary>
    /// <param name="queries">The device.</param>
    /// <exception cref="ArgumentNullException"><paramref name="queries"/> is null.</exception>
    public void Update(IHistogramQueries queries)
    {
        ArgumentNullException.ThrowIfNull(queries);

        int issued = 0;
        _frame++;

        for (int i = 0; i < Entries; i++)
        {
            switch (_state[i])
            {
                case State.Initial when issued < MaxQueriesPerFrame:
                    Issue(queries, i);
                    issued++;
                    break;

                case State.FirstQueryInFlight or State.QueryInFlight when _frame > _queuedIn[i] + 2:
                    // "-1=query not finished. wait until next time"
                    if (queries.TryRead(i) is { } pixels)
                    {
                        _counts[i] = pixels;
                        _state[i] = State.QueryDone;
                        _fresh[i] |= _queuedIn[i] > _staleFrom;
                    }

                    break;
            }
        }

        // "now, issue queries for the oldest finished queries we have"
        while (issued < MaxQueriesPerFrame)
        {
            int oldest = -1;

            for (int i = 0; i < Entries; i++)
            {
                if (_state[i] == State.QueryDone && (oldest < 0 || _queuedIn[i] < _queuedIn[oldest]))
                {
                    oldest = i;
                }
            }

            if (oldest < 0)
            {
                break;
            }

            Issue(queries, oldest);
            issued++;
        }

        FramesFresh = Array.IndexOf(_fresh, false, 0, AutoExposure.Bins) < 0 ? FramesFresh + 1 : 0;
    }

    private void Issue(IHistogramQueries queries, int entry)
    {
        // `:877-882` the bins; `:426` bin 15's ceiling, "Count all pixels >1.0 as 1.0"; `:890-891` the last bucket.
        (float min, float max) = (AutoExposure.BinMin(entry), AutoExposure.BinMax(entry));

        if (entry == AutoExposure.Bins - 1)
        {
            max = 10000f;
        }
        else if (entry == AutoExposure.Bins)
        {
            (min, max) = (0f, 100000f);
        }

        queries.Issue(entry, min, max);
        _state[entry] = _state[entry] == State.Initial ? State.FirstQueryInFlight : State.QueryInFlight;
        _queuedIn[entry] = _frame;
    }
}
