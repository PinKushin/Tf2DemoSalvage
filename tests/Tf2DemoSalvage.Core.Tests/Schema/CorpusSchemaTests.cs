using System;
using System.IO;
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

    private static DemoSchema? ParseSchema(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                return SendTableParser.Parse(command.Payload.Span);
            }
        }

        return null;
    }

    private static ServerInfoMessage? FindServerInfo(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new();

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
