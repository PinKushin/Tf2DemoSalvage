using System;

using Microsoft.Extensions.Logging;

namespace Tf2DemoSalvage.Logging;

/// <summary>Bridges <see cref="ILogger"/> to the callback shapes older code already takes.</summary>
/// <remarks>
/// **Some of this solution was already dependency-inverted before `ILogger` arrived, and better than
/// it looked.** `GameArchives.Open(folder, Action&lt;string, string&gt; log)` takes an area and a
/// message as a delegate rather than reaching for a static — so it was never coupled to the logger
/// at all, and the conversion (D83) has nothing to change there.
///
/// This is the adapter that keeps it that way. The alternative was widening those signatures to take
/// `ILoggerFactory`, which would push a logging dependency down into `Content` — a project that
/// deliberately logs nothing and reports through return values instead.
/// </remarks>
public static class LoggerAdapters
{
    /// <summary>An area-and-message callback that writes through a factory.</summary>
    /// <param name="loggers">Where the lines go.</param>
    /// <returns>A callback taking an area and a message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggers"/> is null.</exception>
    /// <remarks>
    /// **The area becomes the category**, which is the same mapping the rest of the conversion uses,
    /// so a line written through here is indistinguishable from one written directly.
    ///
    /// The message is passed as an ARGUMENT rather than as the template. It is already-formatted
    /// text from a caller that knows nothing about templates, and using it as one would make every
    /// distinct message its own template — which is what CA2254 warns about, and would defeat
    /// structured logging rather than serve it.
    /// </remarks>
    public static Action<string, string> LogTo(this ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        return (area, message) => loggers.CreateLogger(area).LogInformation("{Message}", message);
    }
}
