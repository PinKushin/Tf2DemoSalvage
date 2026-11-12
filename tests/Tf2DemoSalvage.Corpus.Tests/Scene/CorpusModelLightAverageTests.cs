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
/// `tf2-live-build11138802-spec-pov-cp_granary` is chosen because it is in gcor, so the merge gate runs this rather than skipping it, and
/// its map is installed; the walk is the whole recording. A model with no skeleton stands at his feet: the question is the light, and
/// bones would only cost time. On a machine without the game, as CI is, it skips.
/// </remarks>
public sealed class CorpusModelLightAverageTests
{
    private const string Demo = "tf2-live-build11138802-spec-pov-cp_granary";
    private const string Map = "cp_granary";

    /// <summary>A moving entity's index: any below the static props'.</summary>
    private const int Walker = 5;

    /// <summary>Over the recorder's head far enough to leave granary's roofs.</summary>
    private const float SunlitHeight = 600f;

    [Test]
    public void Instances_AlongARealWalkThroughARealMap_DrawTheEnginesRecurrenceOverTheRawStates()
    {
        (MapAssets assets, MapLevel level, DemoTimeline timeline) = Load();
        Walk averaged = Run(assets, level, timeline, true);
        Walk control = Run(assets, level, timeline, false);

        // This spectator never stands in the sun (0 of 17,494 draws), so the same walk again, up in the map's own sky.
        Walk sunlit = Run(assets, level, timeline, true, SunlitHeight);

        TestContext.Out.WriteLine($"sunlit: {sunlit}");
        sunlit.Suns.ShouldBeGreaterThan(0, "the walk up there must reach the sun");
        sunlit.WorstSlot.ShouldBeLessThan(0.0005f, "the sun's colour through the draw");

        control.WorstFromRaw.ShouldBe(0f, "r_lightaverage 0 draws each raw state exactly");
        control.Drawn.ShouldBe(averaged.Drawn, "the same walk");
        averaged.Drawn.ShouldBeGreaterThan(1000, "the instrument: a model at each tick the recorder is alive");
        averaged.LargestStep.ShouldBeGreaterThan(0.01f, "the walk must cross light, or a match says nothing");
        averaged.WorstFromRaw.ShouldBeGreaterThan(0.005f, "a model's light lags its state, the whole point");
        averaged.WorstFromRecurrence.ShouldBeLessThan(0.0005f, "new - (new - old) * exp(-5 dt), face by face");

        // The controls that each kind of slot was there to be wrong about, then the slots: lamps and the sun as the draw carries them.
        averaged.Lamps.ShouldBeGreaterThan(0, "the walk must reach a lamp");
        averaged.Left.ShouldBeGreaterThan(0, "a light must leave on the way");
        averaged.WorstSlot.ShouldBeLessThan(0.0005f, "each light's colour through the draw");
    }

    /// <summary>
    /// `r_worldlights 1` on the production path, `MomentScene.Build` and `Pose`, along the same walk: never more than one lamp reaches the draw, and
    /// what lost its slot is in the cube. The control is the default on the same walk, which must draw more than one somewhere.
    /// </summary>
    [Test]
    public void Pose_AlongARealWalkAtWorldLightsOfOne_DrawsOneLampAndFoldsTheRestIntoTheCube()
    {
        (MapAssets assets, MapLevel level, DemoTimeline timeline) = Load();
        (int oneMost, int oneDraws, float oneCube) = SceneWalk(assets, level, timeline, 1);
        (int fourMost, int fourDraws, float fourCube) = SceneWalk(assets, level, timeline, 4);

        TestContext.Out.WriteLine($"one: most {oneMost}, draws {oneDraws}, cube {oneCube:0.0}; four: most {fourMost}, draws {fourDraws}, cube {fourCube:0.0}");

        oneDraws.ShouldBe(fourDraws, "the same walk");
        oneDraws.ShouldBeGreaterThan(100, "the instrument: a model along the walk");
        fourMost.ShouldBeGreaterThan(1, "the control: this walk has more than one lamp to lose");
        oneMost.ShouldBe(1);
        oneCube.ShouldBeGreaterThan(fourCube, "the lamps that lost their slots went to the cube");
    }

    /// <summary>
    /// A model drawn where cp_process_final's sun and three lamps share its four slots (B170): the draw carries three local lights and the sun.
    /// The point is the `sun-slot` probe's first on that map that drew four lamps and the sun before the fix (77 of its 84 sun-and-three
    /// points did; granary's 3 did not, so granary cannot show it). A map, not a demo: the question is where a model stands.
    /// </summary>
    [Test]
    public void Instances_UnderALampInProcesssSun_DrawsTheSunAndThreeLamps()
    {
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find("cp_process_final") is not { } map || locator.FindGameFolder() is not { } folder)
        {
            Assert.Ignore("cp_process_final or the game folder is not available");
            return;
        }

        byte[] bytes = File.ReadAllBytes(map);
        MapAssets assets = MapAssets.Load(bytes, GameArchives.Open(folder), 64, mapName: map);
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(bytes, NullLogger.Instance), NullLogger.Instance, find: null);
        EntityModelSet models = new() { Geometry = assets.Geometry };
        SceneProp stands = assets.StaticModels[0] with { EntityIndex = Walker };
        SceneProp prop = stands with { Pose = stands.Pose with { X = -459f, Y = -1855f, Z = 721f } };
        List<ModelInstance> drawn = [];

        models.Add([prop]);
        models.Instances([prop], drawn, lighting.ModelLightingAt, lighting.ModelSunAt, 1d);

        ModelInstance instance = drawn.Single(instance => instance.EntityIndex == Walker);

        instance.Sun.ShouldNotBeNull("the control: the sun reaches this point");
        instance.Locals.ShouldNotBeNull().Count.ShouldBe(LocalLights.MaximumLocalLights - 1, "the sun holds the fourth slot");
    }

    private static (int MostLamps, int Draws, float Cube) SceneWalk(MapAssets assets, MapLevel level, DemoTimeline timeline, int worldLights)
    {
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance, find: null);
        EntityModelSet models = new() { Geometry = assets.Geometry };
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Lighting = lighting };
        SceneProp stands = assets.StaticModels[0] with { EntityIndex = Walker };
        List<ScenePlayer> players = [];
        int recorder = timeline.RecorderEntityIndex ?? throw new InvalidOperationException("a point-of-view demo has a recorder");
        int most = 0;
        int draws = 0;
        float cube = 0f;

        models.Add([stands]);

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 4)
        {
            timeline.PlayersAt(tick, players, false);

            int at = players.FindIndex(candidate => candidate.EntityIndex == recorder);

            if (at < 0)
            {
                continue;
            }

            SceneProp prop = stands with { Pose = stands.Pose with { X = players[at].X, Y = players[at].Y, Z = players[at].Z } };
            MomentInfo info = new(tick, tick, false, null, null, timeline.IntervalPerTick, 54f, WorldLights: worldLights);

            scene.Build([], [prop], info);
            scene.Pose(info);

            foreach (ModelInstance instance in scene.Instances)
            {
                if (instance.EntityIndex == Walker && instance.Light is { } light)
                {
                    draws++;
                    most = Math.Max(most, instance.Locals?.Count ?? 0);
                    cube += Faces(light).Sum();
                }
            }
        }

        return (most, draws, cube);
    }

    private static (MapAssets Assets, MapLevel Level, DemoTimeline Timeline) Load()
    {
        if (Corpus.Demo(Demo) is not { } path)
        {
            Assert.Ignore($"{Demo}.dem is not available");
            throw new InvalidOperationException();
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(Map) is not { } map || locator.FindGameFolder() is not { } folder)
        {
            Assert.Ignore($"{Map} or the game folder is not available");
            throw new InvalidOperationException();
        }

        byte[] bytes = File.ReadAllBytes(map);

        return (
            MapAssets.Load(bytes, GameArchives.Open(folder), 64, mapName: map),
            MapLevel.Read(bytes, NullLogger.Instance),
            TimelineCache.For(path));
    }

    private readonly record struct Walk(int Drawn, float LargestStep, float WorstFromRaw, float WorstFromRecurrence, float WorstSlot, int Suns, int Lamps, int Left);

    private static Walk Run(MapAssets assets, MapLevel level, DemoTimeline timeline, bool enabled, float height = 0f)
    {
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance, find: null);
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
        List<Slot> slots = [];
        float worstSlot = 0f;
        int sunDraws = 0;
        int lampDraws = 0;
        int leftDraws = 0;
        int sunAndThree = 0;
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

            SceneProp prop = stands with { Pose = stands.Pose with { X = player.X, Y = player.Y, Z = player.Z + height } };

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

            if (enabled)
            {
                PointLighting state = lighting.ModelLightingAt(origin.X, origin.Y, origin.Z);

                slots = Next(slots, state.State, f, first: count == 1);
                (worstSlot, sunDraws, lampDraws, leftDraws) = Compare(slots, state, shownSun: drawn[0].Sun, shownLamps: drawn[0].Locals ?? [], worstSlot, sunDraws, lampDraws, leftDraws);

                // The sun is one of the four (B170): never a sun beside four lamps, and a full house of the sun and three is counted.
                int lights = (drawn[0].Locals?.Count ?? 0) + (drawn[0].Sun is null ? 0 : 1);

                lights.ShouldBeLessThanOrEqualTo(LocalLights.MaximumLocalLights, "the sun takes one of the four slots");
                sunAndThree += drawn[0].Sun is not null && lights == LocalLights.MaximumLocalLights ? 1 : 0;
            }
        }

        TestContext.Out.WriteLine(
            $"average {enabled}: {count} draws, largest raw step {largestStep:0.0000}, worst from raw {worstFromRaw:0.0000}, worst from recurrence {worstFromRecurrence:0.000000}; " +
            $"worst slot {worstSlot:0.000000} of {sunDraws} suns, {lampDraws} lamps, {leftDraws} slots that had left; {sunAndThree} draws of the sun and three");

        return new Walk(count, largestStep, worstFromRaw, worstFromRecurrence, worstSlot, sunDraws, lampDraws, leftDraws);
    }

    /// <summary>One slot of the prediction: the light, its colour, and whether the state has let it go.</summary>
    private readonly record struct Slot(BspWorldLight Light, (float Red, float Green, float Blue) Colour, bool Left);

    /// <summary>`FUN_1800f4310` once, over what the engine's text says, separately from the code it holds to account.</summary>
    private static List<Slot> Next(List<Slot> stored, LightingState? state, float f, bool first)
    {
        List<BspWorldLight> now = [];

        // The sun among them, in its slot (B170).
        for (int at = 0; state is not null && at < state.Count; at++)
        {
            now.Add(state.SlotAt(at));
        }

        // A dlight by its slot, a lump light by its entry, the sun being one light.
        bool Same(BspWorldLight a, BspWorldLight b)
        {
            if (a.DynamicSlot >= 0 || b.DynamicSlot >= 0)
            {
                return a.DynamicSlot == b.DynamicSlot;
            }

            return a.Kind == WorldLightKind.SkyLight || b.Kind == WorldLightKind.SkyLight ? a.Kind == b.Kind : a.LumpIndex == b.LumpIndex;
        }

        List<Slot> next = [];

        foreach (BspWorldLight light in now)
        {
            (float red, float green, float blue) = light.Intensity;
            (float Red, float Green, float Blue) old = first ? light.Intensity : stored.Where(slot => Same(slot.Light, light)).Select(static slot => slot.Colour).FirstOrDefault();

            next.Add(new Slot(light, (red - ((red - old.Red) * f), green - ((green - old.Green) * f), blue - ((blue - old.Blue) * f)), false));
        }

        foreach (Slot slot in stored)
        {
            (float red, float green, float blue) = slot.Colour;

            if (!now.Exists(light => Same(slot.Light, light)) && (red * red) + (green * green) + (blue * blue) >= 1f)
            {
                if (next.Count >= LocalLights.MaximumLocalLights)
                {
                    break;
                }

                next.Add(new Slot(slot.Light, (f * red, f * green, f * blue), true));
            }
        }

        return next;
    }

    private static (float Worst, int Suns, int Lamps, int Left) Compare(
        List<Slot> slots, PointLighting state, SunLight? shownSun, IReadOnlyList<LocalLight> shownLamps, float worst, int suns, int lamps, int left)
    {
        int lamp = 0;

        foreach (Slot slot in slots)
        {
            left += slot.Left ? 1 : 0;

            if (slot.Light.Kind == WorldLightKind.SkyLight)
            {
                suns++;

                SunLight sun = shownSun.ShouldNotBeNull("the sun in the prediction reached the draw");

                worst = Math.Max(worst, Relative3((sun.Red, sun.Green, sun.Blue), slot.Colour, 1f));
            }
            else if (slot.Light.Kind != WorldLightKind.QuakeLight)
            {
                lamps++;

                LocalLight shown = shownLamps[lamp++];

                worst = Math.Max(worst, Relative3((shown.Red, shown.Green, shown.Blue), slot.Colour, state.Style?.Invoke(slot.Light.Style) ?? 1f));
            }
        }

        lamp.ShouldBe(shownLamps.Count, "the draw holds the lamps the prediction does, and no others");
        (slots.Exists(static slot => slot.Light.Kind == WorldLightKind.SkyLight)).ShouldBe(shownSun is not null);

        return (worst, suns, lamps, left);
    }

    /// <summary>Lump colours run to hundreds, so a float's error is compared to the size of what it is in.</summary>
    private static float Relative(float shown, float predicted) => Math.Abs(shown - predicted) / (Math.Abs(predicted) + 1f);

    private static float Relative3((float Red, float Green, float Blue) shown, (float Red, float Green, float Blue) colour, float style) =>
        Math.Max(
            Relative(shown.Red, colour.Red * style),
            Math.Max(Relative(shown.Green, colour.Green * style), Relative(shown.Blue, colour.Blue * style)));

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
