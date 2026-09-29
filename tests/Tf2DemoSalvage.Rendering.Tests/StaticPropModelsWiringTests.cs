using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Every static prop drawn as a model, on a real map (B426, D198).</summary>
/// <remarks>
/// `engine.dll` `0x1800f1bd0`: a prop with baked `.vhv` colours draws them with no cube and no local
/// lights, and one without draws lit per frame like any model — both through the model draw. So on a
/// real map every placement must reach <see cref="MapAssets.StaticModels"/>, the baked ones must carry
/// their colours, and the world's batches must hold no prop at all.
/// </remarks>
public sealed class StaticPropModelsWiringTests
{
    [Test]
    public void Load_TheReferenceMap_DrawsEveryPropAsAModelAndTheBakedOnesWithTheirColours()
    {
        MapCache.LoadedMap loaded = MapCache.With();
        MapAssets assets = loaded.Assets;

        // The loader's own counts, carried from where it produced them: placements, and how many had baked colours.
        string summary = loaded.Log.From("props").First(line => line.Contains("ASKED FOR", StringComparison.Ordinal));
        int placed = int.Parse(Regex.Match(summary, @"ASKED FOR (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        int baked = int.Parse(Regex.Match(summary, @"HAVE baked lighting for (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

        baked.ShouldBeGreaterThan(0, "the control: the reference map has baked placements");
        assets.StaticModels.Count.ShouldBe(placed, "every placement is a model draw");
        assets.StaticModelColours.Count.ShouldBe(baked, "every baked placement carries its colours, and only those");

        // **Through the production pose**: the set that draws them, handed what `LevelSystems` hands it.
        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn);

        // Every placement drawn — which needs the static props' own models loaded with the map, not only
        // the ones the demo precaches (B426).
        drawn.Count.ShouldBe(placed, "a static prop whose model the map did not load");

        List<ModelInstance> coloured = [.. drawn.Where(instance => assets.StaticModelColours.ContainsKey(instance.EntityIndex))];

        coloured.Count.ShouldBe(baked, "every baked placement is drawn");
        coloured.Count(instance => instance.BakedColours is null)
            .ShouldBe(0, "a baked placement drawn without its colour mesh");
        coloured.Count(instance => instance.Light is not null || instance.Locals is { Count: > 0 })
            .ShouldBe(0, "a baked placement given a cube or lamps");
        drawn.Count(instance => !assets.StaticModelColours.ContainsKey(instance.EntityIndex) && instance.BakedColours is not null)
            .ShouldBe(0, "an unbaked placement drawn with colours");
    }

    /// <remarks>
    /// **B424, on a real map.** `FUN_1801bb830` (`engine.dll`) drops a baked prop's colours for full lighting in a frame
    /// where a light in its handle's list (`FUN_1801b6bf0`) has an animated style, `DAT_18069dd40[style] &gt; 1`. Measured
    /// 2026-09-28 (`styledprops`): no gcor map carries a styled world light; `koth_dryfield` carries one light on style 1,
    /// `world.cpp`'s FLICKER, reaching 56 placements. So with style 1 flickering some baked placements must reach the draw
    /// without colours and lit, and with style 1 a single letter none may.
    /// </remarks>
    [TestCase("mmnmmommommnonmmonqnmmo", true)]
    [TestCase("m", false)]
    public void Instances_KothDryfieldWithStyleOne_DropsBakedColoursOnlyWhileItFlickers(string pattern, bool flickers)
    {
        const string Map = "koth_dryfield";
        MapAssets assets = MapCache.With(mapName: Map).Assets;
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance), NullLogger.Instance);

        LightStyleValues values = new();
        values.Set(1, pattern);
        lighting.StyleAnimates = values.Animates;

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            BakedFallsBack = lighting.TakesFullLighting,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        List<ModelInstance> dropped =
            [.. drawn.Where(instance => assets.StaticModelColours.ContainsKey(instance.EntityIndex) && instance.BakedColours is null)];

        if (flickers)
        {
            dropped.Count.ShouldBeInRange(1, 56);
            dropped.ShouldAllBe(instance => instance.Light != null, "a dropped placement takes its handle's cube");
        }
        else
        {
            dropped.ShouldBeEmpty();
        }
    }

    /// <remarks>
    /// **B429, on a real map.** `FUN_1800eac60` (`engine.dll`) gives an unbaked placement a CPU-lit colour mesh
    /// (`FUN_1800f36e0` → `FUN_1800ee4a0`) when its model has `STUDIOHDR_FLAGS_STATIC_PROP`; one without stays lit per
    /// frame. `koth_harvest_final` has 8 unbaked placements of 652; the census of their models' flags is below, and every
    /// one must reach the draw on the path its flags choose.
    /// </remarks>
    [Test]
    public void Instances_KothHarvestUnbakedPlacements_TakeACpuColourMeshExactlyWhenCompiledStatic()
    {
        const string Map = "koth_harvest_final";
        MapAssets assets = MapCache.With(mapName: Map).Assets;
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(MapCache.Bytes(Map), NullLogger.Instance), NullLogger.Instance);

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPropLighting = lighting.StaticPropLightingAt,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        List<ModelInstance> unbaked = [.. drawn.Where(instance => !assets.StaticModelColours.ContainsKey(instance.EntityIndex))];
        unbaked.Count.ShouldBe(8, "the control: koth_harvest_final's unbaked placements");

        Dictionary<int, Core.Scene.SceneProp> byEntity = assets.StaticModels.ToDictionary(prop => prop.EntityIndex);
        int compiledStatic = 0;

        foreach (ModelInstance instance in unbaked)
        {
            string path = byEntity[instance.EntityIndex].ModelPath;
            int flags = assets.Geometry(path).ShouldNotBeNull().StudioFlags;
            bool cpu = StaticPropVertexLighting.Lights(flags);

            if (cpu)
            {
                compiledStatic++;
                instance.BakedColours.ShouldNotBeNull(path).Length.ShouldBeGreaterThan(0);
                (instance.Light is null && instance.Locals is not { Count: > 0 }).ShouldBeTrue(path);
            }
            else
            {
                instance.BakedColours.ShouldBeNull(path);
                instance.Light.ShouldNotBeNull(path);
            }
        }

        compiledStatic.ShouldBe(8, "measured 2026-09-28: all eight are box_cluster01/02 and tractor_tire001, flags 0x11");
    }

    /// <remarks>
    /// **B427, on the map the loader actually builds.** `CStaticProp::Init` (`engine.dll` `0x1802052c0`)
    /// lights a `STATIC_PROP_USE_LIGHTING_ORIGIN` prop at the lump's `m_LightingOrigin`. Measured
    /// 2026-09-28: 161 of 234 shipped maps flag some props (4,542 of 354,469); `cp_process_final` flags
    /// none and `koth_harvest_final` flags 3 of 652, so the three must reach the draw carrying exactly
    /// their lump point and nothing else may carry one.
    /// </remarks>
    [Test]
    public void Load_KothHarvest_CarriesEachFlaggedPropsLumpLightingOrigin()
    {
        const string Map = "koth_harvest_final";
        IReadOnlyList<Content.Bsp.BspStaticProp> lump = Content.Bsp.BspStaticProps.Read(MapCache.Bytes(Map));
        IReadOnlyList<Core.Scene.SceneProp> models = MapCache.With(mapName: Map).Assets.StaticModels;

        List<Core.Scene.SceneProp> lit = [.. models.Where(prop => prop.LightingOrigin is not null)];

        lit.Count.ShouldBe(3);

        foreach (Core.Scene.SceneProp prop in lit)
        {
            Content.Bsp.BspStaticProp placement = lump[prop.EntityIndex - PropModels.FirstStaticPropEntityIndex];
            placement.UsesLightingOrigin.ShouldBeTrue();
            prop.LightingOrigin.ShouldBe(placement.LightingOrigin);
        }
    }
}
