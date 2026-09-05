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
                $"class={entity.ClassId} serial={entity.SerialNumber} " +
                $"ibits={entity.IndexPayloadBits} {{"));

            foreach (DecodedProperty property in entity.Properties)
            {
                string value = PropertyText.Write(property.Definition, property.Value);
                string shape = Shape(property);
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    prop {property.Index}{shape} {property.Definition.OwnerTable}." +
                    $"{property.Definition.Property.Name} {value}"));
            }

            lines.Add("  " + BlockEnd);
        }

        foreach (int removed in decoder.RemovedEntities)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  removed {removed}"));
        }

        // **The slack is data, not padding, and assuming it away costs a tenth of all snapshots.**
        // A body states its length in bits and the sender builds it in bytes, so a snapshot
        // routinely ends before its stated end - and what sits in the gap is not reliably zero.
        // Writing zeros there produced a body that decoded identically and did not match the demo,
        // which is why 3,897 snapshots were still being carried as hex after everything else had
        // been promoted.
        decoder.EncodeEntities(
            entities, decoder.RemovedEntities, message.IsDelta, 0, out int written);

        int slack = message.LengthBits - written;
        if (slack > 0)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  slack {slack} {Convert.ToHexString(Bits(message.Body.Span, written, slack))}"));
        }

        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Copies a bit range into its own buffer, starting at bit zero.</summary>
    private static byte[] Bits(ReadOnlySpan<byte> source, int startBit, int count)
    {
        Tf2DemoSalvage.Core.Primitives.BitWriter writer = new();
        for (int i = 0; i < count; i++)
        {
            int bit = startBit + i;
            writer.Write((uint)((source[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
    }

    /// <summary>Renders a temp entities body, or <c>null</c> when it will not decode.</summary>
    /// <param name="message">The message.</param>
    /// <param name="decoder">Decoder holding the schema.</param>
    /// <returns>The lines, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <remarks>
    /// Temp entities are entities - explosions, tracers, blood, decals - read against the same
    /// flattened schema an update uses. They are what a 3D viewer draws that no entity in the
    /// table accounts for, because they are fire-and-forget and never enter it.
    /// </remarks>
    public static IReadOnlyList<string>? WriteEffects(
        TempEntitiesMessage message, EntityDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(decoder);

        IReadOnlyList<DecodedTempEntity> effects;
        try
        {
            effects = decoder.DecodeTempEntities(
                message.Body.Span, message.Count, message.BodyBits);
        }
        catch (Exception failure) when (failure is InvalidDataException or EndOfStreamException)
        {
            // Temp entities are effects - a rocket trail, a blood spray - so losing them costs
            // detail rather than structure. It still says so: "no effects in this demo" and "the
            // effects would not decode" look identical in the output otherwise.
            Diagnostics.DecodeLog.Lost("entities", "decoding temp entities", failure);

            return null;
        }

        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture,
                $"svc_tempentities count={message.Count} bits={message.BodyBits} {{"),
        ];

        foreach (DecodedTempEntity effect in effects)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  effect class={effect.ClassId} delay={Round(effect.DelaySeconds)} {{"));

            foreach (DecodedProperty property in effect.Properties)
            {
                string value = PropertyText.Write(property.Definition, property.Value);
                string shape = Shape(property);
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    prop {property.Index}{shape} {property.Definition.OwnerTable}." +
                    $"{property.Definition.Property.Name} {value}"));
            }

            lines.Add("  " + BlockEnd);
        }

        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Reads a temp entities block back into a message.</summary>
    /// <param name="tokens">The message's first line.</param>
    /// <param name="nextLine">Supplies the block's lines.</param>
    /// <param name="decoder">Decoder holding the schema.</param>
    /// <returns>The message, with its body re-encoded from the text.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static TempEntitiesMessage BuildEffects(
        IReadOnlyList<string> tokens, Func<string?> nextLine, EntityDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);
        ArgumentNullException.ThrowIfNull(decoder);

        Dictionary<string, string> header = Fields(tokens);
        List<DecodedTempEntity> effects = [];

        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("An effect block was not closed with '}'.");

            List<string> parts = Tokens(line);
            if (parts.Count == 0)
            {
                continue;
            }

            if (parts[0] == BlockEnd)
            {
                break;
            }

            effects.Add(ReadEffect(parts, nextLine, decoder));
        }

        int count = Field(header, "count");
        int bits = Field(header, "bits");

        return new TempEntitiesMessage(
            count, bits, decoder.EncodeTempEntities(effects, count == 0, bits));
    }

    private static DecodedTempEntity ReadEffect(
        List<string> parts, Func<string?> nextLine, EntityDecoder decoder)
    {
        Dictionary<string, string> fields = Fields(parts);
        int classId = Field(fields, "class");
        float delay = Real(Text(fields, "delay"), "'delay' field");

        List<DecodedProperty> properties = [];
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);

        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("An effect was not closed with '}'.");

            List<string> tokens = Tokens(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0] == BlockEnd)
            {
                break;
            }

            (int index, int indexWidth, int shape) = ParseIndex(Token(tokens, 1, "property index"));
            properties.Add(new DecodedProperty(
                index, flat[index], PropertyText.Read(flat[index], tokens, 3), indexWidth, shape));
        }

        return new DecodedTempEntity(classId, delay, properties);
    }

    private static string Round(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// The property's encoding choices, appended to its index as <c>index/width/coord</c>.
    /// </summary>
    /// <remarks>
    /// Written only when there is something to say, so an ordinary property keeps a bare index.
    /// Both parts are choices the sender made that the value cannot recover: which UBitVar bucket
    /// the index delta used, and which components of a coordinate took the narrow integer field.
    /// </remarks>
    private static string Shape(DecodedProperty property) =>
        property.IndexPayloadBits == 0 && property.CoordShape == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"/{property.IndexPayloadBits}/{property.CoordShape}");

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
        byte[] slack = [];
        int slackBits = 0;

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
                removed.Add(Number(Token(parts, 1, "removed index"), "removed index"));
                continue;
            }

            if (parts[0] == "slack")
            {
                slackBits = Number(Token(parts, 1, "slack width"), "slack width");
                slack = Hex(Token(parts, 2, "slack payload"), "slack payload");
                continue;
            }

            entities.Add(ReadEntity(parts, nextLine, decoder));
        }

        bool isDelta = Field(header, "delta") != 0;
        int lengthBits = Field(header, "bits");

        // Content first, then whatever the sender left in the gap, then zeros if anything is
        // still short of the stated length.
        byte[] content = decoder.EncodeEntities(entities, removed, isDelta, 0, out int written);

        Tf2DemoSalvage.Core.Primitives.BitWriter body = new();
        body.AppendBits(content, written);
        body.AppendBits(slack, slackBits);
        for (int bit = body.BitCount; bit < lengthBits; bit++)
        {
            body.WriteBit(false);
        }

        return new PacketEntitiesMessage(
            Field(header, "max"),
            isDelta,
            Text(header, "from") == "-" ? null : Field(header, "from"),
            Field(header, "baseline") != 0,
            Field(header, "updated"),
            lengthBits,
            Field(header, "updatebaseline") != 0,
            body.Build());
    }

    private static DecodedEntity ReadEntity(
        List<string> parts, Func<string?> nextLine, EntityDecoder decoder)
    {
        int index = Number(Token(parts, 1, "entity index"), "entity index");
        EntityUpdateType update = Update(Token(parts, 2, "update type"));
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

            // The name is written for a reader and ignored here: the index is what addresses the
            // flattened list, and a name that disagreed with it would be the schema's problem
            // rather than something to reconcile at parse time.
            (int propertyIndex, int indexWidth, int shape) = ParseIndex(Token(tokens, 1, "property index"));

            properties.Add(new DecodedProperty(
                propertyIndex,
                flat[propertyIndex],
                PropertyText.Read(flat[propertyIndex], tokens, 3),
                indexWidth,
                shape));
        }

        return new DecodedEntity(
            index, classId, Field(fields, "serial"), update, properties,
            fields.TryGetValue("ibits", out string? width)
                ? Number(width, "'ibits' field")
                : 0);
    }

    /// <summary>Splits <c>index</c> or <c>index/width/coord</c> into its three parts.</summary>
    private static (int Index, int Width, int CoordShape) ParseIndex(string token)
    {
        string[] parts = token.Split('/');
        return (
            Number(parts[0], "property index"),
            parts.Length > 1 ? Number(parts[1], "property index width") : 0,
            parts.Length > 2 ? Number(parts[2], "property coordinate shape") : 0);
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
        Number(Text(fields, name), $"'{name}' field");

    /// <summary>A field's text, or a refusal naming the field that is missing.</summary>
    /// <remarks>
    /// **Every read of a hand-edited trace goes through one of these five, and that is the point**
    /// (B344). `DemoAssembly.cs:533` catches `InvalidDataException` and nothing else, to rethrow it
    /// with the offending line attached; a `FormatException` from a bare `int.Parse` or an
    /// `ArgumentException` from a bare `Enum.Parse` walks straight past that handler and reaches the
    /// person with no line, no field and no file named.
    ///
    /// So the type is not a detail — it is what carries the context. Each of these quotes what was
    /// written as well as what was expected, because a trace is edited by hand and the value is the
    /// half the editor can act on.
    /// </remarks>
    /// <param name="fields">The line's <c>name=value</c> fields.</param>
    /// <param name="name">The field to read.</param>
    /// <returns>The field's text.</returns>
    private static string Text(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value)
            ? value
            : throw new InvalidDataException($"An entity line has no '{name}' field.");

    /// <summary>The token at an index, or a refusal naming what the line lacks.</summary>
    /// <param name="parts">The line's tokens.</param>
    /// <param name="index">The token to read.</param>
    /// <param name="what">What that token holds, for the message.</param>
    /// <returns>The token.</returns>
    private static string Token(List<string> parts, int index, string what) =>
        index < parts.Count
            ? parts[index]
            : throw new InvalidDataException(
                $"An entity line has no {what}: '{string.Join(' ', parts)}'.");

    /// <summary>A whole number, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the number means, for the message.</param>
    /// <returns>The number.</returns>
    private static int Number(string value, string what) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : throw new InvalidDataException(
                $"An entity line's {what} is not a whole number: '{value}'.");

    /// <summary>A real number, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the number means, for the message.</param>
    /// <returns>The number.</returns>
    private static float Real(string value, string what) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : throw new InvalidDataException(
                $"An entity line's {what} is not a number: '{value}'.");

    /// <summary>An update type, or a refusal listing the ones that exist.</summary>
    /// <remarks>
    /// **The valid names are listed because the set is small and closed.** A person who typed
    /// `entre` cannot recover `ENTER` from `Requested value 'entre' was not found`, and there are
    /// four candidates in total — so printing them costs a line and ends the search.
    ///
    /// **A numeric type outside the enum is refused too.** `Enum.Parse` accepts `99` and yields an
    /// undefined value, which would then decode against a rule that matches no branch; `IsDefined`
    /// makes the refusal total rather than leaving one shape through.
    /// </remarks>
    /// <param name="value">The token to read.</param>
    /// <returns>The update type.</returns>
    private static EntityUpdateType Update(string value) =>
        Enum.TryParse(value, ignoreCase: true, out EntityUpdateType update)
            && Enum.IsDefined(update)
            ? update
            : throw new InvalidDataException(
                $"An entity line's update type is not one of " +
                $"{string.Join(", ", Enum.GetNames<EntityUpdateType>())}: '{value}'.");

    /// <summary>Hexadecimal bytes, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the bytes hold, for the message.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Hex(string value, string what)
    {
        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException failure)
        {
            throw new InvalidDataException(
                $"An entity line's {what} is not hexadecimal: '{value}'.", failure);
        }
    }

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
