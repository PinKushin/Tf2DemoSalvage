using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*`, where `Corpus` binds to the namespace.
namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>B676 on real bytes: what the production model set hands the draw along a real player's walk through a real map's light.</summary>
/// <remarks>
/// **Why real bytes**: `ModelLightAverageConformanceTests` chooses every value it blends. This is the one place that reads what
/// <see cref="EntityModelSet.Instances"/> carried to a <see cref="ModelInstance"/>, with the map's cubes and the light cache's own entries,
/// so a wire left out of the draw path fails here and nowhere else. The prediction is the engine's recurrence, run here over the raw
/// states the same cache answers. **The control** is `r_lightaverage 0`, the same walk, which must draw every raw state exactly.
///
/// `hudref-cp_process_f12-2026-10-17-pov` is chosen for being short, one player, on cp_process_f12, the parity map; the walk is the
/// whole recording. A model with no skeleton stands at his feet: the question is the light, and bones would only cost time.
/// </remarks>
public sealed class CorpusModelLightAverageTests
{
    private const string Demo = "hudref-cp_process_f12-2026-10-17-pov";

    /// <summary>A moving entity's index: any below the static props'.</summary>
    private const int Walker = 5;

    [Test]
    public void Instances_AlongARealWalkThroughARealMap_DrawTheEnginesRecurrenceOverTheRawStates()
    {
        (Walk averaged, Walk control) = Walks();

        control.WorstFromRaw.ShouldBe(0f, "r_lightaverage 0 draws each raw state exactly");
        control.Drawn.ShouldBe(averaged.Drawn, "the same walk");
        averaged.Drawn.ShouldBeGreaterThan(1000, "the instrument: a model at each tick the recorder is alive");
        averaged.LargestStep.ShouldBeGreaterThan(0.01f, "the walk must cross light, or a match says nothing");
        averaged.WorstFromRaw.ShouldBeGreaterThan(0.005f, "a model's light lags its state, the whole point");
        averaged.WorstFromRecurrence.ShouldBeLessThan(0.0005f, "new - (new - old) * exp(-5 dt), face by face");
    }

    /// <summary>
    /// The gate, by who stores a history: `FUN_1800f2dd0` passes no static prop, as it has a handle (B674). The control is the moving model,
    /// which must store one.
    /// </summary>
    [Test]
    public void Instances_AStaticProp_StoresNoHistory()
    {
        (MapAssets assets, MapLevel level, _) = Load();
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance);
        EntityModelSet models = new() { Geometry = assets.Geometry };
        SceneProp walker = assets.StaticModels[0] with { EntityIndex = Walker };
        List<ModelInstance> drawn = [];

        models.Add(assets.StaticModels);
        models.Add([walker]);

        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt, 1d);
        drawn.Count.ShouldBeGreaterThan(100, "the instrument: the static props were drawn");
        models.LightAverage.Instances.ShouldBe(0);

        models.Instances([walker], drawn, lighting.ModelLightingAt, lighting.ModelSunAt, 1d);
        models.LightAverage.Instances.ShouldBe(1);
    }

    private static (Walk Averaged, Walk Control) Walks()
    {
        (MapAssets assets, MapLevel level, DemoTimeline timeline) = Load();

        return (Run(assets, level, timeline, true), Run(assets, level, timeline, false));
    }

    private static (MapAssets Assets, MapLevel Level, DemoTimeline Timeline) Load()
    {
        if (Corpus.Demo(Demo) is not { } path)
        {
            Assert.Ignore($"{Demo}.dem is not available");
            throw new InvalidOperationException();
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find("cp_process_f12") is not { } map || locator.FindGameFolder() is not { } folder)
        {
            Assert.Ignore("cp_process_f12 or the game folder is not available");
            throw new InvalidOperationException();
        }

        byte[] bytes = File.ReadAllBytes(map);

        return (
            MapAssets.Load(bytes, GameArchives.Open(folder), 64, mapName: map),
            MapLevel.Read(bytes, NullLogger.Instance),
            TimelineCache.For(path));
    }

    private readonly record struct Walk(int Drawn, float LargestStep, float WorstFromRaw, float WorstFromRecurrence);

    private static Walk Run(MapAssets assets, MapLevel level, DemoTimeline timeline, bool enabled)
    {
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance);
        EntityModelSet models = new() { Geometry = assets.Geometry };

        models.LightAverage.Enabled = enabled;

        SceneProp stands = assets.StaticModels[0] with { EntityIndex = Walker };
        List<ScenePlayer> players = [];
        List<ModelInstance> drawn = [];
        float[]? predicted = null;
        float[]? last = null;
        float largestStep = 0f;
        float worstFromRaw = 0f;
        float worstFromRecurrence = 0f;
        int count = 0;
        int recorder = timeline.RecorderEntityIndex ?? throw new InvalidOperationException("a point-of-view demo has a recorder");

        models.Add([stands]);

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick++)
        {
            timeline.PlayersAt(tick, players, false);

            int at_ = players.FindIndex(candidate => candidate.EntityIndex == recorder);

            if (at_ < 0)
            {
                continue;
            }

            ScenePlayer player = players[at_];

            lighting.Frame++;

            SceneProp prop = stands with { Pose = stands.Pose with { X = player.X, Y = player.Y, Z = player.Z } };

            models.Instances([prop], drawn, lighting.ModelLightingAt, lighting.ModelSunAt, tick * timeline.IntervalPerTick);

            if (drawn.FirstOrDefault(instance => instance.EntityIndex == Walker) is not { Light: { } light, Origin: { } origin })
            {
                continue;
            }

            count++;

            float[] raw = Faces(lighting.ModelLightingAt(origin.X, origin.Y, origin.Z).Cube);
            float[] shown = Faces(light);

            // `FUN_1800f4090`, over a tick's dt, from a first draw at the raw state.
            float f = MathF.Exp(-5f * timeline.IntervalPerTick);

            predicted = predicted is null
                ? raw
                : [.. raw.Select((value, at) => Math.Clamp(value - ((value - predicted[at]) * f), 0f, 8f))];

            for (int at = 0; at < raw.Length; at++)
            {
                worstFromRaw = Math.Max(worstFromRaw, Math.Abs(shown[at] - raw[at]));
                worstFromRecurrence = Math.Max(worstFromRecurrence, Math.Abs(shown[at] - predicted[at]));

                if (last is not null)
                {
                    largestStep = Math.Max(largestStep, Math.Abs(raw[at] - last[at]));
                }
            }

            last = raw;
        }

        TestContext.Out.WriteLine(
            $"average {enabled}: {count} draws, largest raw step {largestStep:0.0000}, worst from raw {worstFromRaw:0.0000}, worst from recurrence {worstFromRecurrence:0.000000}");

        return new Walk(count, largestStep, worstFromRaw, worstFromRecurrence);
    }

    private static float[] Faces(AmbientCube cube) =>
    [
        cube.PositiveX.Red, cube.PositiveX.Green, cube.PositiveX.Blue,
        cube.NegativeX.Red, cube.NegativeX.Green, cube.NegativeX.Blue,
        cube.PositiveY.Red, cube.PositiveY.Green, cube.PositiveY.Blue,
        cube.NegativeY.Red, cube.NegativeY.Green, cube.NegativeY.Blue,
        cube.PositiveZ.Red, cube.PositiveZ.Green, cube.PositiveZ.Blue,
        cube.NegativeZ.Red, cube.NegativeZ.Green, cube.NegativeZ.Blue,
    ];
}
