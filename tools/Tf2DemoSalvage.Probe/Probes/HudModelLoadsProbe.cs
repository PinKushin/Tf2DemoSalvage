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
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every model the HUD's panels ask to load over a demo's playback, against the level's preload list (B779, B812).</summary>
/// <remarks>
/// **The output-level check for "a model only a panel draws is packed through the world's set".** The set's loader is the
/// level's preload (`MapAssets.Geometry`, a lookup over `DemoModels.Needed`), so a panel model missing from that list never
/// loads. This runs the production HUD (`VguiHud`, over `GameContent.HudArchives` with no HUD chosen — the stock files, as
/// the preload reads them) over the demo — the state `HudStates.For` reads at each sampled tick and the events the viewer's
/// feed delivers — with a model cache that records each `LoadMdl` and answers only what `DemoModels.Needed(timeline, game)`
/// lists, as production's loader would. It reports the paths asked, and those the preload does not hold.
///
/// **"Held" means LISTED, not loaded**: the cache answers from the list alone and reads no file, so a listed path whose
/// `.mdl` is absent or unreadable still counts as held here. Whether the listed models load is the level load's own check.
///
/// **Controls, asserted (the run throws when one fails)**:
/// <list type="bullet">
/// <item>the class panel's model is asked and held on any demo whose local player has a class — else the HUD never painted
/// a model panel and the census measured nothing;</item>
/// <item>a path no list names is answered missing — else the cache answers everything and "held" means nothing;</item>
/// <item>`DemoModels.ToPack` holds every path `Needed` holds that the HUD asked — B195's two lists agree on the panels.</item>
/// </list>
///
/// **Layout census**: every `.res` the HUD read during the run is walked for model blocks, and any file with one outside
/// `HudPanelModels.Layouts` fails the run — the list of layouts is measured each run rather than trusted. It also times the
/// level's two lists in production order (the first walks, the second reuses the walk held for the timeline), then warm
/// medians of three of each list and of the carried-item walk alone (`DemoModels.AddCarriedItemModels`).
/// <code>
///   hud-model-loads "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/tf2-2026-pub-pov-clean.dem" [stride]
/// </code>
/// </remarks>
public sealed class HudModelLoadsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "hud-model-loads";

    /// <inheritdoc/>
    public string Summary => "every model the HUD's panels ask to load over a demo, against the level's preload: hud-model-loads <demo> [stride]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("usage: hud-model-loads <demo> [stride]");
            return;
        }

        int stride = arguments.Count >= 2 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 33;
        string? folder = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        game.ChosenHud = null;
        GameArchives archives = game.HudArchives;
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(arguments[0]));

        // The level's lists, timed in the production order (review 21): the first `Needed` walks the carried items and holds the
        // walk for this timeline, and `ToPack` reuses it. Then, warm, the median of three of each list (the walk held) and of
        // the walk alone (`AddCarriedItemModels`, unheld), after one discarded warm-up run of each.
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        HashSet<string> needed = DemoModels.Needed(timeline, game);
        double neededMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        HashSet<string> toPack = DemoModels.ToPack(timeline, game);
        double toPackMs = clock.Elapsed.TotalMilliseconds;
        HashSet<string> carried = new(StringComparer.OrdinalIgnoreCase);

        double Median(Action run)
        {
            run();
            double[] runs = new double[3];

            for (int at = 0; at < runs.Length; at++)
            {
                clock.Restart();
                run();
                runs[at] = clock.Elapsed.TotalMilliseconds;
            }

            Array.Sort(runs);
            return runs[1];
        }

        double neededWarm = Median(() => DemoModels.Needed(timeline, game));
        double toPackWarm = Median(() => DemoModels.ToPack(timeline, game));
        double carriedMs = game.Weapons.Items is { } walkSchema
            ? Median(() =>
            {
                carried.Clear();
                DemoModels.AddCarriedItemModels(timeline.Frames.SelectMany(static frame => frame.Players), walkSchema, carried);
            })
            : 0d;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"first Needed {neededMs:F1} ms ({needed.Count} models, walks), then ToPack {toPackMs:F1} ms ({toPack.Count}, reuses the walk); " +
            $"warm medians of 3: Needed {neededWarm:F1} ms, ToPack {toPackWarm:F1} ms, the carried-item walk alone {carriedMs:F1} ms " +
            $"({carried.Count} paths); {timeline.Frames.Count} frames"));

        AskedCache cache = new(needed);
        ItemSchema? items = game.Weapons.Items;
        AttributeHooks? hooks = items is null ? null : new AttributeHooks(items);
        HashSet<string> resRead = new(StringComparer.OrdinalIgnoreCase);
        byte[]? Read(string path)
        {
            if (path.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
            {
                resRead.Add(path);
            }

            return archives.Read(path);
        }

        TfWeaponData scripts = new(archives.Read);
        VguiSurfaceHost host = new(Read, archives.FullPathOnDisk, new NoFonts(), _ => (64, 64));
        VguiHud hud = new(host, cache);

        hud.Viewport.Scripts = scripts;
        hud.Viewport.ClassModels = game.Classes;
        hud.Viewport.Items = items;
        hud.Viewport.Weapons = game.Weapons;
        hud.Viewport.PaintKits = archives.Read(PaintKitDefinitions.FilePath) is { } kits ? PaintKitDefinitions.Read(kits) : null;

        if (hooks is not null)
        {
            hud.Viewport.PlayerAttribute = hooks.OnPlayer;
            hud.Viewport.WeaponAttribute = hooks.OnItem;
        }

        HudEventFeed feed = new();
        string map = Path.GetFileNameWithoutExtension(arguments[0]);
        int frames = 0;
        int local = 0;
        int withClass = 0;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += stride)
        {
            (bool reset, IReadOnlyList<SceneGameEvent> crossed, IReadOnlyList<SceneUserMessage> messages) = feed.Advance(timeline, tick);
            HudState state = HudStates.For(timeline, tick, scripts, hooks);

            // The watcher's ConVars at their engine defaults, as a fresh config has them (`cl_hud_playerclass_use_playermodel`
            // "1" draws the class panel's model).
            state = state with { ConVars = state.ConVars with { Client = WatcherDefault } };

            host.BeginFrame(1920, 1080);
            hud.Frame(state, HudEventFeed.Resolve(timeline, crossed, map, hooks), reset, messages);
            frames++;
            local += state.HasLocalPlayer ? 1 : 0;
            withClass += state.HasLocalPlayer && state.Player(state.LocalIndex) is { PlayerClass: > 0 } ? 1 : 0;
        }

        TfPlayerModelPanel classPanel = hud.PlayerStatus.PlayerClass.PlayerModelPanel;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"frames with a local player: {local}, of them with a class: {withClass}; class panel: class {classPanel.CurrentClassIndex}, model '{classPanel.ModelName}', " +
            $"shown {classPanel.Visible}, class element shown {hud.PlayerStatus.PlayerClass.Visible}; recorder {timeline.RecorderEntityIndex}"));
        output.WriteLine($"HUD layout models (HudPanelModels): {string.Join(", ", HudPanelModels.Named(archives.Read))}");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{frames} HUD frames, every {stride} ticks; preload list {needed.Count} models; asked {cache.Asked.Count} distinct paths, " +
            $"{cache.Asked.Keys.Count(path => !needed.Contains(path))} not listed in the preload (held = listed, not loaded)"));

        // The layout census: every `.res` the HUD read, walked for model blocks.
        List<string> withModels = [.. resRead.Order(StringComparer.OrdinalIgnoreCase).Where(res => HudPanelModels.Named(archives.Read, [res]).Count > 0)];
        List<string> outside = [.. withModels.Where(res => !HudPanelModels.Layouts.Contains(res, StringComparer.OrdinalIgnoreCase))];
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"layout census: {resRead.Count} .res files read, {withModels.Count} with model blocks: {string.Join(", ", withModels)}; outside Layouts: {outside.Count}"));

        // The controls.
        List<string> failed = [];

        if (outside.Count > 0)
        {
            failed.Add($"layouts with model blocks outside HudPanelModels.Layouts: {string.Join(", ", outside)}");
        }

        if (withClass > 0 && !cache.Asked.Keys.Any(path => path.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) && needed.Contains(path)))
        {
            failed.Add("the local player had a class, and no class model was asked and held: the HUD painted no model panel");
        }

        const string Unlisted = "models/probe/never_listed.mdl";
        bool unlistedAnswered = cache.LoadMdl(Unlisted) is not null;
        cache.Asked.Remove(Unlisted);
        cache.FirstAsker.Remove(Unlisted);

        if (unlistedAnswered || needed.Contains(Unlisted))
        {
            failed.Add("an unlisted path was answered: the cache does not discriminate");
        }

        List<string> heldNotPacked = [.. cache.Asked.Keys.Where(path => needed.Contains(path) && !toPack.Contains(path))];

        if (heldNotPacked.Count > 0)
        {
            failed.Add($"asked paths Needed lists and ToPack does not (B195): {string.Join(", ", heldNotPacked)}");
        }

        string classControl = withClass > 0 ? "asked and held" : "NOT APPLICABLE (no frame's local player has a class)";
        output.WriteLine(failed.Count == 0
            ? $"controls: class model {classControl}, unlisted path missing, ToPack holds every listed ask, no layout outside Layouts"
            : "CONTROLS FAILED: " + string.Join("; ", failed));

        foreach ((string path, int asks) in cache.Asked)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(needed.Contains(path) ? "held   " : "MISSING")} {asks,6} asks  {path}"));

            if (needed.Contains(path))
            {
                continue;
            }

            // What the preload holds under a like name, and who asked: the lead for why the list missed it.
            string stem = Path.GetFileNameWithoutExtension(path).Replace("c_", string.Empty, StringComparison.Ordinal);

            foreach (string near in needed.Where(held => held.Contains(stem, StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine($"           preload has {near}");
            }

            output.WriteLine($"           first asked by {cache.FirstAsker.GetValueOrDefault(path) ?? "?"}");

            // The carried items whose panel model would be this path, as the preload's walk resolves them.
            if (items is null)
            {
                continue;
            }

            HashSet<(int, ulong)> reported = [];

            foreach (ScenePlayer player in timeline.Frames.SelectMany(frame => frame.Players))
            {
                foreach (SceneItem item in (player.Items ?? []).Concat(player.DisguiseWeaponItem is { } disguise ? [disguise] : []))
                {
                    if (item.DefinitionIndex is not { } definition || !reported.Add((definition, item.ItemId)))
                    {
                        continue;
                    }

                    TfItemView? view = TfItemView.From(item, items);
                    string? spy = view is null ? null : TfPlayerModelPanel.DisplayModel(items, view, 8, player.Team ?? 0);

                    if (spy?.Contains(stem, StringComparison.OrdinalIgnoreCase) == true || items.PlayerDisplayModel(definition, 8, 0, 0)?.Contains(stem, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        output.WriteLine($"           carried def {definition} item {item.ItemId} view {(view is null ? "none" : "made")} panel model '{spy}'");
                    }
                }
            }
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException("hud-model-loads: " + string.Join("; ", failed));
        }
    }

    /// <summary>A ConVar's engine default, or null for one not declared.</summary>
    private static string? WatcherDefault(string name) =>
        Tf2DemoSalvage.Core.Net.EngineConVars.TryByName(name, out Tf2DemoSalvage.Core.Net.EngineConVar? declared) ? declared.Default : null;

    /// <summary>The level's loader as production has it: answers only the preload's paths; records every ask.</summary>
    private sealed class AskedCache(HashSet<string> needed) : IMdlCache
    {
        private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

        private static readonly PropModels.ModelFrames Frames = new(
            [], new Dictionary<int, (int, int, float)>(), [], [], Skinned: OneBone());

        private readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);

        public SortedDictionary<string, int> Asked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PropModels.ModelFrames? FindMdl(string path) => _loaded.Contains(path) ? Frames : null;

        /// <summary>The first frames of the stack that asked for each path, for a miss's lead.</summary>
        public Dictionary<string, string> FirstAsker { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PropModels.ModelFrames? LoadMdl(string path)
        {
            Asked[path] = Asked.GetValueOrDefault(path) + 1;

            if (!needed.Contains(path) && !FirstAsker.ContainsKey(path))
            {
                FirstAsker[path] = string.Join(" < ", new System.Diagnostics.StackTrace().GetFrames().Skip(1).Take(5)
                    .Select(frame => frame.GetMethod() is { } method ? $"{method.DeclaringType?.Name}.{method.Name}" : "?"));
            }

            if (needed.Contains(path))
            {
                _loaded.Add(path);
            }

            return FindMdl(path);
        }

        public int FindBodygroup(string modelPath, string group) => -1;

        public int SetBodygroup(string modelPath, int group, int value, int body) => body;

        private static PropModels.SkinnedModel OneBone()
        {
            List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
                [(0, [new StudioSequence(Animation: 0, Flags: 0, Label: "idle", Blend: null, Activity: "idle", ActivityWeight: 1)])];

            return new PropModels.SkinnedModel(
                Bones:
                [
                    new StudioBone(
                        Name: "root",
                        Parent: -1,
                        Position: (0f, 0f, 0f),
                        Rotation: (0f, 0f, 0f, 1f),
                        PoseToBone: Identity,
                        Flags: ~0),
                ],
                Models: [[]],
                Sequences: StudioSequenceTable.Merge(groups),
                Groups: groups,
                PoseParameters: [],
                MasterPose: []);
        }
    }
}
