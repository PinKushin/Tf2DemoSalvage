using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Renders string tables as assembly text, and reads them back.
/// </summary>
/// <remarks>
/// **String tables are the demo's dictionaries.** <c>userinfo</c> holds every player's name,
/// SteamID and user id; <c>soundprecache</c> and <c>modelprecache</c> are what an index in a sound
/// or an entity actually refers to. A viewer needs all of it, and as hex it had none of it.
///
/// **The compressed ones can never be pure text, and saying so is the honest position.** A create
/// message's payload is usually Snappy, and reproducing those bytes means reproducing one
/// particular compressor's output — not something a parser can promise. So a compressed table gets
/// its header promoted and keeps its payload: the name, the capacity and the entry count become
/// readable, and the bits still rebuild. In this corpus that is 137 messages against 315 plain
/// ones, but 16.5 M bits against 3.4 M, because the compressed ones are the big signon tables.
///
/// The per-entry encoding shape rides on the entry, for the reason it does everywhere else here:
/// which of the last 32 strings a name was built from is a choice the sender made and the decoded
/// string does not record it.
/// </remarks>
public static class StringTableAssembly
{
    /// <summary>Closes a block.</summary>
    private const string BlockEnd = "}";

    /// <summary>Renders a <c>svc_CreateStringTable</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The lines, or <c>null</c> when it did not arrive from a demo.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string>? WriteCreate(CreateStringTableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Wire is not { } wire)
        {
            return null;
        }

        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"svc_createstringtable {Quote(message.Name)} max={message.MaxEntries} " +
            $"count={wire.EntryCount} bits={wire.BodyBits} " +
            $"userbytes={wire.FixedUserDataSizeBytes?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
            $"userbits={wire.FixedUserDataSizeBits} " +
            $"compressed={(message.IsCompressed ? 1 : 0)}");

        // A compressed payload keeps its bits. Everything around it is still worth reading, which
        // is why this is a promotion rather than a raw line.
        if (message.IsCompressed || !message.IsDecoded)
        {
            return [$"{head} payload {Convert.ToHexString(wire.Body.Span)}"];
        }

        List<string> lines = [$"{head} {{"];
        AppendEntries(lines, message.Entries);
        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Renders a <c>svc_UpdateStringTable</c>.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The lines, or <c>null</c> when it did not arrive from a demo.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string>? WriteUpdate(UpdateStringTableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Wire is not { } wire)
        {
            return null;
        }

        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"svc_updatestringtable table={message.TableId} count={wire.EntryCount} " +
            $"bits={wire.BodyBits}");

        // An update whose table was never seen has no capacity to size its indices against, so
        // its entries were never decoded either.
        if (!message.IsDecoded)
        {
            return [$"{head} payload {Convert.ToHexString(wire.Body.Span)}"];
        }

        List<string> lines = [$"{head} {{"];
        AppendEntries(lines, message.Entries);
        lines.Add(BlockEnd);
        return lines;
    }

    /// <summary>Reads a <c>svc_CreateStringTable</c> back.</summary>
    /// <param name="tokens">The message's first line.</param>
    /// <param name="nextLine">Supplies the block's lines.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static CreateStringTableMessage BuildCreate(
        IReadOnlyList<string> tokens, Func<string?> nextLine)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);

        Dictionary<string, string> fields = Fields(tokens);
        int maxEntries = Number(fields, "max");
        int bits = Number(fields, "bits");
        bool compressed = Number(fields, "compressed") != 0;
        int? userBytes =
            AssemblyText.Text(fields, "userbytes", Subject) == "-"
                ? null
                : Number(fields, "userbytes");
        int userBits = Number(fields, "userbits");

        int payload = Payload(tokens);
        if (payload >= 0)
        {
            CreateStringTableWire opaque = new(
                Number(fields, "count"), bits, Hex(tokens, payload, "table payload"),
                userBytes, userBits);

            return new CreateStringTableMessage(
                tokens[1], maxEntries, [], compressed, "carried as bits", opaque);
        }

        List<StringTableEntry> entries = ReadEntries(nextLine);
        (byte[] body, int written) = StringTableCodec.WriteEntries(
            entries, maxEntries, userBytes is not null, userBits);

        CreateStringTableWire wire = new(
            entries.Count, bits, Pad(body, written, bits), userBytes, userBits);

        return new CreateStringTableMessage(
            tokens[1], maxEntries, entries, compressed, null, wire);
    }

    /// <summary>Reads a <c>svc_UpdateStringTable</c> back.</summary>
    /// <param name="tokens">The message's first line.</param>
    /// <param name="nextLine">Supplies the block's lines.</param>
    /// <param name="state">Decode state, which knows each table's capacity.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static UpdateStringTableMessage BuildUpdate(
        IReadOnlyList<string> tokens, Func<string?> nextLine, NetDecodeState state)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(nextLine);
        ArgumentNullException.ThrowIfNull(state);

        Dictionary<string, string> fields = Fields(tokens);
        int tableId = Number(fields, "table");
        int bits = Number(fields, "bits");

        int payload = Payload(tokens);
        if (payload >= 0)
        {
            return new UpdateStringTableMessage(
                tableId,
                [],
                "carried as bits",
                new UpdateStringTableWire(
                    Number(fields, "count"), bits, Hex(tokens, payload, "table payload")));
        }

        List<StringTableEntry> entries = ReadEntries(nextLine);

        // The capacity comes from the create message, exactly as it does when reading: an entry
        // index is sized from it, and an update does not carry it.
        (byte[] body, int written) = StringTableCodec.WriteEntries(
            entries, state.StringTableCapacity(tableId), fixedUserData: false, userDataSizeBits: 0);

        return new UpdateStringTableMessage(
            tableId,
            entries,
            null,
            new UpdateStringTableWire(entries.Count, bits, Pad(body, written, bits)));
    }

    private static void AppendEntries(
        List<string> lines, IReadOnlyList<StringTableEntry> entries)
    {
        foreach (StringTableEntry entry in entries)
        {
            System.Text.StringBuilder line = new("  entry ");
            line.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"index={entry.Index} follows={(entry.FollowsPrevious ? 1 : 0)} " +
                $"hist={entry.HistoryIndex} copy={entry.CopyLength}"));

            if (entry.Text is not null)
            {
                line.Append(" text=").Append(Quote(entry.Text));
            }

            if (entry.UserData.Count > 0)
            {
                line.Append(" data=").Append(Convert.ToHexString([.. entry.UserData]));
            }

            lines.Add(line.ToString());
        }
    }

    private static List<StringTableEntry> ReadEntries(Func<string?> nextLine)
    {
        List<StringTableEntry> entries = [];

        while (true)
        {
            string line = nextLine()
                ?? throw new InvalidDataException("A string table was not closed with '}'.");

            List<string> tokens = Tokens(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0] == BlockEnd)
            {
                return entries;
            }

            Dictionary<string, string> fields = Fields(tokens);
            entries.Add(new StringTableEntry(
                Number(fields, "index"),
                fields.TryGetValue("text", out string? text) ? text : null,
                fields.TryGetValue("data", out string? data)
                    ? AssemblyText.Hex(data, "'data' field", Subject)
                    : [],
                Number(fields, "follows") != 0,
                Number(fields, "hist"),
                Number(fields, "copy")));
        }
    }

    /// <summary>Zero-fills a body to the length the message declared.</summary>
    /// <remarks>
    /// A table's stated length can exceed its entries, for the reason it can everywhere in this
    /// format: the sender measures a buffer that was rounded. Those bits are on the wire.
    /// </remarks>
    private static byte[] Pad(byte[] body, int written, int declared)
    {
        if (written >= declared)
        {
            return body;
        }

        BitWriter writer = new();
        writer.AppendBits(body, written);
        for (int bit = written; bit < declared; bit++)
        {
            writer.WriteBit(false);
        }

        return writer.Build();
    }

    /// <summary>Index of the hex payload's token, or -1 when the message carries entries.</summary>
    private static int Payload(IReadOnlyList<string> tokens)
    {
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == "payload")
            {
                return i + 1;
            }
        }

        return -1;
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

    /// <summary>What a refusal from this file calls the thing it was reading.</summary>
    private const string Subject = "A string table line";

    private static int Number(Dictionary<string, string> fields, string name) =>
        AssemblyText.Number(
            AssemblyText.Text(fields, name, Subject), $"'{name}' field", Subject);

    private static byte[] Hex(IReadOnlyList<string> tokens, int index, string what) =>
        AssemblyText.Hex(
            AssemblyText.Token(tokens, index, $"a {what}", Subject), what, Subject);

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
