using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
}
