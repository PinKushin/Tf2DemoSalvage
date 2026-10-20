using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Captures taken while PLAYING, at listed ticks: <c>TF2VIEW_PLAY_SHOTS="folder tick tick ..."</c> (scripts only).</summary>
/// <remarks>
/// **A `--shot` reaches its tick by seek**, so a capture of anything only played time does (an effect's creation frame, its
/// sleep, what its operators kill) never ran in one: the byte-identical explosion pair of finding 82. With
/// `--autoplay --measure`, each listed tick is captured on the first frame shown at or past it. Paired with
/// <see cref="FixedStepTime"/>, two builds show the same moments.
/// </remarks>
public sealed class PlayedCaptures
{
    private readonly Queue<int> _ticks;

    private PlayedCaptures(string folder, IEnumerable<int> ticks)
    {
        Folder = folder;
        List<int> sorted = [.. ticks];
        sorted.Sort();
        _ticks = new Queue<int>(sorted);
    }

    /// <summary>Where the captures go.</summary>
    public string Folder { get; }

    /// <summary>Reads the variable.</summary>
    /// <param name="value">"folder tick tick ...", or null.</param>
    /// <returns>The list, or null when unset or naming no tick.</returns>
    public static PlayedCaptures? Parse(string? value)
    {
        string[] parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<int> ticks = [];

        for (int index = 1; index < parts.Length; index++)
        {
            if (int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick))
            {
                ticks.Add(tick);
            }
        }

        return ticks.Count == 0 ? null : new PlayedCaptures(parts[0], ticks);
    }

    /// <summary>The capture this frame owes, or null: the earliest listed tick at or before the moment shown.</summary>
    /// <param name="shown">The moment shown, fraction included.</param>
    /// <returns>The file to write, named by the listed tick; one a frame, so ticks a long frame skipped follow on the next.</returns>
    public string? Due(double shown)
    {
        if (_ticks.Count == 0 || _ticks.Peek() > shown)
        {
            return null;
        }

        return Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"play-{_ticks.Dequeue()}.png"));
    }
}

/// <summary>Playback time that advances a fixed step every frame: <c>TF2VIEW_FIXED_FRAME="seconds"</c> (scripts only).</summary>
/// <remarks>
/// **So two runs show the same moments.** Wall-clock frames land at different fractions of a tick every run, and a played
/// particle depends on every frame's length (D221), so two captures of one build differed everywhere. Not a parity claim
/// for any engine cvar. Read once a frame by <see cref="PlaybackPresenter.Advance"/>, which restarts it after.
/// </remarks>
public sealed class FixedStepTime : IElapsedTime
{
    private readonly double _step;
    private bool _running;

    /// <summary>A clock of one fixed step.</summary>
    /// <param name="step">Seconds a frame.</param>
    public FixedStepTime(double step) => _step = step;

    /// <summary>Reads the variable.</summary>
    /// <param name="value">Seconds a frame, or null.</param>
    /// <returns>The clock, or null when unset or not a positive number.</returns>
    public static FixedStepTime? Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double step) && step > 0d
            ? new FixedStepTime(step)
            : null;

    /// <inheritdoc />
    public double Seconds => _running ? _step : 0d;

    /// <inheritdoc />
    public void Restart() => _running = true;

    /// <inheritdoc />
    public void Reset() => _running = false;
}
