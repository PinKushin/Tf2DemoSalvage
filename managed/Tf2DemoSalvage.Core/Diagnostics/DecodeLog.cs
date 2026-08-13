using System;

namespace Tf2DemoSalvage.Core.Diagnostics;

/// <summary>
/// Where Core and Content report things they could not read.
/// </summary>
/// <remarks>
/// **These projects were silent because the viewer's logger was out of reach**, not because there
/// was nothing to say. A library that cannot see the application's log has two honest options —
/// throw, or return nothing — and both were being used in places where the right answer was "carry
/// on, but say so". The result was a catch with a justifying comment and no output, which is the
/// silent fallback this repo bans everywhere else.
///
/// **A sink rather than a logger.** Core has no business knowing about files, the viewer's format,
/// or whether anyone is listening at all. It writes a category and a message; the application
/// decides what that means. Nothing is written when no sink is attached, which keeps a library
/// consumer free of a logging dependency it never asked for.
///
/// Deliberately not an interface passed through every call: the alternative is threading a logger
/// argument through every reader in two projects, which is a large change to say one small thing.
/// </remarks>
public static class DecodeLog
{
    /// <summary>Where messages go, or null when nobody is listening.</summary>
    /// <remarks>
    /// Set once by the application at startup. A static is the right shape here precisely because
    /// there is one process and one log; anything richer would be ceremony around a string.
    /// </remarks>
    public static Action<string, string>? Sink { get; set; }

    /// <summary>Where ordinary observations go, when a host has offered somewhere.</summary>
    /// <remarks>
    /// **Separate from <see cref="Sink"/> because a count is not a warning.** The viewer routes
    /// losses to its warning channel, and sending "read 4,812 ambient samples" there would train
    /// a reader to ignore the word. Both are worth having: one says what went wrong, the other
    /// says what the reader actually found, and the second is what makes an absence visible.
    /// </remarks>
    public static Action<string, string>? Notes { get; set; }

    /// <summary>Records what a reader found, whether or not anything went wrong.</summary>
    /// <param name="category">Which reader is speaking.</param>
    /// <param name="message">What it read, with numbers.</param>
    /// <remarks>
    /// **Counts, because an empty result is the failure that reports nothing.** A map with no
    /// ambient samples and a reader that silently returned none look identical from the outside;
    /// a number distinguishes them without anyone having to reproduce the problem.
    /// </remarks>
    public static void Note(string category, string message) => Notes?.Invoke(category, message);

    /// <summary>Reports something that could not be read, without failing the read.</summary>
    /// <param name="category">Which reader is speaking, such as <c>assets</c> or <c>entities</c>.</param>
    /// <param name="message">What could not be read, and what happened instead.</param>
    /// <remarks>
    /// **Say what was lost, not that something went wrong.** "A custom folder was unreadable" is
    /// an event; "a custom folder was unreadable, so its material overrides are not in the search
    /// path" is a diagnosis, and the second is what makes a log worth reading at three in the
    /// morning.
    /// </remarks>
    public static void Lost(string category, string message) => Sink?.Invoke(category, message);

    /// <summary>Reports a failure that cost something, naming the exception.</summary>
    /// <param name="category">Which reader is speaking.</param>
    /// <param name="message">What was being attempted.</param>
    /// <param name="failure">The exception, whose type and message are included.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is null.</exception>
    public static void Lost(string category, string message, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Sink?.Invoke(category, $"{message}: {failure.GetType().Name}: {failure.Message}");
    }
}
