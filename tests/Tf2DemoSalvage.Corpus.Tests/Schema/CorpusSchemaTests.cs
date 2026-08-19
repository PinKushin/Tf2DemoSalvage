using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The embedded entity schema, parsed out of real demos.
/// </summary>
/// <remarks>
/// There is no length prefix inside the table stream, so a wrong field width does not fail —
/// it turns every later table into noise. Recognisable table and property names are therefore
/// the proof, and a trailing server class list that matches the count <c>svc_ServerInfo</c>
/// independently reported is the strongest single check available.
/// </remarks>
public sealed class CorpusSchemaTests
{
    [Test]
    public void LaunchBuildSourceTv_TruncatesItsSchemaAtSixtyFourKilobytes()
    {
        // Pinned rather than skipped. FilesWithSchema() excludes this demo from every test that
        // needs entities, and an exclusion nobody asserts is indistinguishable from a test that
        // quietly stopped covering something.
        //
        // The finding: TF2's launch build truncates dem_datatables at exactly 65,536 bytes when
        // SourceTV writes it. The POV recording of the SAME session carries 85,063, which is what
        // establishes the schema really is larger and the cut is the writer's rather than this
        // parser's. Four things say the file is otherwise intact: the size is exactly 2^16, the
        // payload sits in the signon block at the START of the recording where an interrupted
        // capture cannot reach, its frame check is exact at 3,897 of 3,897, and it ends with
        // dem_stop.
        string? truncated = Corpus.Files().FirstOrDefault(
            f => Path.GetFileName(f) == "tf2-2007-build3258-stv-cp_granary.dem");
        if (truncated is null)
        {
            return;                                  // corpus not checked out
        }

        Corpus.TrySchema(truncated).ShouldBeNull(
            "the launch-build SourceTV schema is truncated and must not parse");

        InvalidDataException failure =
            Should.Throw<InvalidDataException>(() => Corpus.Schema(truncated));
        failure.Message.ShouldContain("65536");

        // The paired POV proves the schema is genuinely larger than the cut.
        string pov = Corpus.Files().First(
            f => Path.GetFileName(f) == "tf2-2007-build3258-pov-cp_granary.dem");
        Corpus.TrySchema(pov).ShouldNotBeNull().ServerClasses.Count.ShouldBeGreaterThan(200);
    }

    [Test]
    public void Schema_ParsesAndNamesAreRecognisable()
    {
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull(
                $"{Path.GetFileName(path)}: no dem_datatables command found");

            schema.Tables.ShouldNotBeEmpty();
            schema.ServerClasses.ShouldNotBeEmpty();

            // Every TF2 demo describes the player. If the stream were misaligned this lookup
            // would fail long before any value was wrong.
            schema.FindTable("DT_TFPlayer").ShouldNotBeNull(Path.GetFileName(path));

            // Not every table is DT_-prefixed, which an earlier version of this test assumed.
            // Source auto-generates a table per array property - _ST_<prop> for the element
            // send table and _LPT_<prop> for its length proxy - plus tables named directly
            // after the property. Their presence is evidence of correct parsing rather than a
            // problem: nothing but a correctly aligned read produces _LPT_m_AnimOverlay_15.
            foreach (SendTable table in schema.Tables)
            {
                table.Name.ShouldNotBeNullOrWhiteSpace(Path.GetFileName(path));
                table.Name.Length.ShouldBeLessThan(128);
                table.Name.ShouldAllBe(c => !char.IsControl(c));
            }

            schema.Tables.ShouldContain(
                t => t.Name.StartsWith("_LPT_", StringComparison.Ordinal),
                $"{Path.GetFileName(path)}: expected Source's auto-generated array tables");
        }
    }

    [Test]
    public void Schema_ServerClassCountMatchesWhatServerInfoReported()
    {
        // Two completely separate paths: a 16-bit count at the end of the datatables command,
        // and MaxClasses inside svc_ServerInfo in the signon stream. Agreement is the best
        // evidence available that both decoders are aligned.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            ServerInfoMessage info = FindServerInfo(path).ShouldNotBeNull();

            schema.ServerClasses.Count.ShouldBe(
                info.MaxClasses,
                $"{Path.GetFileName(path)}: datatables and ServerInfo disagree on class count");
        }
    }

    // Six tests removed on 2026-08-19 and covered elsewhere.
    //
    // Schema_PropertiesLookLikeSourceEngineFields, Flatten_ProducesPlausibleListsForEveryClass
    // and Flatten_PlayerClassContainsTheFieldsAViewerNeeds were plausibility checks - every
    // name under 128 characters and free of control characters, every bit count between 0 and
    // 32, every list non-empty. Those catch a schema read at the wrong offset and say nothing
    // about ORDER, which is the whole contract: an update names properties by position, so a
    // flattener producing the right set in the wrong sequence decodes every property into its
    // neighbour's slot with every value still plausible.
    //
    // SyntheticFlatteningTests states the expected order outright, which found data cannot do
    // without reimplementing the flattener to find out. It covers the parent-before-child rule,
    // SPROP_CHANGES_OFTEN sorting forward, exclusions removing rather than emitting, and the
    // owner table travelling with each property.
    //
    // SchemaShape, FlattenedShape and SchemaDecodability were reports whose only assertion was
    // Corpus.Files().ShouldNotBeEmpty() - a guard on the fixture rather than on the code.

    /// <summary>The demo's schema, parsed once per process by <see cref="Corpus"/>.</summary>
    /// <remarks>
    /// Every test in this class needs the schema, and parsing one is a bit-level walk of up to
    /// 1.4 MB. Sharing the parse took this class from roughly eight seconds to under one, and
    /// the same saving multiplies across every mutant in a Stryker run.
    /// </remarks>
    private static DemoSchema? ParseSchema(string path) => Corpus.Schema(path);

    private static ServerInfoMessage? FindServerInfo(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        // Seeded from the header: the protocol sizes the message type field, so a
        // protocol-15 demo yields no messages at all without it (RISKS B17).
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
        };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet)
            .Take(50))
        {
            ServerInfoMessage? info = NetMessageReader.Read(command.Payload.Span, state)
                .Messages.OfType<ServerInfoMessage>().FirstOrDefault();
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }
}
