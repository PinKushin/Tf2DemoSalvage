using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Cli;

/// <summary>
/// Command-line entry point: reads a demo and writes it in one of several readable forms.
/// </summary>
/// <remarks>
/// Output goes to standard output by default so it can be piped or redirected; <c>-o</c>
/// writes to a file instead. Both are the same code path — every writer takes a
/// <see cref="TextWriter"/> and does not care which it got.
///
/// Progress is reported to standard error, and only when writing to a file. Standard output
/// may be the destination, and a progress bar interleaved with the trace would corrupt it.
/// </remarks>
public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 2;
    private const int ExitFailure = 1;

    /// <summary>Entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>0 on success, 1 on failure, 2 on a usage error.</returns>
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        CommandLine line = CommandLine.Parse(args);

        if (line.HelpRequested)
        {
            WriteUsage(Console.Out);
            return ExitSuccess;
        }

        if (line.Error is not null)
        {
            Console.Error.WriteLine($"error: {line.Error}.");
            WriteUsage(Console.Error);
            return ExitUsage;
        }

        try
        {
            return Run(line);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Expected failure modes: a file that is missing, unreadable, or not a demo.
            // Anything else is a defect and should surface with its stack trace rather than
            // being flattened into a tidy message.
            Console.Error.WriteLine($"error: {exception.Message}");
            return ExitFailure;
        }
    }

    private static int Run(CommandLine line)
    {
        if (!File.Exists(line.DemoPath))
        {
            Console.Error.WriteLine($"error: no such file: {line.DemoPath}");
            return ExitFailure;
        }

        byte[] bytes = File.ReadAllBytes(line.DemoPath);
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        string name = Path.GetFileName(line.DemoPath);

        if (line.OutputPath is null)
        {
            Write(Console.Out, line, name, header, commands, null);
            return ExitSuccess;
        }

        // Nested so the bar is disposed *after* the writer but *before* the summary line
        // below: its Dispose ends the progress line, and printing the summary first would
        // append it to a line the bar had not finished. An earlier version called Finish()
        // explicitly here, which mutation testing flagged as unkillable - scoping expresses
        // the same ordering without a statement whose removal nothing can observe.
        using (ProgressBar bar = new(Console.Error))
        using (StreamWriter writer = new(line.OutputPath))
        {
            Write(writer, line, name, header, commands, bar);
        }

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"wrote {commands.Count} commands to {line.OutputPath}"));

        return ExitSuccess;
    }

    /// <summary>Writes the demo in whichever form was asked for.</summary>
    private static void Write(
        TextWriter writer,
        CommandLine line,
        string name,
        DemoHeader header,
        IReadOnlyList<DemoCommand> commands,
        IProgress<DumpProgress>? progress)
    {
        switch (line.Format)
        {
            case OutputFormat.Trace:
                DemoTraceWriter.Write(writer, name, header, commands, progress, new DemoTraceOptions
                {
                    IncludeEntities = line.IncludeEntities,
                    EntitySnapshotLimit = line.EntitySnapshotLimit,
                });
                break;

            case OutputFormat.JsonLines:
                DemoJsonLinesWriter.Write(writer, name, header, commands, progress);
                break;

            default:
                // Summary and Dump differ only by the command listing, so they share a branch
                // rather than each getting an empty case that says nothing.
                DemoTextDumper.Write(
                    writer, name, header, commands,
                    new DemoDumpOptions { IncludeCommandListing = line.Format != OutputFormat.Summary },
                    progress);
                break;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("tf2demosalvage - read a TF2 demo in readable form");
        writer.WriteLine();
        writer.WriteLine("usage: tf2demosalvage <demo.dem> [-o <out.txt>] [-s|-t|-j] [-e] [--entity-limit <n>]");
        writer.WriteLine();
        writer.WriteLine("  -o, --output <path>     write to a file instead of standard output");
        writer.WriteLine();
        writer.WriteLine("  -s, --summary           header, counts, players and events; no command listing");
        writer.WriteLine("  -t, --trace             decompile to text, message by message, in stream order");
        writer.WriteLine("  -j, --jsonl             one JSON object per line");
        writer.WriteLine("                          (default: the summary plus a per-command listing)");
        writer.WriteLine();
        writer.WriteLine("  -e, --entities          expand entity snapshots into properties (trace only)");
        writer.WriteLine("      --entity-limit <n>  expand only the first n snapshots; implies -e");
        writer.WriteLine();
        writer.WriteLine("  -h, --help              this message");
        writer.WriteLine();
        writer.WriteLine("Entities are off by default because expanding them turns a 39 MB demo into");
        writer.WriteLine("gigabytes of text. --entity-limit is the practical way to inspect them.");
    }
}
