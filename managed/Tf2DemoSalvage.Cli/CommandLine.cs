using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Cli;

/// <summary>How much the tool reports about its own progress.</summary>
/// <remarks>
/// Three levels rather than a bool, because the two useful directions are opposites: a batch run
/// over a corpus wants less than the default, and a failing demo wants more.
/// </remarks>
public enum Verbosity
{
    /// <summary>Warnings and errors only.</summary>
    Quiet = 0,

    /// <summary>The default: what was read, what was written, and anything unusual.</summary>
    Normal = 1,

    /// <summary>Per-stage detail, including timings and per-command-type counts.</summary>
    Verbose = 2,
}

/// <summary>What the tool should write.</summary>
public enum OutputFormat
{
    /// <summary>Header, counts, players, events, and a per-command listing.</summary>
    Dump = 0,

    /// <summary>The dump without the per-command listing.</summary>
    Summary = 1,

    /// <summary>The demo decompiled to text, message by message, in stream order.</summary>
    Trace = 2,

    /// <summary>One JSON object per line.</summary>
    JsonLines = 3,

    /// <summary>
    /// The assembly form: complete, compilable back into the demo, and not meant to be read.
    /// </summary>
    Assembly = 4,
}

/// <summary>
/// The command line, parsed.
/// </summary>
/// <remarks>
/// Split out from <see cref="Program"/> so the argument grammar can be tested without running
/// the tool. Parsing is where a command-line program is most likely to be quietly wrong — an
/// option that consumes the wrong number of arguments, or a flag that silently loses to
/// another — and none of that is reachable through a test that only checks the output of a
/// successful run.
/// </remarks>
public sealed record CommandLine
{
    /// <summary>The demo to read.</summary>
    public required string DemoPath { get; init; }

    /// <summary>Where to write, or <c>null</c> for standard output.</summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Whether to read assembly text and write a demo, rather than the other way round.
    /// </summary>
    /// <remarks>
    /// A direction rather than a format, which is why it is separate from
    /// <see cref="OutputFormat"/>: the input is text and the output is a <c>.dem</c>, so every
    /// other option describes something that does not apply.
    /// </remarks>
    public bool Compile { get; init; }

    /// <summary>What to write.</summary>
    public OutputFormat Format { get; init; }

    /// <summary>Whether to expand entity snapshots. Only meaningful for a trace.</summary>
    public bool IncludeEntities { get; init; }

    /// <summary>Stop expanding snapshots after this many, or zero for all of them.</summary>
    public int EntitySnapshotLimit { get; init; }

    /// <summary>The parse failure, or <c>null</c> if the command line was valid.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the user asked for help.</summary>
    public bool HelpRequested { get; init; }

    /// <summary>How much to report about the run itself.</summary>
    /// <remarks>
    /// Diagnostics only. Nothing here changes what the tool writes to standard output — the
    /// demo's decoded form goes there and must stay pipeable, so every log line goes to standard
    /// error regardless of level.
    /// </remarks>
    public Verbosity Verbosity { get; init; }

    /// <summary>Parses arguments.</summary>
    /// <param name="args">The raw arguments.</param>
    /// <returns>The parsed command line, with <see cref="Error"/> set if it was invalid.</returns>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        if (args is null || args.Count == 0)
        {
            return Invalid("no demo given");
        }

        if (args[0] is "-h" or "--help" or "/?")
        {
            return new CommandLine { DemoPath = string.Empty, HelpRequested = true };
        }

        string demoPath = args[0];
        string? outputPath = null;
        OutputFormat format = OutputFormat.Dump;
        bool compile = false;
        bool entities = false;
        int limit = 0;
        Verbosity verbosity = Verbosity.Normal;

        // An explicit cursor rather than a for-loop: options that take a value consume two
        // arguments, and advancing a for-loop's counter from inside its body reads badly
        // (and trips S127).
        int index = 1;
        while (index < args.Count)
        {
            string argument = args[index];
            index++;

            switch (argument)
            {
                case "-o" or "--output":
                    if (index >= args.Count)
                    {
                        return Invalid("-o requires a path");
                    }

                    outputPath = args[index];
                    index++;
                    break;

                case "-s" or "--summary":
                    format = OutputFormat.Summary;
                    break;

                case "-t" or "--trace":
                    format = OutputFormat.Trace;
                    break;

                case "-j" or "--jsonl":
                    format = OutputFormat.JsonLines;
                    break;

                case "-a" or "--asm":
                    format = OutputFormat.Assembly;
                    break;

                case "-c" or "--compile":
                    compile = true;
                    break;

                case "-v" or "--verbose":
                    verbosity = Verbosity.Verbose;
                    break;

                case "-q" or "--quiet":
                    verbosity = Verbosity.Quiet;
                    break;

                case "-e" or "--entities":
                    entities = true;
                    break;

                case "--entity-limit":
                    if (index >= args.Count)
                    {
                        return Invalid("--entity-limit requires a count");
                    }

                    if (!int.TryParse(
                            args[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out limit) || limit < 0)
                    {
                        return Invalid($"--entity-limit needs a non-negative number, not '{args[index]}'");
                    }

                    index++;

                    // Asking for a limit without asking for entities is a command line that
                    // would silently do nothing. Treating it as a request for both is the
                    // reading that cannot be a mistake.
                    entities = true;
                    break;

                default:
                    return Invalid($"unrecognised option '{argument}'");
            }
        }

        if (compile && outputPath is null)
        {
            // A demo is binary and standard output is text. Writing one to the other produces a
            // file that looks right in a terminal and will not play.
            return Invalid("--compile needs -o: a demo cannot go to standard output");
        }

        return new CommandLine
        {
            DemoPath = demoPath,
            OutputPath = outputPath,
            Format = format,
            IncludeEntities = entities,
            EntitySnapshotLimit = limit,
            Compile = compile,
            Verbosity = verbosity,
        };
    }

    private static CommandLine Invalid(string error) =>
        new() { DemoPath = string.Empty, Error = error };
}
