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
/// loads. This runs the production HUD (`VguiHud`, from the install, stock files) over the demo — the state
/// `HudStates.For` reads at each sampled tick and the events the viewer's feed delivers — with a model cache that records
/// each `LoadMdl` and answers only what `DemoModels.Needed(timeline, game)` lists, as production's loader would. It reports
/// the paths asked, and those the preload does not hold.
///
/// **The control**: the class panel's model must be asked and held on a POV demo with a local player — if no class model
/// is asked, the HUD never painted a model panel and the census measured nothing.
/// <code>
///   hud-model-loads "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/auto-20260817-1720-cp_process_f12.dem" [stride]
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
        GameArchives archives = game.Archives.WithoutCustom();
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(arguments[0]));
        HashSet<string> needed = DemoModels.Needed(timeline, game);
        AskedCache cache = new(needed);
        ItemSchema? items = game.Weapons.Items;
        AttributeHooks? hooks = items is null ? null : new AttributeHooks(items);
        TfWeaponData scripts = new(archives.Read);
        VguiSurfaceHost host = new(archives.Read, archives.FullPathOnDisk, new NoFonts(), _ => (64, 64));
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
        output.WriteLine($"HUD layout models (HudPanelModels): {string.Join(", ", HudPanelModels.Named(game.Archives.Read))}");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{frames} HUD frames, every {stride} ticks; preload list {needed.Count} models; asked {cache.Asked.Count} distinct paths, " +
            $"{cache.Asked.Keys.Count(path => !needed.Contains(path))} not in the preload"));

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
