using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// The trace run against real demos.
/// </summary>
/// <remarks>
/// Deliberately corpus-based rather than fixture-based. Producing a hand-built demo carrying a
/// schema and a matching entity snapshot means writing several interlocking wire formats
/// correctly, and every attempt at that in this project has produced a fixture that parsed to
/// nothing rather than a test that failed usefully. A real demo is both cheaper and stronger
/// evidence here.
/// </remarks>
public sealed class CorpusTraceTests(ITestOutputHelper output)
{
    /// <summary>Traces the opening of a demo, which is enough to exercise every shape.</summary>
    private static string Trace(string path, DemoTraceOptions? options = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands =
        [
            .. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(400),
        ];

        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(writer, Path.GetFileName(path), header, commands, null, options);
        return writer.ToString();
    }

    [Fact]
    public void EveryDemo_TracesWithoutAnUnreadableBlock()
    {
        // The trace reports what it cannot read rather than throwing, so a failure shows up
        // here as text rather than as an exception. That is the point of the format.
        //
        // This briefly carried an exemption for the POV demo, which appeared to contain an
        // unknown message id 1. It did not - svc_BspDecal was overreading by up to 38 bits and
        // the id was garbage produced downstream of that (RISKS B16). The exemption is gone
        // because the reason for it was never real.
        foreach (string path in Corpus.Files())
        {
            string trace = Trace(path);

            trace.ShouldContain("block dem_packet");
            trace.ShouldNotContain("stopped after", Case.Sensitive, Path.GetFileName(path));
        }
    }

    [Fact]
    public void EntitiesAreOff_UnlessAskedFor()
    {
        // The default has to stay cheap: expanding entities turns a 39 MB demo into gigabytes
        // of text, so it cannot be what an unqualified trace produces.
        Trace(Corpus.Files()[0]).ShouldNotContain("        entity ");
    }

    [Fact]
    public void WithEntities_PropertiesAreNamedAndValued()
    {
        // The whole claim of the project in one assertion: the demo carries its own schema, so
        // properties come out named without this parser knowing anything about TF2's layout.
        string trace = Trace(
            Corpus.Files()[0],
            new DemoTraceOptions { IncludeEntities = true, EntitySnapshotLimit = 5 });

        trace.ShouldContain("entity ");
        trace.ShouldContain("DT_");
        trace.ShouldContain("m_flSimulationTime");
    }

    [Fact]
    public void WithoutProperties_EntitiesStillAppearWithACount()
    {
        // The middle setting: which entities a snapshot touched, without the values that make
        // up most of the volume.
        string trace = Trace(
            Corpus.Files()[0],
            new DemoTraceOptions
            {
                IncludeEntities = true,
                IncludeEntityProperties = false,
                EntitySnapshotLimit = 5,
            });

        trace.ShouldContain("props ");
        trace.ShouldNotContain("m_flSimulationTime");
    }

    [Fact]
    public void SnapshotLimit_StopsExpandingAfterTheStatedCount()
    {
        string limited = Trace(
            Corpus.Files()[0],
            new DemoTraceOptions { IncludeEntities = true, EntitySnapshotLimit = 2 });
        string more = Trace(
            Corpus.Files()[0],
            new DemoTraceOptions { IncludeEntities = true, EntitySnapshotLimit = 20 });

        // Past the limit, snapshots still appear as messages - they simply stop being expanded.
        limited.Length.ShouldBeLessThan(more.Length);
        limited.ShouldContain("svc_packetentities");
    }

    [Fact]
    public void ReportTraceShape()
    {
        foreach (string path in Corpus.Files())
        {
            string trace = Trace(path);
            string[] lines = trace.Split('\n');

            output.WriteLine(
                $"{Path.GetFileName(path)}: {lines.Length} lines from 400 commands, " +
                $"{lines.Count(l => l.StartsWith("block", StringComparison.Ordinal))} blocks");
        }

        Corpus.Files().ShouldNotBeEmpty();
    }
}
