using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The collector's own running counters at one moment: pause time, collections by generation, bytes allocated.</summary>
/// <remarks>
/// **Read from the runtime, never estimated** (B243): the pause is <see cref="GC.GetTotalPauseDuration"/>, the
/// counts <see cref="GC.CollectionCount(int)"/>. Finding 82 found the render thread parked in a GC suspension for 41%
/// of its samples, which no phase timer could name because a pause lands inside whichever phase allocated.
/// </remarks>
/// <param name="Pause">Total time the runtime has paused managed threads.</param>
/// <param name="Gen0">Generation 0 collections so far.</param>
/// <param name="Gen1">Generation 1 collections so far.</param>
/// <param name="Gen2">Generation 2 collections so far.</param>
/// <param name="Allocated">Bytes allocated so far, every heap.</param>
/// <param name="ThisThread">
/// Bytes allocated so far by the thread that took the reading, from <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
/// The viewer reads on the render thread, so this is the frame's own allocation, apart from the corpse worker's
/// (finding 87: the target metric for removing per-frame allocation).
/// </param>
public readonly record struct GcReading(TimeSpan Pause, int Gen0, int Gen1, int Gen2, long Allocated, long ThisThread)
{
    /// <summary>The counters now. Read both ends on the same thread, or <see cref="ThisThread"/> means nothing.</summary>
    public static GcReading Now() =>
        new(GC.GetTotalPauseDuration(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalAllocatedBytes(),
            GC.GetAllocatedBytesForCurrentThread());

    /// <summary>The cost between an earlier reading and this one, per frame.</summary>
    /// <param name="start">The earlier reading.</param>
    /// <param name="frames">Frames between the two; positive.</param>
    /// <returns>One line.</returns>
    public string Since(GcReading start, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frames);

        // A gen2 collection counts as gen1 and gen0 too, so each column is collections OF that generation.
        int gen2 = Gen2 - start.Gen2;
        int gen1 = Gen1 - start.Gen1 - gen2;
        int gen0 = Gen0 - start.Gen0 - gen1 - gen2;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"gc: paused {(Pause - start.Pause).TotalMilliseconds / frames:0.00} ms a frame; collections gen0 {gen0}, gen1 {gen1}, gen2 {gen2}; " +
            $"allocated {(Allocated - start.Allocated) / 1048576d / frames:0.00} MB a frame; " +
            $"render thread {(ThisThread - start.ThisThread) / frames:N0} bytes a frame");
    }
}
