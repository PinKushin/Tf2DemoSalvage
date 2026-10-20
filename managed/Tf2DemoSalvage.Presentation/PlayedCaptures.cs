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

    /// <summary>The capture asked for last frame, whose PNG the device has written by now; see <see cref="SidecarDue"/>.</summary>
    private string? _asked;

    private PlayedCaptures(string folder, IEnumerable<int> ticks)
    {
        Folder = folder;
        List<int> sorted = [.. ticks];
        sorted.Sort();
        _ticks = new Queue<int>(sorted);

        // The sidecar is written straight to the folder; the PNG writer would make it only a frame later.
        Directory.CreateDirectory(folder);
    }

    /// <summary>Where the captures go.</summary>
    public string Folder { get; }

    /// <summary>Reads the variable.</summary>
    /// <param name="value">"folder tick tick ...", or null.</param>
    /// <param name="problem">Why a value that names ticks was refused, for the caller's warning; null otherwise.</param>
    /// <returns>The list, or null when unset, naming no tick, or holding a token that is not a whole tick of 0 or more.</returns>
    /// <remarks>A folder with a space in it cannot be named: its second word is refused as a tick, and said so.</remarks>
    public static PlayedCaptures? Parse(string? value, out string? problem)
    {
        problem = null;
        string[] parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<int> ticks = [];

        for (int index = 1; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out int tick))
            {
                problem = $"TF2VIEW_PLAY_SHOTS: '{parts[index]}' is not a tick (a whole number, 0 or more), so nothing is captured";
                return null;
            }

            ticks.Add(tick);
        }

        return ticks.Count == 0 ? null : new PlayedCaptures(parts[0], ticks);
    }

    /// <summary>The capture this frame owes, or null: the earliest listed tick at or before the moment shown.</summary>
    /// <param name="shown">The moment shown, fraction included.</param>
    /// <returns>The file to write, named by the listed tick; one a frame, so ticks a long frame skipped follow on the next.</returns>
    /// <remarks>Ask <see cref="SidecarDue"/> first each frame: this arms the next frame's sidecar.</remarks>
    public string? Due(double shown)
    {
        if (_ticks.Count == 0 || _ticks.Peek() > shown)
        {
            return null;
        }

        _asked = Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"play-{_ticks.Dequeue()}.png"));

        return _asked;
    }

    /// <summary>The capture whose sidecar this frame owes, once: the one asked for on the frame before.</summary>
    /// <returns>Its PNG's path, or null.</returns>
    /// <remarks>
    /// **The device writes a capture on the NEXT present** (`Device3D`), after another played step, so a sidecar written
    /// beside the request described the frame before the picture. Written the frame after, it reads the tick and camera of
    /// the frame the PNG shows (D226: a TF2 capture is paired by that tick).
    /// </remarks>
    public string? SidecarDue()
    {
        string? owed = _asked;
        _asked = null;

        return owed;
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
