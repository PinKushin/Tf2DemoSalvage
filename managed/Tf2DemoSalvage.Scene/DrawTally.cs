using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// How many props were asked for, how many drew, and why the rest did not.
/// </summary>
/// <remarks>
/// **Every prop that does not draw is counted with its reason.** A silent <c>continue</c> is how
/// "all the props went away" became a guessing game: the scene said 14 models, the map showed one,
/// and nothing in between reported which test rejected the other thirteen.
///
/// Four categories, per the project's rule — asked for, what we have, what was produced, what is
/// missing and why.
///
/// **Split out of the draw loop on 2026-08-24** (B181). It was about forty of that loop's lines and
/// none of them are about drawing anything; keeping the counters beside the pose code is what let
/// the loop grow to two hundred lines without anybody noticing it had five jobs.
/// </remarks>
public sealed class DrawTally
{
    private readonly ILogger _props;

    /// <summary>Creates a tally that reports through one logger.</summary>
    /// <param name="props">Where the line goes, under <c>props</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="props"/> is null.</exception>
    public DrawTally(ILogger props)
    {
        ArgumentNullException.ThrowIfNull(props);

        _props = props;
    }

    private int _askedFor;
    private int _notStudio;
    private int _noBatches;
    private int _drawn;

    private readonly Dictionary<string, int> _noBatchesBy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _notStudioBy = new(StringComparer.Ordinal);

    /// <summary>Starts a frame's count.</summary>
    /// <param name="askedFor">How many props the scene offered.</param>
    public void Begin(int askedFor)
    {
        _askedFor = askedFor;
        _notStudio = 0;
        _noBatches = 0;
        _drawn = 0;

        _noBatchesBy.Clear();
        _notStudioBy.Clear();
    }

    /// <summary>Records a prop whose model kind this renderer cannot draw.</summary>
    /// <param name="prop">The prop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **Inline BSP submodels collapse to one entry.** A map's doors and moving brushes are
    /// <c>*1</c>, <c>*2</c>, … and cp_process names 141 of them, which turns the line into a wall
    /// that hides the entry that matters. They are one gap, not 141 findings.
    /// </remarks>
    public void NotDrawable(SceneProp prop)
    {
        _notStudio++;

        string name;

        if (prop.ModelPath.Length == 0)
        {
            name = "<no model>";
        }
        else if (prop.ModelPath.StartsWith('*'))
        {
            name = "<inline submodel>";
        }
        else
        {
            name = System.IO.Path.GetFileName(prop.ModelPath);
        }

        string rejected = $"{name}#{prop.Kind}";

        _notStudioBy[rejected] = _notStudioBy.GetValueOrDefault(rejected) + 1;
    }

    /// <summary>Records a prop whose model produced no geometry.</summary>
    /// <param name="modelPath">Which model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelPath"/> is null.</exception>
    /// <remarks>
    /// Named per model, because "no batches" for ONE model is a load failure and for all of them is
    /// a frame-selection failure, and the two need different fixes.
    /// </remarks>
    public void NoGeometry(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        _noBatches++;

        string name = System.IO.Path.GetFileName(modelPath);

        _noBatchesBy[name] = _noBatchesBy.GetValueOrDefault(name) + 1;
    }

    /// <summary>Records a prop that will be drawn.</summary>
    public void Drawn() => _drawn++;

    private (int AskedFor, int Drawn, int NotStudio, int NoBatches) _last = (-1, -1, -1, -1);
    private long _reportedAt;

    /// <summary>Reports the frame's counts, when they have changed and not too often.</summary>
    /// <remarks>
    /// **"Only when they change" was not enough, and the log proved it.** Measured 2026-08-24: this
    /// line printed 13,566 times in two minutes of playback, because the counts ALTERNATE between
    /// two shapes as props enter and leave view — 280/272 one frame, 272/272 the next — so every
    /// frame is a change and the guard never fires.
    ///
    /// A change guard against a value that oscillates is not a guard. Paired with a rate limit,
    /// which is the part that bounds it: at most one line a second, and still only on a change, so a
    /// steady state stays silent and a genuine shift is reported within a second of happening.
    /// </remarks>
    public void Report()
    {
        (int, int, int, int) state = (_askedFor, _drawn, _notStudio, _noBatches);

        long now = Stopwatch.GetTimestamp();

        if (state == _last || now - _reportedAt < Stopwatch.Frequency)
        {
            return;
        }

        _last = state;
        _reportedAt = now;

        // Debug: written from the draw loop, once a second at most but still during play, and every
        // line is a disk flush (B191). The change guard above limits how OFTEN, never whether a
        // production run pays at all.
        _props.LogDebug(
            "{Message}",
            $"asked for {_askedFor}, produced {_drawn}; " +
            $"skipped {_notStudio} not-studio [{Named(_notStudioBy)}], " +
            $"{_noBatches} no-batches [{Named(_noBatchesBy)}]");
    }

    private static string Named(Dictionary<string, int> by) =>
        by.Count == 0 ? "none" : string.Join(", ", by.Select(entry => $"{entry.Value}x{entry.Key}"));
}
