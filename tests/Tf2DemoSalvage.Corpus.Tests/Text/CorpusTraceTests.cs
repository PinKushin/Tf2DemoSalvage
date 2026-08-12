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

    [Fact]
    public void NoMessageIsAnonymous()
    {
        // Phase 1's finish line for the message layer. Every message type the corpus contains is
        // now reported with its own fields; none falls back to SkippedMessage, whose rendering is
        // a bare name and a bit count and tells a reader nothing but "something was here".
        //
        // Asserted against the trace text rather than against message types, because the trace
        // is what a reader actually sees - a type could be modelled and still render as nothing
        // useful.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            string[] anonymous =
            [
                .. Trace(path)
                    .Split('\n')
                    .Select(line => line.Trim().TrimEnd(';'))
                    .Where(IsAnonymous)
                    .Distinct(),
            ];

            anonymous.ShouldBeEmpty($"{name}: {string.Join(", ", anonymous.Take(5))}");
        }
    }

    [Fact]
    public void UserCommandsAndConsoleCommandsAreExpandedRatherThanCounted()
    {
        // Both were bare one-line blocks until the payload behind them was decoded, which is a
        // failure mode worth naming: a trace listing `block dem_usercmd tick 72;` reads as
        // complete, because nothing about it says a payload went unread.
        int expanded = 0;

        foreach (string path in Corpus.Files())
        {
            string trace = Trace(path);

            if (!trace.Contains("block dem_usercmd", StringComparison.Ordinal))
            {
                // SourceTV recordings have no player behind the camera and so carry none.
                continue;
            }

            expanded++;
            string name = Path.GetFileName(path);

            // The block must open rather than terminate, and it must carry the resolved command
            // number - the field whose absent form means one rather than zero.
            trace.ShouldContain("block dem_usercmd tick ", Case.Sensitive, name);
            trace.ShouldNotContain("block dem_usercmd tick 0;", Case.Sensitive, name);
            trace.ShouldContain("    command ", Case.Sensitive, name);

            // A player who was moving at all produces angles, and every corpus demo opens with
            // someone already in the world.
            trace.ShouldContain("    angles ", Case.Sensitive, name);
        }

        expanded.ShouldBeGreaterThan(0, "no point-of-view demo reached the trace");
        output.WriteLine($"{expanded} demos expanded their user commands");
    }

    [Fact]
    public void SoundsAreNamedFromTheSoundPrecacheTable()
    {
        // A svc_Sounds body carries an index into soundprecache, never a name, and the table is
        // per-server and per-map - so the number alone is the one part of the sound that does not
        // travel. This is the check that the resolution actually happens on real demos, where the
        // table arrives compressed in the signon stream rather than as a fixture.
        int named = 0;

        foreach (string path in Corpus.Files())
        {
            string trace = Trace(path);
            string name = Path.GetFileName(path);

            if (!trace.Contains("        sound ", StringComparison.Ordinal))
            {
                continue;
            }

            // Every sound line either carries a quoted path or is one this demo genuinely never
            // precached. Requiring at least one named sound per demo with sounds is the assertion
            // that has teeth: a resolver keyed on the wrong table, or on list position instead of
            // the entry index, resolves nothing at all.
            bool anyNamed = trace
                .Split('\n')
                .Any(line => line.TrimStart().StartsWith("sound ", StringComparison.Ordinal) &&
                             line.Contains(".wav", StringComparison.OrdinalIgnoreCase));

            anyNamed.ShouldBeTrue($"{name}: no sound resolved to a precached path");
            named++;
        }

        named.ShouldBeGreaterThan(0, "no demo in the corpus rendered a sound");
        output.WriteLine($"{named} demos resolved sound names");
    }

    /// <summary>Whether a trace line is a bare "name bits N", which is how a skip renders.</summary>
    private static bool IsAnonymous(string line)
    {
        if (!line.StartsWith("svc_", StringComparison.Ordinal) &&
            !line.StartsWith("net_", StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = line.Split(' ');
        return parts.Length == 3 && parts[1] == "bits" && int.TryParse(parts[2], out _);
    }
}
