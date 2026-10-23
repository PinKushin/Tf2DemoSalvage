using System;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary><see cref="BspVisibility"/>'s behaviour that is ours, not Valve's: what the engine's code says is in
/// <see cref="BspVisibilityConformanceTests"/>.</summary>
public sealed class BspVisibilityTests
{
    /// <remarks>
    /// **One lump, read from four threads at once** (B581 rung 2). Every `WorldCulling.Twin()` shares the map's one
    /// <see cref="BspVisibility"/>, and its row cache fills on first use, so the game thread's cull and the render thread's
    /// monitor cull insert into it concurrently. A plain dictionary corrupts or throws under that. Every answer is compared
    /// with a serial reading of a second, untouched instance.
    /// </remarks>
    [Test]
    public void Visible_FromFourThreadsAtOnce_AnswersAsOneThreadDoes()
    {
        const int Clusters = 4096;
        byte[] row = new byte[Clusters / 8];

        for (int at = 0; at < row.Length; at++)
        {
            row[at] = (byte)((at * 37) ^ (at >> 2));
        }

        byte[] lump = BspVisibilityConformanceTests.Lump(Clusters, row);
        BspVisibility reference = BspVisibility.FromLump(lump);
        bool[] expected = new bool[Clusters];

        for (int from = 0; from < Clusters; from++)
        {
            expected[from] = reference.Visible(from, (from * 11) % Clusters);
        }

        BspVisibility shared = BspVisibility.FromLump(lump);
        int wrong = 0;
        Task[] readers = new Task[4];

        for (int thread = 0; thread < readers.Length; thread++)
        {
            // Odd strides over a power of two: every thread visits every cluster, each in its own order.
            int stride = (thread * 2) + 1;

            readers[thread] = Task.Run(() =>
            {
                for (int step = 0; step < Clusters; step++)
                {
                    int from = (step * stride) % Clusters;

                    if (shared.Visible(from, (from * 11) % Clusters) != expected[from])
                    {
                        Interlocked.Increment(ref wrong);
                    }
                }
            });
        }

        Task.WaitAll(readers, TimeSpan.FromSeconds(30)).ShouldBeTrue("a corrupted dictionary can loop for ever");
        wrong.ShouldBe(0);
    }
}
