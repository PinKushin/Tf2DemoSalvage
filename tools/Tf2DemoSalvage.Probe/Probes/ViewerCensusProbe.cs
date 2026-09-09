using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a whole demo asks the viewer to draw, and how much of it never reaches the screen.
/// </summary>
/// <remarks>
/// **The owner's question, and it is a denominator question:** *"we should do a parity audit to see
/// what we are still missing in the viewer too, since while i cant think of anything offhand, i also
/// have not watched a bunch of demos all the way through either to see if there are missing meshes or
/// materials or textures, or something isnt being draw"*.
///
/// **A feature list cannot answer it.** `docs/memory/the-denominator-decides-what-can-be-lost.md`: a
/// hand-kept list of what we implement drifts, and it cannot miss what it omits. A census of what the
/// RECORDING asks for can — every model reference, every entity class, counted against how many
/// reached the draw list.
///
/// <code>
///   viewer-census 20130518_0313_cp_granary_blu_blu
///   viewer-census cp_process_f12 200
/// </code>
///
/// **Every number is carried from the draw loop, not recomputed** (B243). `DrawTally` counts them
/// where the decision is made, and this reads its cumulative totals — a second walk would be
/// measuring itself, which is how five probes gave confident wrong answers in one session.
///
/// **Three populations, and they need separate answers:**
///
/// - **A class on the wire that never becomes a prop at all** — the scene never offered it, so no
///   renderer report mentions it. This is the gap a feature list hides.
/// - **A prop offered and rejected** — `NotDrawable` for a kind with no path, `NoGeometry` for a model
///   that loaded nothing. Both are ours.
/// - **A prop deliberately not drawn** — `kRenderNone` and the frustum cull. Both are the engine
///   agreeing, and folding them into the above would bury a real gap under the ordinary case.
/// </remarks>
public sealed class ViewerCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "viewer-census";

    /// <inheritdoc/>
    public string Summary =>
        "what a whole demo asks the viewer to draw and what never reaches it: viewer-census <demo> [everyN]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("viewer-census <demo> [everyN]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        // **Sampled rather than every tick, because a hundred thousand frames of pose work is
        // minutes.** The population being censused is which MODELS and CLASSES appear, and those
        // change over seconds rather than ticks — so a stride of a few ticks sees the same set. The
        // stride is printed, because a census that does not say what it sampled is a rate nobody can
        // reproduce.
        int stride = arguments.Count > 1 &&
            int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int every)
                ? Math.Max(1, every)
                : 50;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {timeline.Frames.Count:N0} frames, " +
            $"{timeline.Props.Count:N0} prop tracks, {timeline.PlayerTracks.Count:N0} player tracks, " +
            $"sampling every {stride}"));

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } folder)
        {
            output.WriteLine("No TF2 install, so nothing can be resolved. This is not a finding.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        IPlayerAppearance appearance = DemoAppearance.Ensure(
            DemoAppearance.None, timeline, game, NullLogger.Instance);

        // **The MAP has to be loaded, because geometry comes from its asset set.** A census run without
        // it would report every model as having no geometry — a confident, entirely false answer, and
        // the same shape as the instrument faults this project keeps a casebook of.
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(File.ReadAllBytes(path)).MapName;

        if (mapName.Length == 0 ||
            new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .Find(mapName) is not { } mapPath)
        {
            output.WriteLine("  The demo's map was not found, so geometry cannot resolve. Not a finding.");
            return;
        }

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("  The map loaded no asset set, so geometry cannot resolve. Not a finding.");
            return;
        }

        EntityModelSet models = new() { Geometry = assets.Geometry };

        List<SceneProp> drawn = [];
        List<ModelInstance> instances = [];

        int sampled = 0;

        for (int index = 0; index < timeline.Frames.Count; index += stride)
        {
            int tick = timeline.Frames[index].Tick;

            drawn.Clear();
            instances.Clear();

            timeline.PropsAt(tick, drawn);
            PlayerProps.Add(timeline.PlayersAt(tick), drawn, appearance, models);

            models.Add(drawn, assets.Geometry);
            models.UpdateClientSideAnimations(drawn);
            models.Instances(drawn, instances, seconds: tick * timeline.IntervalPerTick);

            sampled++;
        }

        (long askedFor, long shown, long culled, long notDrawn) = models.Tally.Totals;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {sampled:N0} frames sampled: {askedFor:N0} props offered, {shown:N0} drawn, " +
            $"{culled:N0} off screen, {notDrawn:N0} kRenderNone"));

        // **The two OURS categories, named per model.** Everything above is a total; these are the
        // lists somebody has to act on, and a total cannot be acted on.
        Report(output, "NO GEOMETRY", models.Tally.EverNoGeometry);
        Report(output, "NOT DRAWABLE", models.Tally.EverNotStudio);

        // **The class census, which is the gap a feature list hides.** A class whose entities carry a
        // model and never become a prop is invisible to every renderer report, because the renderer
        // was never offered one.
        Dictionary<string, (int Tracks, int WithModel)> byClass = new(StringComparer.Ordinal);

        foreach (ScenePropTrack track in timeline.Props)
        {
            string owner = track.ClassName ?? "<unnamed>";

            (int tracks, int withModel) = byClass.GetValueOrDefault(owner);

            byClass[owner] = (tracks + 1, withModel + (track.ModelPath.Length > 0 ? 1 : 0));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {byClass.Count:N0} entity classes carry a prop track"));

        foreach (KeyValuePair<string, (int Tracks, int WithModel)> entry in byClass
            .Where(pair => pair.Value.WithModel == 0)
            .OrderByDescending(pair => pair.Value.Tracks)
            .Take(12))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    NO MODEL {entry.Value.Tracks,5}x {entry.Key}"));
        }
    }

    /// <summary>Reports one failure bucket, commonest first, with what it did not cover.</summary>
    /// <remarks>
    /// **Says how many were NOT listed rather than truncating silently.** A census that shows a top
    /// twelve and stops reads as complete; `docs/memory/the-denominator-decides-what-can-be-lost.md`
    /// is about exactly that, and the rule from the workflow guidance applies here too — if a report
    /// bounds its coverage it has to say what it dropped.
    /// </remarks>
    private static void Report(
        TextWriter output, string label, IReadOnlyDictionary<string, int> bucket)
    {
        if (bucket.Count == 0)
        {
            output.WriteLine($"  {label}: none");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"  {label}: {bucket.Count:N0} distinct"));

        int shown = 0;

        foreach ((string name, int count) in bucket.OrderByDescending(pair => pair.Value).Take(12))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {count,7}x {name}"));

            shown++;
        }

        if (bucket.Count > shown)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    … and {bucket.Count - shown:N0} more not listed"));
        }
    }
}
