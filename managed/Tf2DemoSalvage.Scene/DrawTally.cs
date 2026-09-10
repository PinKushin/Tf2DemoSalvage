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

    private string _pass = "world";
    private int _askedFor;
    private int _notStudio;
    private int _culled;
    private int _noBatches;
    private int _drawn;
    private int _notDrawn;

    private readonly Dictionary<string, int> _noBatchesBy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _notStudioBy = new(StringComparer.Ordinal);

    /// <summary>Everything this tally has ever counted, which the per-frame fields cannot answer.</summary>
    /// <remarks>
    /// **The per-frame counters are cleared by every `Begin`, so nothing could ask what a whole
    /// recording lost.** The log line reports a frame, rate-limited and only on a change, which is
    /// right for watching and useless for an audit: a model that failed to load in one frame out of
    /// a hundred thousand never appears in a line anybody reads.
    ///
    /// **Carried from the same calls that count the frame** (B243), not a second route. A census that
    /// re-derived these by walking the demo again would be measuring its own walk, which is how five
    /// instruments gave confident wrong answers in one session.
    /// </remarks>
    private readonly Dictionary<string, int> _everNoGeometry = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _everNotStudio = new(StringComparer.Ordinal);

    /// <summary>One entity index per undrawable bucket, so a census can be followed (B379).</summary>
    private readonly Dictionary<string, int> _firstNotDrawable = new(StringComparer.Ordinal);

    /// <summary>An entity that landed in each undrawable bucket, by the same key.</summary>
    public IReadOnlyDictionary<string, int> FirstNotDrawable => _firstNotDrawable;

    private long _everAskedFor;
    private long _everDrawn;
    private long _everCulled;
    private long _everNotDrawn;

    /// <summary>Cumulative totals over every frame this tally has seen.</summary>
    /// <remarks>
    /// **For an audit rather than for the log.** The owner's question is what a whole demo fails to
    /// draw — *"missing meshes or materials or textures, or something isnt being draw"* — and that is
    /// a question about totals, which the frame counters and the rate-limited line both erase.
    /// </remarks>
    public (long AskedFor, long Drawn, long Culled, long NotDrawn) Totals =>
        (_everAskedFor, _everDrawn, _everCulled, _everNotDrawn);

    /// <summary>Every model that ever failed to produce geometry, with how many times.</summary>
    public IReadOnlyDictionary<string, int> EverNoGeometry => _everNoGeometry;

    /// <summary>Every model ever rejected as not being a studio model, with how many times.</summary>
    public IReadOnlyDictionary<string, int> EverNotStudio => _everNotStudio;

    /// <summary>Starts a frame's count.</summary>
    /// <param name="askedFor">How many props the scene offered.</param>
    /// <param name="pass">Which pass is counting — <c>world</c> or <c>viewmodel</c>.</param>
    /// <remarks>
    /// **The pass is named because one tally serves two of them and the line could not say
    /// which.** `Instances` runs twice a frame against the same `EntityModelSet`: hundreds of
    /// world props with a real frustum, then two or three viewmodel props with none. Both call
    /// `Begin` and `Report`, the rate limit lets at most one line a second through, and which
    /// pass won that race was invisible — so somebody investigating the frustum cull could read
    /// `0 off-screen` off the viewmodel pass, which never had a frustum to cull against.
    /// </remarks>
    public void Begin(int askedFor, string pass = "world")
    {
        _pass = pass;
        _askedFor = askedFor;
        _everAskedFor += askedFor;
        _notStudio = 0;
        _culled = 0;
        _noBatches = 0;
        _drawn = 0;
        _notDrawn = 0;

        _noBatchesBy.Clear();
        _notStudioBy.Clear();
    }

    /// <summary>Records a prop the view frustum rejected before it was posed.</summary>
    /// <remarks>
    /// **Takes no prop, unlike its neighbours.** They group their rejections by model name because
    /// each names a gap somebody has to go and close; this one is the map working, and a list of
    /// which crates were off screen this frame is noise.
    ///
    /// **Counted apart from every other reason, because it is the only one that is not a gap.** A
    /// prop off screen is the map working: `CollateRenderablesInLeaf` rejects it too, and a viewer
    /// that drew it would be the one diverging. Reported so the number can be READ — a cull that
    /// suddenly rejects everything looks exactly like a rendering failure, and the count is what
    /// separates them (B254).
    /// </remarks>
    public void Culled()
    {
        _culled++;
        _everCulled++;
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
        ArgumentNullException.ThrowIfNull(prop);

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

        // **The KIND is kept in the cumulative map too, because it is the whole diagnosis.** A prop
        // rejected as `#Unknown` is a model reference this project does not classify; one rejected as
        // a kind it knows is a renderer that has no path for that kind. Losing the kind here would
        // turn two different gaps into one number.
        // **The CLASS, for the population with no model at all** (B379). Twelve props a frame are
        // rejected here naming no model, and `<no model>#Studio` is one bucket holding all of them —
        // so the census could say how many and never which, and the entry says so: *"what is NOT
        // established: which entities they are… it needs the entity index and class carried to the
        // rejection."* A model path cannot name a prop that has no model path; the class can, and it
        // is the only field that survives the thing being missing.
        string ever = prop.ModelPath.Length == 0
            ? $"<no model>#{prop.Kind}#{ClassOf(prop.ClassName)}"
            : $"{prop.ModelPath}#{prop.Kind}";

        _everNotStudio[ever] = _everNotStudio.GetValueOrDefault(ever) + 1;

        // **One example index per bucket, kept rather than counted** (B243). A count says a
        // population exists and cannot be followed; an index is what a probe takes. The FIRST is kept
        // rather than the last, so re-running lands on the same entity and a second measurement is
        // about the same subject as the first.
        _firstNotDrawable.TryAdd(ever, prop.EntityIndex);
    }

    /// <summary>An entity's class, or a placeholder when the demo has not named one.</summary>
    /// <param name="className">What the track carries, which may be absent.</param>
    /// <returns>A name safe to use as part of a key.</returns>
    /// <remarks>
    /// **A missing class is itself a finding, so it gets a name rather than being folded in.** An
    /// entity with neither a model nor a class is a track built from something other than a server
    /// class, and that is a different gap from a known class this renderer has no path for.
    /// </remarks>
    private static string ClassOf(string? className) =>
        string.IsNullOrEmpty(className) ? "<no class>" : className;

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

        // The full path in the cumulative map, because an audit has to go and find the file — where
        // the per-frame map keeps only the leaf name, which is what a log line has room for.
        _everNoGeometry[modelPath] = _everNoGeometry.GetValueOrDefault(modelPath) + 1;
    }

    /// <summary>Records a prop the entity itself asks not to be drawn.</summary>
    /// <remarks>
    /// **`kRenderNone`, and it is counted apart from every other reason on purpose.** A prop with no
    /// geometry is a load failure and a prop of an undrawable KIND is a gap in this renderer; this
    /// one is the map working as intended — 118 entities in a real match, eighteen `func_door`s on
    /// `cp_fulgur` alone. Folding it into "not drawable" would bury a real gap under the ordinary
    /// case, which is the mistake `NotDrawable`'s own note is about.
    ///
    /// These entities stay in the SCENE. Their children hang off their transforms
    /// (`CalcAbsolutePosition`, `c_baseentity.cpp:4350`), which is why they cannot simply be
    /// dropped upstream — see `EntityState.IsDrawn`.
    /// </remarks>
    public void NotDrawn()
    {
        _notDrawn++;
        _everNotDrawn++;
    }

    /// <summary>Records a prop that will be drawn.</summary>
    public void Drawn()
    {
        _drawn++;
        _everDrawn++;
    }

    /// <summary>The last state reported for each pass, so an unchanged one stays silent.</summary>
    /// <remarks>
    /// **Keyed by pass, because one tally serves two of them.** Sharing one slot made the two
    /// passes' different numbers look like a single oscillating pass — the exact shape the change
    /// guard cannot guard against, which is what the rate limit beside it exists for.
    /// </remarks>
    private readonly Dictionary<string, (int AskedFor, int Drawn, int NotStudio, int NoBatches,
        int NotDrawn, int Culled)> _seen = new(StringComparer.Ordinal);

    /// <summary>When each pass last reported, for the rate limit.</summary>
    private readonly Dictionary<string, long> _reportedAt = new(StringComparer.Ordinal);

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
        (int, int, int, int, int, int) state =
            (_askedFor, _drawn, _notStudio, _noBatches, _notDrawn, _culled);

        long now = Stopwatch.GetTimestamp();

        // **Both guards are PER PASS, and a shared rate limit did not merely blur the two — it
        // silenced one.** `Instances` runs twice a frame against one tally, so with a single
        // `_reportedAt` the world pass reported and the viewmodel pass, arriving microseconds
        // later, was inside the window and dropped; then the same again the next second. The
        // viewmodel pass could never report at all, which is worse than the ambiguity this label
        // was added to fix. Found by the test, not by reading: the label went in first and the
        // second half of `Report_TheTwoPasses_AreNamedAndDoNotSuppressEachOther` went red.
        _seen.TryGetValue(_pass, out (int, int, int, int, int, int) last);
        _reportedAt.TryGetValue(_pass, out long lastAt);

        if (state == last || now - lastAt < Stopwatch.Frequency)
        {
            return;
        }

        _seen[_pass] = state;
        _reportedAt[_pass] = now;

        // Debug: written from the draw loop, once a second at most but still during play, and every
        // line is a disk flush (B191). The change guard above limits how OFTEN, never whether a
        // production run pays at all.
        _props.LogDebug(
            "{Message}",
            $"{_pass} pass: asked for {_askedFor}, produced {_drawn}; " +
            $"skipped {_notStudio} not-studio [{Named(_notStudioBy)}], " +
            $"{_noBatches} no-batches [{Named(_noBatchesBy)}], " +

            // Counted and REPORTED apart from the two failures beside it: this one is the map
            // working as intended, and a number that never moves is how a reader tells the
            // difference between "we cannot draw it" and "it asked not to be drawn".
            $"{_notDrawn} kRenderNone, " +

            // The engine's own rejection rather than a gap, and the one number here that SHOULD be
            // large: everything off screen (B254).
            $"{_culled} off-screen");
    }

    private static string Named(Dictionary<string, int> by) =>
        by.Count == 0 ? "none" : string.Join(", ", by.Select(entry => $"{entry.Value}x{entry.Key}"));
}
