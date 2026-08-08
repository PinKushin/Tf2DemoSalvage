using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Container;

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
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/>, <paramref name="header"/>, or <paramref name="commands"/> is
    /// <c>null</c>.
    /// </exception>
    public static void Write(
        TextWriter writer,
        string fileName,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands,
        DemoDumpOptions? options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(commands);

        options ??= new DemoDumpOptions();
        writer.NewLine = "\n";

        WriteHeaderSection(writer, fileName, header);
        WriteSummarySection(writer, header, commands);

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
