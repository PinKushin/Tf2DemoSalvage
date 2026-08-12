using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// PacketEntities headers read from real demos.
/// </summary>
/// <remarks>
/// Only the header is decoded so far, but its fields are checkable against things known from
/// elsewhere: entity counts against the engine's edict limit, and the snapshot pattern against
/// how delta compression has to work — one full snapshot, then deltas referencing earlier ticks.
/// </remarks>
public sealed class CorpusPacketEntitiesTests
{
    /// <summary>MAX_EDICTS. A server cannot describe more entities than this.</summary>
    private const int MaxEdicts = 2048;

    [Test]
    public void EntityCountsStayWithinEngineLimits()
    {
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<Corpus.SnapshotSummary> snapshots = Corpus.FirstSnapshots(path, 400);

            // Asserted, not assumed. A loop over an empty list passes identically to one that
            // ran and was satisfied, and RISKS B20 is exactly that mistake: a helper stopped
            // yielding anything for one demo and every test built on it kept passing.
            snapshots.ShouldNotBeEmpty(Path.GetFileName(path));

            foreach (Corpus.SnapshotSummary message in snapshots)
            {
                message.MaxEntries.ShouldBeInRange(1, MaxEdicts);
                message.UpdatedEntries.ShouldBeInRange(0, message.MaxEntries);
                message.LengthBits.ShouldBeGreaterThan(0);
            }
        }
    }

    [Test]
    public void AlmostEverySnapshotIsADelta()
    {
        // Delta compression has to start from a full snapshot somewhere, but we cannot yet
        // assert it is the *first* one we see: the true first snapshot arrives during signon,
        // which still stops at svc_SignonState, so the earliest one reachable is already a
        // delta. That assertion becomes possible once signon decodes fully.
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<Corpus.SnapshotSummary> snapshots = Corpus.FirstSnapshots(path, 200);
            snapshots.ShouldNotBeEmpty(Path.GetFileName(path));

            snapshots.Count(s => s.IsDelta)
                .ShouldBeGreaterThan(snapshots.Count / 2, Path.GetFileName(path));
            snapshots.Where(s => s.IsDelta).ShouldAllBe(s => s.DeltaFromTick != null);
            snapshots.Where(s => !s.IsDelta).ShouldAllBe(s => s.DeltaFromTick == null);
        }
    }

    [Test]
    public void DeltasReferenceTicksThatHaveAlreadyHappened()
    {
        // A delta against a future tick would mean the header is being misread.
        //
        // The comparison must be against the *server* tick from net_Tick in the same packet,
        // not the container command's tick. Those are different clocks - the container counts
        // from the start of the recording while net_Tick carries the server's own counter, and
        // they differ by a constant offset of around 12,640 in this corpus. Comparing them
        // directly is what an earlier version of this test did, and it failed for that reason
        // rather than because anything was wrong with the parser.
        foreach (string path in Corpus.Files())
        {
            foreach (Corpus.SnapshotSummary message in Corpus.FirstSnapshots(path, 200))
            {
                if (message.DeltaFromTick is int from)
                {
                    from.ShouldBeLessThan(
                        message.ServerTick, $"{Path.GetFileName(path)}: delta from a future tick");
                }
            }
        }
    }

    [Test]
    public void ReportSnapshotShape()
    {
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<Corpus.SnapshotSummary> snapshots = Corpus.FirstSnapshots(path, 200);
            snapshots.ShouldNotBeEmpty(Path.GetFileName(path));

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: first snapshot updates {snapshots[0].UpdatedEntries} " +
                $"of {snapshots[0].MaxEntries} entities in {snapshots[0].LengthBits} bits");
            TestContext.Out.WriteLine(
                $"  later snapshots average {snapshots.Skip(1).Average(s => s.UpdatedEntries):F1} " +
                $"entities and {snapshots.Skip(1).Average(s => s.LengthBits):F0} bits");
            TestContext.Out.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

}
