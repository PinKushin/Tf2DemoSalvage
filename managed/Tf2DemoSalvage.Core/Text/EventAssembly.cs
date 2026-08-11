using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders game events and their definitions as assembly text, and reads them back.
/// </summary>
/// <remarks>
/// **Events are the match's narrative** — who killed whom, who captured what, when a round ended —
/// and a viewer wants them as words rather than as an id and a bit count.
///
/// **A game event is not self-describing.** Its body is a bare sequence of values whose names,
/// order and types live in <c>svc_GameEventList</c>, which arrives once. So the definitions are
/// written out in full and every later event is read against them, which is the same shape the
/// entity schema has. Order matters more than it looks: the decoded values are a dictionary and a
/// dictionary does not remember the wire, so a writer that emitted them in enumeration order
/// would produce a body that decodes to the same values and does not match the demo.
///
/// The declared body length travels too. A sender measures its buffer after rounding, so a body
/// routinely runs a few bits past its last field, and those bits are on the wire.
/// </remarks>
public static class EventAssembly
{
    /// <summary>Closes a block.</summary>
    private const string BlockEnd = "}";

    /// <summary>Type names, so a definition reads as a declaration rather than as numbers.</summary>
    private static readonly Dictionary<GameEventValueType, string> TypeNames = new()
    {
        [GameEventValueType.String] = "string",
        [GameEventValueType.Float] = "float",
        [GameEventValueType.Long] = "long",
        [GameEventValueType.Short] = "short",
        [GameEventValueType.Byte] = "byte",
        [GameEventValueType.Bool] = "bool",
        [GameEventValueType.Local] = "local",
    };

    private static readonly Dictionary<string, GameEventValueType> Types =
        BuildReverse();

    /// <summary>Renders a <c>svc_GameEventList</c>.</summary>
    /// <param name="message">The list.</param>
    /// <returns>The lines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> WriteList(GameEventListMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        List<string> lines =
        [
            string.Create(
                CultureInfo.InvariantCulture, $"svc_gameeventlist bits={message.BodyBits} {{"),
        ];

        foreach (GameEventDefinition definition in message.Definitions)
        {
            System.Text.StringBuilder line = new("  event ");
            line.Append(definition.Id.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(Quote(definition.Name));

            foreach (GameEventField field in definition.Fields)
            {
                line.Append(' ').Append(TypeNames[field.Type]).Append(' ').Append(Quote(field.Name));
            }

            lines.Add(line.ToString());
        }

        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Reads a <c>svc_GameEventList</c> back.</summary>
    /// <param name="tokens">The message's first line.</param>
    /// <param name="nextLine">Supplies the block's lines.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">A line is malformed.</exception>
    public static GameEventListMessage BuildList(
        IReadOnlyList<string> tokens, Func<string?> nextLine)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);

        List<GameEventDefinition> definitions = [];

        foreach (List<string> line in Block(nextLine))
        {
            // Fields start after the id and the name, in (type, name) pairs.
            List<GameEventField> fields = [];
            for (int i = 3; i + 1 < line.Count; i += 2)
            {
                fields.Add(new GameEventField(line[i + 1], Type(line[i])));
            }

            definitions.Add(new GameEventDefinition(
                int.Parse(line[1], CultureInfo.InvariantCulture), line[2], fields));
        }

        return new GameEventListMessage(definitions, Bits(tokens));
    }

    /// <summary>Renders a <c>svc_GameEvent</c>.</summary>
    /// <param name="message">The event.</param>
    /// <returns>The lines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> WriteEvent(GameEventMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        System.Text.StringBuilder line = new("svc_gameevent ");
        line.Append(message.EventId.ToString(CultureInfo.InvariantCulture))
            .Append(" bits=")
            .Append(message.BodyBits.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Quote(message.Name ?? string.Empty));

        foreach (KeyValuePair<string, object?> field in message.Values)
        {
            line.Append(' ').Append(field.Key).Append('=').Append(Value(field.Value));
        }

        return [line.ToString()];
    }

    /// <summary>Reads a <c>svc_GameEvent</c> back, against the definitions seen so far.</summary>
    /// <param name="tokens">The message's line.</param>
    /// <param name="nextLine">Unused; events are a single line.</param>
    /// <param name="state">Decode state, which holds the definitions.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The event has no definition.</exception>
    public static GameEventMessage BuildEvent(
        IReadOnlyList<string> tokens, Func<string?> nextLine, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);
        ArgumentNullException.ThrowIfNull(state);

        int id = int.Parse(tokens[1], CultureInfo.InvariantCulture);

        if (!state.EventDefinitions.TryGetValue(id, out GameEventDefinition? definition))
        {
            throw new InvalidDataException(
                $"Event {id} has no definition, so its fields cannot be typed.");
        }

        // The declared length rides on the same line as the fields, so it is filtered out by
        // name rather than by position.
        Dictionary<string, string> raw = new(StringComparer.Ordinal);
        foreach (string token in tokens.Where(
            candidate => candidate.Contains('=', StringComparison.Ordinal) &&
                !candidate.StartsWith('=') &&
                !candidate.StartsWith("bits=", StringComparison.Ordinal)))
        {
            int equals = token.IndexOf('=', StringComparison.Ordinal);
            raw[token[..equals]] = token[(equals + 1)..];
        }

        Dictionary<string, object?> values = new(definition.Fields.Count, StringComparer.Ordinal);
        foreach (GameEventField field in definition.Fields)
        {
            values[field.Name] = raw.TryGetValue(field.Name, out string? text)
                ? Parse(field.Type, text)
                : null;
        }

        return new GameEventMessage(id, definition.Name, values, Bits(tokens));
    }

    private static object? Parse(GameEventValueType type, string text) => type switch
    {
        GameEventValueType.String => text,
        GameEventValueType.Float => float.Parse(text, CultureInfo.InvariantCulture),
        GameEventValueType.Long => int.Parse(text, CultureInfo.InvariantCulture),
        GameEventValueType.Short => short.Parse(text, CultureInfo.InvariantCulture),
        GameEventValueType.Byte => byte.Parse(text, CultureInfo.InvariantCulture),
        GameEventValueType.Bool => text == "1",

        // Declared by the server and never broadcast, so it occupies no bits in either direction.
        _ => null,
    };

    private static string Value(object? value) => value switch
    {
        null => "-",
        bool flag => flag ? "1" : "0",
        string text => Quote(text),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static GameEventValueType Type(string name) =>
        Types.TryGetValue(name, out GameEventValueType type)
            ? type
            : throw new InvalidDataException($"'{name}' is not a game event field type.");

    private static Dictionary<string, GameEventValueType> BuildReverse()
    {
        Dictionary<string, GameEventValueType> reverse = new(StringComparer.Ordinal);
        foreach (KeyValuePair<GameEventValueType, string> entry in TypeNames)
        {
            reverse[entry.Value] = entry.Key;
        }

        return reverse;
    }

    /// <summary>The declared body length, which is not derivable from the fields.</summary>
    private static int Bits(IReadOnlyList<string> tokens)
    {
        string? declared = tokens.FirstOrDefault(
            token => token.StartsWith("bits=", StringComparison.Ordinal));

        return declared is null
            ? throw new InvalidDataException("A game event line has no 'bits' field.")
            : int.Parse(declared[5..], CultureInfo.InvariantCulture);
    }

    private static IEnumerable<List<string>> Block(Func<string?> nextLine)
    {
        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("An event list was not closed with '}'.");

            List<string> tokens = Tokens(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0] == BlockEnd)
            {
                yield break;
            }

            yield return tokens;
        }
    }

    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

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
