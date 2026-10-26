using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What the per-activity tuple lists cost over a demo's loaded skinned models, and what a pick costs (D233).</summary>
/// <remarks>
/// **Retained bytes are measured, not added up**: `GC.GetTotalMemory(true)` around building every model's map on a fresh
/// table, minus the same around building only the fresh tables (the control), against a heaviest-only
/// `Dictionary&lt;string, int&gt;` of the same names (what B584 held). The timings are a Stopwatch over the production
/// calls on the demo's own player models, Release.
/// </remarks>
public sealed class ActivityTuplesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "activity-tuples";

    /// <inheritdoc/>
    public string Summary => "per-activity tuple memory and pick cost over a demo's skinned models: activity-tuples <demo> [tick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(file).MapName;

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);
        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        new WeaponPropModels().Resolve(props, players, game.Weapons.For, game.Weapons.WorldDisplayModel);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        List<(string Path, PropModels.SkinnedModel Model)> models =
        [
            // The props at the tick and the nine class models: players are not props on the timeline, but they are the
            // models this map is asked of every frame.
            .. props.Select(prop => prop.ModelPath).Where(model => model.Length > 0)
                .Concat(ClassModels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(model => (model, assets.Geometry(model)?.Skinned))
                .Where(pair => pair.Skinned is not null)
                .Select(pair => (pair.model, pair.Skinned!)),
        ];

        int sequences = 0;
        int candidates = 0;
        int activities = 0;
        int multi = 0;

        foreach ((string Path, PropModels.SkinnedModel Model) m in models)
        {
            PropModels.SkinnedModel model = m.Model;
            sequences += model.Sequences.Count;
            List<string> named = [.. Enumerable.Range(0, model.Sequences.Count).Select(model.ActivityOf).Where(a => a.Length > 0)];
            candidates += named.Count;
            activities += named.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            foreach (IGrouping<string, string> group in named.GroupBy(a => a, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                multi++;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {Path.GetFileName(m.Path)} {group.Key} x{group.Count()}"));
            }
        }

        // The control: fresh tables alone. Then the same fresh tables with their maps built.
        long tablesOnly = Retained(() => models.Select(m => (object)(m.Model with { Sequences = StudioSequenceTable.Merge(m.Model.Groups) })).ToList());
        long withMaps = Retained(() => models.Select(m =>
        {
            PropModels.SkinnedModel fresh = m.Model with { Sequences = StudioSequenceTable.Merge(m.Model.Groups) };
            fresh.ForActivity("ACT_MP_STAND_PRIMARY");
            return (object)fresh;
        }).ToList());
        long heaviestOnly = Retained(() => models.Select(m =>
        {
            Dictionary<string, int> names = new(StringComparer.OrdinalIgnoreCase);

            for (int s = 0; s < m.Model.Sequences.Count; s++)
            {
                if (m.Model.ActivityOf(s) is { Length: > 0 } a)
                {
                    names.TryAdd(a, s);
                }
            }

            return (object)names;
        }).ToList());

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} tick {tick}: {models.Count} skinned models, {sequences} sequences, {candidates} with an activity, " +
            $"{activities} activities, {multi} with more than one candidate"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"retained: tuple maps {withMaps - tablesOnly} B; heaviest-only map of the same names {heaviestOnly} B"));

        PropModels.SkinnedModel[] classes = [.. models.Where(m => m.Path.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) &&
            !m.Path.Contains("/items/", StringComparison.OrdinalIgnoreCase)).Select(m => m.Model)];

        if (classes.Length == 0)
        {
            output.WriteLine("no class model in view to time");
            return;
        }

        const string Activity = "ACT_MP_STAND_PRIMARY";
        const int Calls = 2_000_000;
        int sink = 0;

        foreach (PropModels.SkinnedModel model in classes)
        {
            sink += model.ForActivity(Activity) + model.SelectWeightedSequence(Activity, -1, 1u);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch watch = Stopwatch.StartNew();

        for (int call = 0; call < Calls; call++)
        {
            sink += classes[call % classes.Length].ForActivity(Activity);
        }

        double forActivity = watch.Elapsed.TotalNanoseconds / Calls;
        watch.Restart();

        for (int call = 0; call < Calls; call++)
        {
            sink += classes[call % classes.Length].SelectWeightedSequence(Activity, -1, (uint)call * 2654435761u);
        }

        double pick = watch.Elapsed.TotalNanoseconds / Calls;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{classes.Length} class models: ForActivity {forActivity:0.0} ns/call, SelectWeightedSequence {pick:0.0} ns/call, " +
            $"{allocated} B allocated over {2 * Calls} calls (sink {sink % 10})"));
    }

    // S1215 guards production code against forcing collections; a probe whose output IS the retained heap needs exactly
    // that collection, so the rule's premise does not hold here.
#pragma warning disable S1215
    private static readonly string[] ClassModels =
    [
        .. new[] { "scout", "sniper", "soldier", "demo", "medic", "heavy", "pyro", "spy", "engineer" }
            .Select(name => $"models/player/{name}.mdl"),
    ];

    private static long Retained(Func<object> build)
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        object kept = build();
        long after = GC.GetTotalMemory(forceFullCollection: true);
#pragma warning restore S1215
        GC.KeepAlive(kept);
        return after - before;
    }
}
