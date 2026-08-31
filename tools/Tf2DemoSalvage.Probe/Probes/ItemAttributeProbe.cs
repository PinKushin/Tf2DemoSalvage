using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether a demo's own schema declares the item attributes an econ entity carries.
/// </summary>
/// <remarks>
/// **There is a send table named for this project's exact use case.** `CEconItemView` networks two
/// attribute lists (`econ_item_view.cpp:191`):
///
/// <code>
///   SendPropDataTable( SENDINFO_DT( m_AttributeList ), &amp;REFERENCE_SEND_TABLE( DT_AttributeList ) ),
///   SendPropDataTable( SENDINFO_DT( m_NetworkedDynamicAttributesForDemos ), ... ),
/// </code>
///
/// and the server fills the second on every <c>CEconEntity::InitializeAttributes</c>
/// (<c>econ_entity.cpp:251</c>) — unconditionally, not only while recording. Each entry is an
/// attribute definition index and thirty-two raw bits, sent under the wire name
/// <c>m_iRawValue32</c>:
///
/// <code>
///   SendPropInt( SENDINFO( m_iAttributeDefinitionIndex ), -1, SPROP_UNSIGNED ),
///   SendPropInt( SENDINFO_NAME( m_flValue, m_iRawValue32 ), 32, SPROP_UNSIGNED ),
/// </code>
///
/// That is paint colours, unusual effects, killstreak sheens and the style override — none of
/// which this project decodes. This reports what each demo's schema actually declares, per era,
/// because the tables arrived at some point and a claim about "demos carry this" needs a date.
/// </remarks>
public sealed class ItemAttributeProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "item-attributes";

    /// <inheritdoc/>
    public string Summary => "which demos declare the econ attribute tables, per era";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string path in DemoCorpus.Files(output))
        {
            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException failure)
            {
                output.WriteLine($"{Path.GetFileName(path)}: unreadable, {failure.Message}");
                continue;
            }

            Report(output, path, bytes);
        }
    }

    private static void Report(TextWriter output, string path, byte[] bytes)
    {
        ushort protocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol;
        DemoSchema? schema = null;

        string name = Path.GetFileName(path);

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type != DemoCommandType.DataTables)
            {
                continue;
            }

            // **A truncated `dem_datatables` is a real and known state of a real file**, not a
            // decoder fault — the writer stops mid-table when a recording ends abruptly. A probe
            // that threw here would report nothing about every OTHER demo in the corpus, which is
            // the whole measurement.
            try
            {
                schema = SendTableParser.Parse(command.Payload.Span, protocol);
            }
            catch (InvalidDataException failure)
            {
                output.WriteLine($"{name} protocol {protocol}: schema unreadable, {failure.Message}");
                return;
            }

            break;
        }

        if (schema is null)
        {
            output.WriteLine($"{name} protocol {protocol}: no dem_datatables");
            return;
        }

        List<string> tables =
        [
            .. schema.Tables
                .Select(table => table.Name)
                .Where(table => table.Contains("Attribute", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        // The property names, because a table's presence says the layout exists and the property
        // names say what a decoder would have to ask for.
        List<string> properties =
        [
            .. schema.Tables
                .SelectMany(table => table.Properties.Select(property => property.Name))
                .Where(property =>
                    property.Contains("Attribute", StringComparison.Ordinal)
                    || property.Contains("RawValue", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        output.WriteLine(
            $"{name} protocol {protocol.ToString(CultureInfo.InvariantCulture)}: "
            + $"tables [{string.Join(", ", tables)}] "
            + $"props [{string.Join(", ", properties)}]");
    }
}
