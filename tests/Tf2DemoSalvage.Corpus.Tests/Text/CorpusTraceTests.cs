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
public sealed class CorpusTraceTests
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

    [Test]
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

    // Six tests removed on 2026-08-19, all covered by synthetic demos.
    //
    // EntitiesAreOff_UnlessAskedFor and Trace_WithEntities_NamesAndValuesProperties are now
    // EntityAssemblyDemoTests.Trace_ASnapshotWithASchema_ExpandsEntitiesRatherThanCountingThem,
    // which asserts BOTH halves of the opt-in rather than one each.
    //
    // Trace_WithoutProperties_StillShowsEntitiesWithACount and
    // SnapshotLimit_StopsExpandingAfterTheStatedCount are SyntheticTraceOptionTests. They only
    // ever needed a demo with entities in it.
    //
    // Trace_UserAndConsoleCommands_AreExpandedNotCounted is UserCommandTraceTests, which can
    // choose which buttons were pressed - a point-of-view field no SourceTV recording carries.
    //
    // Trace_Sounds_AreNamedFromThePrecacheTable was blocked until svc_CreateStringTable became
    // writable in a fixture; it is SyntheticTraceOptionTests now, with the index chosen so an
    // off-by-one names a different sound rather than the right one.
    //
    // TraceShape_TheCorpus_IsReported was a report whose only assertion guarded the fixture.

    [Test]
    public void Trace_EveryMessage_IsNamed()
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
