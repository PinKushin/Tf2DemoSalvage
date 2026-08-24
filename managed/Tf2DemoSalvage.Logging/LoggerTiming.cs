using System;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Logging;

/// <summary>Times an operation and logs how long it took.</summary>
/// <remarks>
/// **`ViewerLog.Time`'s replacement, kept because the reason for it holds (D83).** Timings belong in
/// the log rather than in a comment: "the map took 1.1 seconds" is a claim that goes stale, and a
/// line saying so on every load is a claim that cannot.
///
/// An extension rather than a member of <see cref="ILogger"/>, obviously — but also rather than a
/// logging SCOPE, which is what `BeginScope` would suggest. A scope decorates other lines; this
/// emits one of its own, on disposal, with a number that did not exist when the scope opened.
/// </remarks>
public static class LoggerTiming
{
    /// <summary>Times an operation, logging on disposal.</summary>
    /// <param name="logger">Where to write.</param>
    /// <param name="what">What is being timed.</param>
    /// <returns>A scope that logs when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public static IDisposable Time(this ILogger logger, string what)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new Timing(logger, what);
    }

    private sealed class Timing(ILogger logger, string what) : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        // **Guarded on `IsEnabled`, and the format lives in the template rather than in a
        // `ToString` at the call site.** CA1873 is right on both counts: `Elapsed` allocates a
        // TimeSpan and the argument is evaluated before the level is ever consulted, and a timing
        // scope disposes on every load whether anyone is listening.
        public void Dispose()
        {
            if (!logger.IsEnabled(LogLevel.Information))
            {
                return;
            }

            logger.LogInformation("{What} took {Seconds:0.00}s", what, _clock.Elapsed.TotalSeconds);
        }
    }
}
