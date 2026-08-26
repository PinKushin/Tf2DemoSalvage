using System;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where playback is, and how that is written on screen.</summary>
/// <param name="Tick">The tick being shown.</param>
/// <param name="LastTick">The demo's final tick, or zero when none is open.</param>
/// <remarks>
/// **The format was a literal inside `TransportBar`** (B188, D90) — `$"tick {value} / {maximum}"` —
/// which is the same kind of thing as the speed wording that moved to <see cref="TimeScale"/>, and
/// it moved for the same reason: a rule about how a quantity reads is not a fact about a WinForms
/// control, and a second frontend should get it rather than reinvent it.
///
/// **The parser lives beside the format, and that is the point of the type.** A UI test used to
/// split the label on `/` and compare the halves, with a comment admitting the arrangement: *"the
/// format is the bar's own"*. Two places knowing one format is two places to change, and the test
/// would have gone red on a wording change with nothing wrong.
///
/// **Ticks do not start at zero**, so "at the end" is a comparison against the demo's own last tick
/// and never against a literal (`docs/memory/demo-ticks-do-not-start-at-zero.md`).
/// </remarks>
public readonly record struct DemoPosition(int Tick, int LastTick)
{
    /// <summary>Nothing open.</summary>
    public static DemoPosition None => default;

    /// <summary>Whether playback has reached the demo's last tick.</summary>
    /// <remarks>
    /// **False when no demo is open**, rather than true because zero equals zero. A viewer showing
    /// nothing has not reached the end of anything, and the `>= 0` reading is how a fresh window
    /// would otherwise report that it had.
    /// </remarks>
    public bool AtEnd => LastTick > 0 && Tick >= LastTick;

    /// <summary>The readout as it appears on the transport bar.</summary>
    /// <returns>Text of the form <c>tick 2500 / 8065</c>.</returns>
    public string Label() =>
        string.Create(CultureInfo.InvariantCulture, $"tick {Tick} / {LastTick}");

    /// <summary>Reads a position back out of a readout.</summary>
    /// <param name="readout">Text produced by <see cref="Label"/>.</param>
    /// <returns>The position, or null when the text is not that shape.</returns>
    /// <remarks>
    /// **Exists so a test can ask rather than parse.** The alternative is a test that owns a copy of
    /// the format, which passes until somebody rewords the label and then fails with nothing wrong —
    /// the same trap `TimeScale.Description` was made public to avoid.
    ///
    /// **Null rather than a guess for a malformed reading**, so a caller can say "not that shape"
    /// instead of asserting against a fabricated position. A sentinel here would conflate "the
    /// readout says something unexpected" with "playback is at tick zero".
    /// </remarks>
    public static DemoPosition? Read(string readout)
    {
        ArgumentNullException.ThrowIfNull(readout);

        string[] halves = readout
            .Replace("tick", string.Empty, StringComparison.Ordinal)
            .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return halves.Length == 2
            && int.TryParse(halves[0], CultureInfo.InvariantCulture, out int tick)
            && int.TryParse(halves[1], CultureInfo.InvariantCulture, out int last)
                ? new DemoPosition(tick, last)
                : null;
    }
}
