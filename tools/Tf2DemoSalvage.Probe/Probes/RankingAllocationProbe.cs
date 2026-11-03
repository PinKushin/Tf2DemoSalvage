using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether a warm traced ranking (<see cref="LocalLights.Strongest"/>) allocates on its own thread while another thread
/// churns the heap and forces gen-2 collections — the condition a full parallel test run gives it (B667 round 4).
/// </summary>
/// <remarks>
/// Each trial counts <see cref="GC.GetAllocatedBytesForCurrentThread"/> over 1,000 calls, the shape
/// `Strongest_TracedAndWarm_AllocatesNothing` measures, and every non-zero trial is printed with its bytes. **The
/// control** is the quiet pass first, with no churn: it must print zero, or the instrument counts something else. Run it
/// under `DOTNET_GCHeapHardLimit` so the churn's live set makes the GC report high memory load, which is when
/// `ArrayPool.Shared` trims everything it holds.
/// <code>
///   ranking-alloc [trials] [live MB]
/// </code>
/// </remarks>
public sealed class RankingAllocationProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "ranking-alloc";

    /// <inheritdoc/>
    public string Summary => "does a warm traced LocalLights ranking allocate under gen-2 churn on another thread: ranking-alloc [trials] [live MB]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        int trials = arguments.Count > 0 ? int.Parse(arguments[0], CultureInfo.InvariantCulture) : 200;
        int liveMegabytes = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 200;

        List<BspWorldLight> lights =
        [
            Lamp((64f, 0f, 0f), 900f), Lamp((-64f, 0f, 0f), 800f), Lamp((0f, 64f, 0f), 700f),
            Lamp((0f, -64f, 0f), 700f), Lamp((0f, 0f, 64f), 600f), Lamp((0f, 0f, -64f), 500f),
        ];

        Func<int, bool> keeps = static index => index % 2 == 1;
        LocalLight[] into = new LocalLight[LocalLights.MaximumLocalLights];

        for (int call = 0; call < 200; call++)
        {
            LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: keeps);
        }

        output.WriteLine($"quiet: {Trials(trials, lights, into, keeps, output, rank: true)} of {trials} trials allocated");

        foreach (bool forced in (ReadOnlySpan<bool>)[true, false])
        {
            using CancellationTokenSource stop = new();
            Thread churn = new(() => Churn(liveMegabytes, forced, stop.Token)) { IsBackground = true };
            churn.Start();

            string how = forced ? "forced blocking gen-2s" : "natural collections, small and large objects";
            output.WriteLine($"{how}, ranking: {Trials(trials, lights, into, keeps, output, rank: true)} of {trials} trials allocated");

            // The instrument's own control: a window that calls nothing must read zero under the same churn.
            output.WriteLine($"{how}, empty window: {Trials(trials, lights, into, keeps, output, rank: false)} of {trials} trials allocated");

            stop.Cancel();
            churn.Join();
        }
        output.WriteLine($"gen-2 collections in all: {GC.CollectionCount(2)}; memory load {GC.GetGCMemoryInfo().MemoryLoadBytes / (1 << 20)} MB of {GC.GetGCMemoryInfo().HighMemoryLoadThresholdBytes / (1 << 20)} MB high");
    }

    private static int Trials(int trials, List<BspWorldLight> lights, LocalLight[] into, Func<int, bool> keeps, TextWriter output, bool rank)
    {
        int allocated = 0;
        double sink = 0;

        for (int trial = 0; trial < trials; trial++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int call = 0; call < 1000; call++)
            {
                if (rank)
                {
                    LocalLights.Strongest(lights, 0f, 0f, 0f, into, traceKeeps: keeps);
                }
                else
                {
                    // About as long as a ranking, calling nothing.
                    for (int spin = 0; spin < 200; spin++)
                    {
                        sink += Math.Sqrt(spin + call);
                    }
                }
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

            if (bytes != 0)
            {
                allocated++;
                output.WriteLine($"  trial {trial}: {bytes} B");
            }
        }

        GC.KeepAlive(sink);
        return allocated;
    }

    /// <summary>
    /// Holds a live set of the given size and keeps replacing pieces of it, with a storm of small objects between; forcing a
    /// blocking gen-2 collection now and then, or leaving the GC to collect as it would in a test run.
    /// </summary>
    private static void Churn(int liveMegabytes, bool forced, CancellationToken stop)
    {
        byte[][] live = new byte[liveMegabytes][];
        object[] small = new object[4096];
        int next = 0;

        while (!stop.IsCancellationRequested)
        {
            live[next++ % live.Length] = new byte[1 << 20];

            for (int each = 0; each < small.Length; each++)
            {
                small[each] = new object[8];
            }

            if (forced && next % 64 == 0)
            {
#pragma warning disable S1215 // The collection is the condition this probe measures under.
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
#pragma warning restore S1215
            }
        }

        GC.KeepAlive(live);
    }

    private static BspWorldLight Lamp((float X, float Y, float Z) origin, float bright) =>
        new(
            origin, (bright, bright, bright), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f);
}
