using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders entity snapshots as assembly text, and reads them back.
/// </summary>
/// <remarks>
/// **The one that matters for a viewer.** <c>svc_PacketEntities</c> is where player positions,
/// angles, health and every other networked value live, and it is 80% of the bits that were still
/// carried as hex. A 2D or 3D viewer cannot do anything with a hex string; it can do everything
/// with these lines.
///
/// **The schema is what makes it possible, and it is not on this message.** A property's name,
/// type and encoding come from <c>dem_datatables</c>, which arrives once as its own command, so
/// both directions have to carry an <see cref="EntityDecoder"/> built from it. That is the same
/// dependency the format has everywhere — nothing here is self-describing — and it is why this
/// could not be promoted before the schema parser existed.
///
/// **Property values are written by type, not by <c>ToString</c>.** A vector is three round-trip
/// floats rather than a formatted triple, because the text has to reproduce the bits and a
/// display format loses the last few. The property index is written as its own number rather than
/// implied by order: a snapshot sends only the properties that changed, so the indices are what
/// say which ones those were.
/// </remarks>
public static class EntityAssembly
{
    /// <summary>Closes a block.</summary>
    private const string BlockEnd = "}";

    /// <summary>Renders a snapshot, or <c>null</c> when it cannot be decoded.</summary>
    /// <param name="message">The snapshot.</param>
    /// <param name="decoder">Decoder holding the schema and the entity-to-class map.</param>
    /// <returns>The lines, or <c>null</c> when the body does not decode.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static IReadOnlyList<string>? Write(PacketEntitiesMessage message, EntityDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(decoder);

        IReadOnlyList<DecodedEntity> entities;
        try
        {
            entities = decoder.Decode(message.Body.Span, message, message.LengthBits);
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            // A snapshot that will not decode stays as bits. That is the honest outcome and it is
            // exactly the case this project exists to survive.
            return null;
        }

        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"svc_packetentities delta={(message.IsDelta ? 1 : 0)} " +
                $"from={message.DeltaFromTick?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                $"max={message.MaxEntries} baseline={(message.BaselineIndex ? 1 : 0)} " +
                $"updatebaseline={(message.UpdateBaseline ? 1 : 0)} " +
                $"updated={message.UpdatedEntries} bits={message.LengthBits} {{"),
        ];

        foreach (DecodedEntity entity in entities)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {entity.EntityIndex} {entity.UpdateType.ToString().ToUpperInvariant()} " +
                $"class={entity.ClassId} serial={entity.SerialNumber} {{"));

            foreach (DecodedProperty property in entity.Properties)
            {
                string value = PropertyText.Write(property.Definition, property.Value);
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    prop {property.Index} {property.Definition.OwnerTable}." +
                    $"{property.Definition.Property.Name} {value}"));
            }

            lines.Add("  " + BlockEnd);
        }

        foreach (int removed in decoder.RemovedEntities)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  removed {removed}"));
        }

        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Reads a snapshot's lines back into a message.</summary>
    /// <param name="tokens">The <c>svc_packetentities</c> line's tokens.</param>
    /// <param name="nextLine">Supplies the block's remaining lines.</param>
    /// <param name="decoder">Decoder holding the schema and the entity-to-class map.</param>
    /// <returns>The message, with its body re-encoded from the text.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The block is malformed.</exception>
    public static PacketEntitiesMessage Build(
        IReadOnlyList<string> tokens, Func<string?> nextLine, EntityDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);
        ArgumentNullException.ThrowIfNull(decoder);

        Dictionary<string, string> header = Fields(tokens);
        List<DecodedEntity> entities = [];
        List<int> removed = [];

        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("An entity block was not closed with '}'.");

            List<string> parts = Tokens(line);
            if (parts.Count == 0)
            {
                continue;
            }

            if (parts[0] == BlockEnd)
            {
                break;
            }

            if (parts[0] == "removed")
            {
                removed.Add(int.Parse(parts[1], CultureInfo.InvariantCulture));
                continue;
            }

            entities.Add(ReadEntity(parts, nextLine, decoder));
        }

        bool isDelta = Field(header, "delta") != 0;
        int lengthBits = Field(header, "bits");

        byte[] body = decoder.EncodeEntities(entities, removed, isDelta, lengthBits);

        return new PacketEntitiesMessage(
            Field(header, "max"),
            isDelta,
            header["from"] == "-" ? null : Field(header, "from"),
            Field(header, "baseline") != 0,
            Field(header, "updated"),
            lengthBits,
            Field(header, "updatebaseline") != 0,
            body);
    }

    private static DecodedEntity ReadEntity(
        List<string> parts, Func<string?> nextLine, EntityDecoder decoder)
    {
        int index = int.Parse(parts[1], CultureInfo.InvariantCulture);
        EntityUpdateType update = Enum.Parse<EntityUpdateType>(parts[2], ignoreCase: true);
        Dictionary<string, string> fields = Fields(parts);
        int classId = Field(fields, "class");

        List<DecodedProperty> properties = [];
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);

        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("An entity was not closed with '}'.");

            List<string> tokens = Tokens(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0] == BlockEnd)
            {
                break;
            }

            int propertyIndex = int.Parse(tokens[1], CultureInfo.InvariantCulture);

            // The name is written for a reader and ignored here: the index is what addresses the
            // flattened list, and a name that disagreed with it would be the schema's problem
            // rather than something to reconcile at parse time.
            properties.Add(new DecodedProperty(
                propertyIndex,
                flat[propertyIndex],
                PropertyText.Read(flat[propertyIndex], tokens, 3)));
        }

        return new DecodedEntity(
            index, classId, Field(fields, "serial"), update, properties);
    }

    private static Dictionary<string, string> Fields(IReadOnlyList<string> tokens)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                fields[token[..equals]] = token[(equals + 1)..];
            }
        }

        return fields;
    }

    private static int Field(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"An entity line has no '{name}' field.");

    /// <summary>Splits a line into bare tokens and quoted strings.</summary>
    private static List<string> Tokens(string line)
    {
        List<string> tokens = [];
        System.Text.StringBuilder current = new();
        bool quoted = false;
        bool escaped = false;
        bool started = false;

        foreach (char character in line)
        {
            if (escaped)
            {
                current.Append(character == 'n' ? '\n' : character);
                escaped = false;
                continue;
            }

            if (quoted && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (started || current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
        }

        if (started || current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
