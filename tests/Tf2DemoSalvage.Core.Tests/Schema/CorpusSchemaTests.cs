using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Xunit.Abstractions;

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
public sealed class CorpusSchemaTests(ITestOutputHelper output)
{
    [Fact]
    public void Schema_ParsesAndNamesAreRecognisable()
    {
        foreach (string path in Corpus.Files())
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

    [Fact]
    public void Schema_ServerClassCountMatchesWhatServerInfoReported()
    {
        // Two completely separate paths: a 16-bit count at the end of the datatables command,
        // and MaxClasses inside svc_ServerInfo in the signon stream. Agreement is the best
        // evidence available that both decoders are aligned.
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            ServerInfoMessage info = FindServerInfo(path).ShouldNotBeNull();

            schema.ServerClasses.Count.ShouldBe(
                info.MaxClasses,
                $"{Path.GetFileName(path)}: datatables and ServerInfo disagree on class count");
        }
    }

    [Fact]
    public void Schema_PropertiesLookLikeSourceEngineFields()
    {
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();

            foreach (SendProperty property in schema.Tables.SelectMany(t => t.Properties))
            {
                property.Name.ShouldNotBeNullOrEmpty();
                property.Name.Length.ShouldBeLessThan(128);
                property.Name.ShouldAllBe(c => !char.IsControl(c));
                Enum.IsDefined(property.Type).ShouldBeTrue();

                // 32 is the widest a networked value gets.
                property.BitCount.ShouldBeInRange(0, 32);
            }
        }
    }

    [Fact]
    public void ReportSchemaShape()
    {
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            SendTable player = schema.FindTable("DT_TFPlayer")!;

            output.WriteLine(
                $"{Path.GetFileName(path)}: {schema.Tables.Count} tables, " +
                $"{schema.ServerClasses.Count} classes, " +
                $"{schema.Tables.Sum(t => t.Properties.Count)} properties");
            output.WriteLine(
                $"  DT_TFPlayer: {player.Properties.Count} props - " +
                string.Join(", ", player.Properties.Take(4).Select(p => $"{p.Type} {p.Name}")));

            int changesOften = schema.Tables.SelectMany(t => t.Properties).Count(p => p.ChangesOften);
            int excluded = schema.Tables.SelectMany(t => t.Properties).Count(p => p.IsExcluded);
            output.WriteLine($"  {changesOften} changes-often, {excluded} exclusions");
            output.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    [Fact]
    public void Flatten_ProducesPlausibleListsForEveryClass()
    {
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();

            foreach (ServerClass serverClass in schema.ServerClasses)
            {
                IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, serverClass);

                // MAX_DATATABLE_PROPS from the SDK. Exceeding it would mean the flattener is
                // duplicating properties, most likely by following a cycle.
                flat.Count.ShouldBeLessThanOrEqualTo(4096, serverClass.ClassName);

                // Changes-often properties must form an unbroken prefix. This is the ordering
                // rule entity deltas depend on, checked on real data rather than fixtures.
                int firstSlow = -1;
                for (int i = 0; i < flat.Count; i++)
                {
                    if (!flat[i].Property.ChangesOften)
                    {
                        firstSlow = i;
                    }
                    else if (firstSlow >= 0)
                    {
                        Assert.Fail(
                            $"{Path.GetFileName(path)} {serverClass.ClassName}: changes-often " +
                            $"property at {i} follows a normal one at {firstSlow}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Flatten_PlayerClassContainsTheFieldsAViewerNeeds()
    {
        // The properties Phase 2 and 3 exist to draw. If flattening dropped a table or applied
        // an exclusion too broadly, these would quietly go missing.
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            ServerClass player = schema.ServerClasses.First(c => c.ClassName == "CTFPlayer");

            string[] names = [.. SchemaFlattener.Flatten(schema, player)
                .Select(f => f.Property.Name)];

            names.ShouldContain("m_vecOrigin", Path.GetFileName(path));
            names.ShouldContain("m_iHealth", Path.GetFileName(path));
            names.ShouldContain("m_iTeamNum", Path.GetFileName(path));
        }
    }

    [Fact]
    public void ReportFlattenedShape()
    {
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            ServerClass player = schema.ServerClasses.First(c => c.ClassName == "CTFPlayer");
            IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, player);

            output.WriteLine($"{Path.GetFileName(path)}: CTFPlayer flattens to {flat.Count} props, " +
                             $"{flat.Count(f => f.Property.ChangesOften)} changes-often, " +
                             $"from {flat.Select(f => f.OwnerTable).Distinct().Count()} tables");
            output.WriteLine("  first 6: " + string.Join(", ", flat.Take(6)
                .Select(f => $"{f.OwnerTable}.{f.Property.Name}")));

            int biggest = schema.ServerClasses.Max(c => SchemaFlattener.Flatten(schema, c).Count);
            output.WriteLine($"  largest class flattens to {biggest} props");
            output.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    [Fact]
    public void ReportHowMuchOfTheSchemaIsDecodable()
    {
        // Quantifies what the coordinate encodings actually cost. SPROP_COORD_MP is
        // undocumented in VDC and unimplemented here, and this says how much of the schema
        // that leaves out of reach rather than leaving it to be guessed at.
        foreach (string path in Corpus.Files())
        {
            DemoSchema schema = ParseSchema(path).ShouldNotBeNull();
            ServerClass player = schema.ServerClasses.First(c => c.ClassName == "CTFPlayer");
            IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(schema, player);

            int decodable = flat.Count(f => SendPropDecoder.IsSupported(f.Property));
            string[] blocked = [.. flat
                .Where(f => !SendPropDecoder.IsSupported(f.Property))
                .Select(f => f.Property.Name)
                .Distinct()
                .Take(6)];

            output.WriteLine(
                $"{Path.GetFileName(path)}: CTFPlayer {decodable}/{flat.Count} properties " +
                $"decodable ({100.0 * decodable / flat.Count:F1}%)");
            output.WriteLine($"  blocked examples: {string.Join(", ", blocked)}");

            int allProps = schema.ServerClasses.Sum(c => SchemaFlattener.Flatten(schema, c).Count);
            int allOk = schema.ServerClasses.Sum(c =>
                SchemaFlattener.Flatten(schema, c).Count(f => SendPropDecoder.IsSupported(f.Property)));
            output.WriteLine($"  across all classes: {allOk}/{allProps} ({100.0 * allOk / allProps:F1}%)");
            output.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

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
