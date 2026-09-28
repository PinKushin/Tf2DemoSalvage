using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Which static props leave the world batches for the model draw, on a real map (B426).</summary>
/// <remarks>
/// `engine.dll` `0x1800f1bd0`: a prop with baked `.vhv` colours draws them with no cube and no local
/// lights, and one without draws lit per frame like any model. So on a real map the unbaked placements
/// must reach <see cref="MapAssets.StaticModels"/> and none of their corners may stay in the world's
/// batches, while the baked ones stay there.
/// </remarks>
public sealed class StaticPropModelsWiringTests
{
    [Test]
    public void Load_TheReferenceMap_DrawsUnbakedPropsAsModelsAndKeepsBakedOnesInTheWorld()
    {
        MapCache.LoadedMap loaded = MapCache.With();
        MapAssets assets = loaded.Assets;

        // The loader's own counts, carried from where it produced them: placements, and how many had baked colours.
        string summary = loaded.Log.From("props").First(line => line.Contains("ASKED FOR", StringComparison.Ordinal));
        int placed = int.Parse(Regex.Match(summary, @"ASKED FOR (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        int baked = int.Parse(Regex.Match(summary, @"HAVE baked lighting for (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

        baked.ShouldBeGreaterThan(0, "the control: the reference map has baked placements");
        assets.StaticModels.Count.ShouldBe(placed - baked, "every placement vrad never lit, and only those");
        assets.Props.ShouldNotBeEmpty("the baked placements stay in the world batches");

        HashSet<(float, float)> modelled = [.. assets.StaticModels.Select(static prop => (prop.Pose.X, prop.Pose.Y))];

        assets.Props.Count(corner => modelled.Contains((corner.OriginX, corner.OriginY)))
            .ShouldBe(0, "an unbaked placement's corners were also merged into the world");
    }
}
