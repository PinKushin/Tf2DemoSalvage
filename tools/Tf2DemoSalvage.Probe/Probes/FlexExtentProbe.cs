using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How much of each face's flex stream a flex can reach and does reach, per model, over a stretch of a real demo (B597).
/// </summary>
/// <remarks>
/// **The question decides how a face is uploaded:** the stream is a model's whole buffer, six floats a slot, but only
/// the slots a flex names can be non-zero. A ranged upload pays the span from the first such slot to the last; a
/// compacted one pays the count. **Both read from the production path** (`MomentScene.Build` then `Pose`, as
/// `viewer-census` drives it): the static set is <see cref="EntityModelSet.FlexedSlotsOf"/>, the set the scene itself
/// writes through; "in motion" is the slots of each instance's <see cref="ModelInstance.Flex"/> holding a non-zero float
/// on that frame. A model whose face was never driven (a worn item, a resting face) has no static set and reports
/// motion only. **Control:** in-motion slots must lie inside the static set's span, which the probe checks.
/// <code>
///   flex-extent D:/measure-f12/f12.dem 30000 60
/// </code>
/// </remarks>
public sealed class FlexExtentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "flex-extent";

    /// <inheritdoc/>
    public string Summary =>
        "per face model, the slots a flex can and does move, count and first-to-last span: flex-extent <demo> <tick> <seconds> [every]";

    private sealed class Model
    {
        public int Length;
        public long Frames;
        public long Moved;
        public int MaxMoved;
        public long Span;
        public int First = int.MaxValue;
        public int Last = -1;
    }

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("flex-extent <demo> <tick> <seconds> [every]");
            return;
        }

        int start = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        double seconds = double.Parse(arguments[2], CultureInfo.InvariantCulture);

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(File.ReadAllBytes(path)).MapName;

        if (locator.FindGameFolder() is not { } folder || mapName.Length == 0 || locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine("The game or the demo's map was not found. Not a finding.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        IPlayerAppearance appearance = DemoAppearance.Ensure(DemoAppearance.None, timeline, game, NullLogger.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded no asset set. Not a finding.");
            return;
        }

        EntityModelSet models = new() { Geometry = assets.Geometry, IntervalPerTick = timeline.IntervalPerTick };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Appearance = appearance,
            Weapons = game.Weapons,
            Lighting = map.Lighting,
        };

        Dictionary<string, Model> seen = new(StringComparer.OrdinalIgnoreCase);
        List<SceneProp> drawn = [];
        List<ScenePlayer> players = [];
        int end = start + (int)Math.Round(seconds / timeline.IntervalPerTick);
        int frames = 0;
        long faces = 0;

        // A stride for a census of which models appear over a whole match; 1 (every frame) for the motion columns.
        int stride = arguments.Count > 3 ? Math.Max(1, int.Parse(arguments[3], CultureInfo.InvariantCulture)) : 1;

        for (int index = 0; index < timeline.Frames.Count; index += stride)
        {
            int tick = timeline.Frames[index].Tick;

            if (tick < start || tick > end)
            {
                continue;
            }

            drawn.Clear();
            players.Clear();
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, drawn);

            MomentInfo info = new(
                tick, tick, FirstPerson: false, Followed: null, EyeCamera: null, timeline.IntervalPerTick,
                ViewmodelFieldOfView: 54f, DrawViewmodel: false, RoundState: timeline.RoundStateAt(tick),
                Recorder: timeline.RecorderEntityIndex);

            scene.Build(players, drawn, info);
            scene.Pose(info);
            frames++;

            foreach (ModelInstance instance in scene.Instances)
            {
                if (instance.Flex is not { } face)
                {
                    continue;
                }

                faces++;

                if (!seen.TryGetValue(instance.ModelPath, out Model? model))
                {
                    model = new Model { Length = face.Length / 6 };
                    seen[instance.ModelPath] = model;
                }

                int moved = 0;
                int first = -1;
                int last = -1;

                for (int slot = 0; slot < model.Length; slot++)
                {
                    ReadOnlySpan<float> six = face.AsSpan(slot * 6, 6);

                    if (six.IndexOfAnyExcept(0f) >= 0)
                    {
                        moved++;
                        first = first < 0 ? slot : first;
                        last = slot;
                    }
                }

                model.Frames++;
                model.Moved += moved;
                model.MaxMoved = Math.Max(model.MaxMoved, moved);

                if (moved > 0)
                {
                    model.Span += last - first + 1;
                    model.First = Math.Min(model.First, first);
                    model.Last = Math.Max(model.Last, last);
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} ticks {start}-{end}: {frames} frames, {faces / Math.Max(1, frames):0.0} faces a frame"));
        output.WriteLine("model | slots | static: flexed, first-last, span | motion: frames, mean moved, max moved, mean span, first-last | inside");

        foreach ((string name, Model model) in seen.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            string fixedSet = "-";
            string inside = "-";

            if (models.FlexedSlotsOf(name) is { } flexed && flexed.Corners.At.Length > 0)
            {
                ImmutableArray<int> at = flexed.Corners.At;
                fixedSet = string.Create(CultureInfo.InvariantCulture,
                    $"{at.Length} ({100.0 * at.Length / flexed.Slots:0.0}%), {at[0]}-{at[^1]}, {at[^1] - at[0] + 1} ({100.0 * (at[^1] - at[0] + 1) / flexed.Slots:0.0}%)");
                inside = model.Last < 0 || (model.First >= at[0] && model.Last <= at[^1]) ? "yes" : "NO";
            }

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name} | {model.Length} | {fixedSet} | {model.Frames}, {(double)model.Moved / model.Frames:0}, {model.MaxMoved}, " +
                $"{(double)model.Span / model.Frames:0}, {(model.Last < 0 ? "-" : $"{model.First}-{model.Last}")} | {inside}"));
        }
    }
}
