using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Writes a human-readable dump of a demo, in the spirit of the Quake community's demo tools.
/// </summary>
/// <remarks>
/// Writes to a <see cref="TextWriter"/> rather than a path, so the console and a file are the
/// same code path, tests need no temp files, and 120,000 command rows stream out instead of
/// being built into one enormous string.
///
/// Two properties are deliberate and tested. Output is **culture-invariant**, because a dump
/// that renders 1814.02 as "1814,02" on a German machine cannot be diffed against one that
/// does not. And line endings are always LF, so dumps compare cleanly across platforms.
/// Both exist because this output is meant to be diffed — against previous runs to catch
/// regressions, and eventually against another parser (see <c>docs/RISKS.md</c> B4).
/// </remarks>
public static class DemoTextDumper
{
    private const string Separator
        = "--------------------------------------------------------------------";

    /// <summary>Writes the dump.</summary>
    /// <param name="writer">Destination. Console, file, or a string buffer in tests.</param>
    /// <param name="fileName">Name to report for the demo being dumped.</param>
    /// <param name="header">The demo's parsed header.</param>
    /// <param name="commands">The demo's commands, in stream order.</param>
    /// <param name="options">Reporting options, or <c>null</c> for the defaults.</param>
    /// <param name="progress">
    /// Optional listener for scan progress. The event scan walks every packet in the demo —
    /// tens of thousands on a full match — and silence for that long is indistinguishable from
    /// a hang.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/>, <paramref name="header"/>, or <paramref name="commands"/> is
    /// <c>null</c>.
    /// </exception>
    public static void Write(
        TextWriter writer,
        string fileName,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands,
        DemoDumpOptions? options,
        IProgress<DumpProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        options ??= new DemoDumpOptions();
        writer.NewLine = "\n";

        WriteHeaderSection(writer, fileName, header);
        WriteSummarySection(writer, header, commands);

        // One pass, two sections. Both need the decoded message stream, and scanning twice
        // doubles the cost on a demo with a hundred thousand packets - which is what this did
        // when the player section was first added.
        if (options.IncludePlayers || options.IncludeGameEvents || options.IncludeChat)
        {
            DemoScan.Result scan = DemoScan.Run(commands, options.GameEventSampleSize, progress, (ushort)header.NetworkProtocol);

            if (options.IncludePlayers)
            {
                WritePlayerSection(writer, scan.Players);
            }

            if (options.IncludeChat)
            {
                WriteChatSection(writer, scan.Chat, options.ChatSampleSize);
            }

            if (options.IncludeGameEvents)
            {
                WriteKillFeedSection(writer, scan);
                WriteGameEventSection(writer, scan);
            }
        }

        if (options.IncludeCommandListing)
        {
            WriteCommandListing(writer, commands);
        }
    }

    private static void WriteHeaderSection(TextWriter writer, string fileName, DemoHeader header)
    {
        writer.WriteLine(Separator);
        writer.WriteLine($"Demo dump: {fileName}");
        writer.WriteLine(Separator);
        WriteField(writer, "Demo protocol", header.DemoProtocol.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "Network protocol", header.NetworkProtocol.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "Server", header.ServerName);
        WriteField(writer, "Client", header.ClientName);
        WriteField(writer, "Map", header.MapName);
        WriteField(writer, "Game directory", header.GameDirectory);
        WriteField(writer, "Playback time", string.Create(
            CultureInfo.InvariantCulture, $"{header.PlaybackTimeSeconds:F2} s"));
        WriteField(writer, "Playback ticks", header.PlaybackTicks.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "Playback frames", header.PlaybackFrames.ToString(CultureInfo.InvariantCulture));
        WriteField(writer, "Signon length", string.Create(
            CultureInfo.InvariantCulture, $"{header.SignonLengthBytes} bytes"));
        writer.WriteLine();
    }

    private static void WriteSummarySection(
        TextWriter writer,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands)
    {
        SortedDictionary<DemoCommandType, int> counts = new();
        SortedDictionary<DemoCommandType, long> payloadBytes = new();
        int packets = 0;

        foreach (DemoCommand command in commands)
        {
            counts.TryGetValue(command.Type, out int count);
            counts[command.Type] = count + 1;

            payloadBytes.TryGetValue(command.Type, out long bytes);
            payloadBytes[command.Type] = bytes + command.Payload.Length;

            if (command.Type == DemoCommandType.Packet)
            {
                packets++;
            }
        }

        writer.WriteLine("Command summary");
        foreach ((DemoCommandType type, int count) in counts)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {WireName(type),-18} {count,10}   {payloadBytes[type],14} payload bytes"));
        }

        writer.WriteLine();

        // The single most useful correctness signal available: an off-by-one anywhere in the
        // container walk drifts and never lands exactly on the declared frame count. Reporting
        // it in the dump means a human reading the output sees the check, not just the data.
        bool agrees = packets == header.PlaybackFrames;
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Frame check: {packets} dem_packet vs {header.PlaybackFrames} declared -> " +
            $"{(agrees ? "ok" : "MISMATCH")}"));
        writer.WriteLine();
    }

    /// <summary>
    /// Lists the players the <c>userinfo</c> string table names.
    /// </summary>
    /// <remarks>
    /// Two identifiers appear here because two exist. Game events carry <c>user_id</c> and
    /// entities are addressed by index; this table is where they meet. Printing both is what
    /// lets a reader follow a kill from the event log to the entity that moved.
    /// </remarks>
    private static void WritePlayerSection(
        TextWriter writer, IReadOnlyDictionary<int, PlayerInfo> players)
    {
        writer.WriteLine(Separator);
        writer.WriteLine("Players");
        writer.WriteLine(Separator);

        if (players.Count == 0)
        {
            writer.WriteLine("  none found");
            writer.WriteLine();
            return;
        }

        writer.WriteLine("  entity  userid  name                             steam id");

        foreach (PlayerInfo player in players.Values)
        {
            // A SourceTV slot is not a player and a bot is not a person; both are marked so a
            // reader counting a roster does not include them.
            string note = string.Empty;
            if (player.IsSourceTv)
            {
                note = " (SourceTV)";
            }
            else if (player.IsBot)
            {
                note = " (bot)";
            }

            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {player.EntityIndex,6}  {player.UserId,6}  {player.Name,-32} " +
                $"{player.SteamId}{note}"));
        }

        writer.WriteLine();
    }

    /// <summary>
    /// Writes the match's chat log.
    /// </summary>
    /// <remarks>
    /// Two shapes reach here. A player message carries a channel and a sender; a server or
    /// plugin message carries neither and is rendered as such rather than as empty punctuation,
    /// which is what a naive template produces.
    /// </remarks>
    private static void WriteChatSection(
        TextWriter writer, List<(int Tick, ChatMessage Chat)> chat, int sampleSize)
    {
        writer.WriteLine(Separator);
        writer.WriteLine("Chat");
        writer.WriteLine(Separator);

        if (chat.Count == 0)
        {
            writer.WriteLine("  none");
            writer.WriteLine();
            return;
        }

        WriteField(writer, "Lines", chat.Count.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine();

        foreach ((int tick, ChatMessage line) in chat.Take(sampleSize))
        {
            string who = string.IsNullOrEmpty(line.From) ? "(server)" : line.From;
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  tick {tick,-8} {who}: {line.Text}"));
        }

        if (chat.Count > sampleSize)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  ... {chat.Count - sampleSize} more"));
        }

        writer.WriteLine();
    }

    /// <summary>
    /// Decodes the packet stream and reports what happened in the match.
    /// </summary>
    /// <remarks>
    /// Everything above this in the dump describes the file. This describes the game, which is
    /// the point of having a parser at all.
    ///
    /// A demo whose events cannot be decoded says so rather than printing an empty section — an
    /// absent section and a match with no events are different facts, and a dump that renders
    /// them identically hides a parser failure behind a plausible-looking report.
    /// </remarks>
    private static void WriteGameEventSection(TextWriter writer, DemoScan.Result scan)
    {
        Dictionary<string, int> counts = scan.EventCounts;
        List<(int Tick, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)> sample =
            scan.EventSample;
        int total = scan.EventTotal;

        writer.WriteLine(Separator);
        writer.WriteLine("Game events");
        writer.WriteLine(Separator);

        if (total == 0)
        {
            writer.WriteLine("  none decoded");
            writer.WriteLine();
            return;
        }

        WriteField(writer, "Events", string.Create(
            CultureInfo.InvariantCulture, $"{total} across {counts.Count} types"));
        writer.WriteLine();

        foreach ((string name, int count) in counts.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-32} {count,8}"));
        }

        writer.WriteLine();
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  First {sample.Count} in order:"));
        writer.WriteLine();

        // Same history the kill feed uses, and for the same reason: an event names whoever was
        // playing when it fired, not whoever holds that entity slot at the end of the demo.
        Dictionary<int, PlayerInfo> byUserId = scan.Everyone;

        foreach ((int tick, string name, IReadOnlyList<KeyValuePair<string, object?>> fields) in sample)
        {
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tick {tick,-8} {name,-28} {Describe(fields, byUserId)}"));
        }

        writer.WriteLine();
    }

    /// <summary>
    /// Renders an event's fields compactly, in wire order, naming players where it can.
    /// </summary>
    /// <remarks>
    /// **Resolution falls back rather than guessing.** A field this parser wrongly believes
    /// names a player, or an id belonging to someone who has since left, prints the number it
    /// read. That is honest; inventing a name would not be, and a wrong name in a kill log is
    /// exactly the kind of plausible-looking error this codebase keeps meeting.
    ///
    /// Two identifier spaces are in play and they overlap numerically, so which map to use is
    /// decided by the field's name: anything containing <c>entindex</c> is an entity index,
    /// everything else is a user id. Looking up an entity index in the user map would print a
    /// real name for a player who is not involved.
    /// </remarks>
    /// <summary>Writes every death in order, in the game's kill feed shape.</summary>
    /// <remarks>
    /// **Separate from the game event section, and complete rather than sampled.** That section
    /// answers "what kinds of thing happened and roughly what do they look like", capped so a demo
    /// with 400 kills does not print 400 raw field dumps. This answers "what happened in the match",
    /// which is a sequence — and the first entry of a sequence is not a summary of it.
    ///
    /// Player references are resolved here rather than during the scan because a userinfo update can
    /// name a player *after* an event referencing them.
    /// </remarks>
    private static void WriteKillFeedSection(TextWriter writer, DemoScan.Result scan)
    {
        if (scan.Kills.Count == 0)
        {
            return;
        }

        writer.WriteLine(Separator);
        writer.WriteLine("Kills");
        writer.WriteLine(Separator);

        // **scan.Everyone, not scan.Players.** The slot map holds who occupied each entity index
        // last; a kill references whoever was playing at the time, and in the modern corpus demo six
        // of those had their slots taken over by later joiners and by bots. Naming from the slot map
        // printed bare user ids for players the demo names perfectly well.
        Dictionary<int, PlayerInfo> byUserId = scan.Everyone;

        foreach ((int tick, IReadOnlyList<KeyValuePair<string, object?>> fields) in scan.Kills)
        {
            // **Only the name fields are rendered; the rest keep their numeric type.**
            // PlayerReferences.Render returns a string for everything, so resolving the whole list
            // turned customkill, death_flags and damagebits into text — and KillFeed reads those as
            // numbers, so it silently annotated nothing. The feed shipped without "(headshot)" or
            // "(crit)" on any line while its unit tests, which pass raw values, stayed green.
            //
            // Second time this exact shape has bitten in this file. The first was the dumper's
            // annotation matching `int` when the value arrives as a byte.
            List<KeyValuePair<string, object?>> resolved = [];
            foreach (KeyValuePair<string, object?> field in fields)
            {
                bool isName = field.Key is "userid" or "attacker" or "assister";

                resolved.Add(isName
                    ? new(field.Key, PlayerReferences.Render(field, byUserId))
                    : field);
            }

            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  tick {tick,-8} {KillFeed.Line(resolved)}"));
        }

        writer.WriteLine();
    }

    private static string Describe(
        IReadOnlyList<KeyValuePair<string, object?>> fields,
        IReadOnlyDictionary<int, PlayerInfo> byUserId)
    {
        StringBuilder detail = new();

        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (detail.Length > 0)
            {
                detail.Append(' ');
            }

            detail.Append(field.Key).Append('=').Append(PlayerReferences.Render(field, byUserId));

            // **Annotate in place rather than replacing the raw value.** The number stays because
            // it is what the demo actually contains and this dump is meant to be checkable against
            // the bytes; the word is added beside it because "customkill=1" tells a reader nothing
            // and "customkill=1 (headshot)" tells them the most interesting thing about the kill.
            //
            // Only qualifiers that change how a kill READS are annotated. Deliberately not a general
            // "make every field pretty" pass, which would be a large surface with no obvious edge.
            if (Annotate(field) is { } note)
            {
                detail.Append(" (").Append(note).Append(')');
            }
        }

        return detail.ToString();
    }

    /// <summary>A human-readable note for a field whose number hides its meaning.</summary>
    /// <remarks>
    /// Returns null for everything else, so the dump stays raw by default. The mappings live in
    /// <see cref="KillDescription"/> and are held against the SDK by
    /// <c>KillDescriptionConformanceTests</c> — which caught them being transcribed wrongly the
    /// first time.
    /// </remarks>
    private static string? Annotate(KeyValuePair<string, object?> field)
    {
        // **Every integral width, not just int.** Game event fields are typed by the event
        // definition — customkill arrives as a byte and death_flags as a short — so a pattern match
        // on `int` alone matches neither, and the annotation silently did nothing while its own unit
        // tests passed. Caught by looking at the actual output rather than at the function.
        int value = field.Value switch
        {
            int whole => whole,
            short small => small,
            byte tiny => tiny,
            _ => -1,
        };

        if (value < 0)
        {
            return null;
        }

        return field.Key switch
        {
            "customkill" => KillDescription.CustomKill(value),
            "death_flags" => KillDescription.DeathFlags(value),
            "damagebits" => KillDescription.DamageTypes(value),
            _ => null,
        };
    }

    private static void WriteCommandListing(TextWriter writer, IReadOnlyList<DemoCommand> commands)
    {
        writer.WriteLine("Commands");
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {"#",8}  {"tick",10}  {"command",-18} {"payload",12}"));

        for (int i = 0; i < commands.Count; i++)
        {
            DemoCommand command = commands[i];
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {i + 1,8}  {command.Tick,10}  {WireName(command.Type),-18} " +
                $"{command.Payload.Length,12}"));
        }
    }

    private static void WriteField(TextWriter writer, string label, string value) =>
        writer.WriteLine($"{label,-18} {value}");

    /// <summary>
    /// The name TF2 itself uses, rather than the C# enum member — a dump is for humans reading
    /// alongside Valve's own documentation.
    /// </summary>
    private static string WireName(DemoCommandType type) => type switch
    {
        DemoCommandType.Signon => "dem_signon",
        DemoCommandType.Packet => "dem_packet",
        DemoCommandType.SyncTick => "dem_synctick",
        DemoCommandType.ConsoleCmd => "dem_consolecmd",
        DemoCommandType.UserCmd => "dem_usercmd",
        DemoCommandType.DataTables => "dem_datatables",
        DemoCommandType.Stop => "dem_stop",
        DemoCommandType.StringTables => "dem_stringtables",
        _ => type.ToString(),
    };
}
