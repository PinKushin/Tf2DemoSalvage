using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Cli;

/// <summary>
/// Command-line entry point: reads a demo and writes a human-readable dump.
/// </summary>
/// <remarks>
/// Output goes to standard output by default so it can be piped or redirected; <c>-o</c>
/// writes to a file instead. Both are the same code path — <see cref="DemoTextDumper"/> takes
/// a <see cref="TextWriter"/> and does not care which it got.
/// </remarks>
public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 2;
    private const int ExitFailure = 1;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || IsHelpRequest(args[0]))
        {
            WriteUsage(Console.Out);
            return args.Length == 0 ? ExitUsage : ExitSuccess;
        }

        try
        {
            return Run(args);
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

    private static int Run(string[] args)
    {
        string demoPath = args[0];
        string? outputPath = null;
        bool summaryOnly = false;

        // An explicit cursor rather than a for-loop: options that take a value consume two
        // arguments, and advancing a for-loop's counter from inside its body reads badly
        // (and trips S127).
        int index = 1;
        while (index < args.Length)
        {
            string argument = args[index];
            index++;

            switch (argument)
            {
                case "-o" or "--output":
                    if (index >= args.Length)
                    {
                        Console.Error.WriteLine("error: -o requires a path.");
                        return ExitUsage;
                    }

                    outputPath = args[index];
                    index++;
                    break;

                case "-s" or "--summary":
                    summaryOnly = true;
                    break;

                default:
                    Console.Error.WriteLine($"error: unrecognised option '{argument}'.");
                    WriteUsage(Console.Error);
                    return ExitUsage;
            }
        }

        if (!File.Exists(demoPath))
        {
            Console.Error.WriteLine($"error: no such file: {demoPath}");
            return ExitFailure;
        }

        byte[] bytes = File.ReadAllBytes(demoPath);
        DemoHeader header = DemoHeader.Parse(bytes);
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))];

        var options = new DemoDumpOptions { IncludeCommandListing = !summaryOnly };

        if (outputPath is null)
        {
            DemoTextDumper.Write(
                Console.Out, Path.GetFileName(demoPath), header, commands, options);
        }
        else
        {
            using var writer = new StreamWriter(outputPath);
            DemoTextDumper.Write(
                writer, Path.GetFileName(demoPath), header, commands, options);
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"wrote {commands.Count} commands to {outputPath}"));
        }

        return ExitSuccess;
    }

    private static bool IsHelpRequest(string argument) =>
        argument is "-h" or "--help" or "/?";

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("tf2demosalvage - dump a TF2 demo in readable form");
        writer.WriteLine();
        writer.WriteLine("usage: tf2demosalvage <demo.dem> [-o <out.txt>] [-s]");
        writer.WriteLine();
        writer.WriteLine("  -o, --output   write to a file instead of standard output");
        writer.WriteLine("  -s, --summary  header and command counts only, no per-command rows");
        writer.WriteLine("  -h, --help     this message");
    }
}
