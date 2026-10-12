using System;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Which of a few frame copies is queued and which is read, each frame — the states of
/// <c>CLuminanceHistogramSystem::Update</c> (<c>viewpostprocess.cpp:551-613</c>) without a device.
/// </summary>
/// <remarks>
/// *Read from published source.* The engine's entry is INITIAL, IN_FLIGHT or DONE: a query is issued for an entry
/// that is not in flight, read no sooner than <c>cur_query_frame &gt; m_frame_queued + 2</c>, and when the device
/// has no answer it is asked again next frame — "-1=query not finished. wait until next time" — and NOT issued again.
/// Finished entries are re-issued oldest first. Here an entry is a copy of the frame rather than one bin's count.
/// </remarks>
public sealed class HistogramRing
{
    /// <summary>The frame each slot was queued in, or -1 where nothing is in flight.</summary>
    private readonly long[] _queuedIn;

    private long _frame;

    /// <summary>Makes a ring.</summary>
    /// <param name="slots">How many copies there are.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slots"/> is under one.</exception>
    public HistogramRing(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, 1);

        _queuedIn = new long[slots];
        Array.Fill(_queuedIn, -1);
    }

    /// <summary><c>cur_query_frame++</c>.</summary>
    public void BeginFrame() => _frame++;

    /// <summary>A slot with nothing in flight, or null when every copy is still waiting to be read.</summary>
    /// <returns>The slot to copy this frame into.</returns>
    public int? SlotToQueue()
    {
        int free = Array.IndexOf(_queuedIn, -1L);

        return free < 0 ? null : free;
    }

    /// <summary>Marks a slot as queued this frame.</summary>
    /// <param name="slot">The slot <see cref="SlotToQueue"/> gave.</param>
    public void Queued(int slot) => _queuedIn[slot] = _frame;

    /// <summary>The oldest copy in flight that is at least three frames old, or null.</summary>
    /// <returns>The slot to try to read.</returns>
    public int? SlotToRead()
    {
        int oldest = -1;

        for (int slot = 0; slot < _queuedIn.Length; slot++)
        {
            if (_queuedIn[slot] >= 0 && _frame > _queuedIn[slot] + 2 &&
                (oldest < 0 || _queuedIn[slot] < _queuedIn[oldest]))
            {
                oldest = slot;
            }
        }

        return oldest < 0 ? null : oldest;
    }

    /// <summary>Marks a slot as read: nothing is in flight in it, and it may be queued again.</summary>
    /// <param name="slot">The slot <see cref="SlotToRead"/> gave.</param>
    public void Read(int slot) => _queuedIn[slot] = -1;

    /// <summary>Forgets every copy in flight — the copies themselves were released.</summary>
    public void Reset() => Array.Fill(_queuedIn, -1);
}
