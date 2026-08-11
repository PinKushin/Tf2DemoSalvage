using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Tf2DemoSalvage.Cli;

/// <summary>
/// Builds the tool's logger, wired so that diagnostics can never contaminate the output.
/// </summary>
/// <remarks>
/// **Every log line goes to standard error, at every level.** The tool's real output — a trace, a
/// JSON Lines stream, the assembly form — goes to standard output and is routinely piped into
/// another program or redirected to a file. A single informational line on standard output would
/// corrupt that silently, and for the assembly form it would produce a file that no longer
/// compiles back into the demo.
///
/// The console provider does not do this by default: it writes everything below
/// <see cref="LogLevel.Error"/> to standard output. `LogToStandardErrorThreshold` is what moves
/// the rest, and setting it to <see cref="LogLevel.Trace"/> means "all of it".
///
/// **Logging lives in the CLI and not in <c>Core</c>.** The decode engine reports what happened by
/// returning it — <c>NetMessageReadResult</c> carries where a walk stopped and why — so a caller
/// cannot discard a decode failure without deciding to. A log line is a side effect that can be
/// ignored by default, which is the wrong shape for a parser. It would also cost Core its
/// dependency-free property, which the fuzz target depends on.
/// </remarks>
public static class LogSetup
{
    /// <summary>Creates a logger factory for the given verbosity.</summary>
    /// <param name="verbosity">How much to report.</param>
    /// <returns>A factory the caller owns and must dispose.</returns>
    public static ILoggerFactory Create(Verbosity verbosity) =>
        LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LevelFor(verbosity))
            .AddConsole(options =>
            {
                // Registering a formatter does not select it - without the name, the provider
                // keeps its default and writes a category line and an indented message for every
                // entry, three lines deep.
                options.FormatterName = TerseFormatter.FormatterName;
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            })
            .AddConsoleFormatter<TerseFormatter, ConsoleFormatterOptions>());

    /// <summary>The lowest level that will be written.</summary>
    /// <remarks>
    /// <see cref="Verbosity.Quiet"/> stops at warnings rather than at errors: a demo that decodes
    /// only in part is the single most important thing this tool can tell you, and a batch run
    /// that suppressed it would report success over a corpus it had half read.
    /// </remarks>
    internal static LogLevel LevelFor(Verbosity verbosity) => verbosity switch
    {
        Verbosity.Quiet => LogLevel.Warning,
        Verbosity.Verbose => LogLevel.Debug,
        _ => LogLevel.Information,
    };
}

/// <summary>A one-line formatter: a level tag and the message, and nothing else.</summary>
/// <remarks>
/// The default console formatter writes the category, an event id and a newline before every
/// message, which triples the height of a run over a corpus and buries the content. This tool's
/// log is read by a person watching a demo decode, so it is formatted for that.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the logging container through AddConsoleFormatter, which " +
                    "the analyser cannot see.")]
internal sealed class TerseFormatter() : ConsoleFormatter(FormatterName)
{
    /// <summary>The name this formatter registers under.</summary>
    public const string FormatterName = "terse";

    /// <inheritdoc />
    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, System.IO.TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        // Information is the default and needs no tag - tagging it would put a word in front of
        // every ordinary line for no information.
        string tag = logEntry.LogLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "debug: ",
            LogLevel.Warning => "warning: ",
            LogLevel.Error or LogLevel.Critical => "error: ",
            _ => string.Empty,
        };

        textWriter.Write(tag);
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }
}
