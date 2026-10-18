using System;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The map cache keeps a map exactly as long as a test holds it.</summary>
public sealed class MapCacheTests
{
    /// <remarks>
    /// **Holding only the assets keeps the load** — the review's red, 2026-10-18: a memo of the assets-and-log pair was
    /// collectable while a test still held the assets, so the next ask loaded a second copy of the map beside the first.
    /// </remarks>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The subject is collection: the test must force one to show a held map survives it.")]
    public void Load_WhileATestHoldsOnlyTheAssets_IsNotLoadedAgainAfterACollection()
    {
        MapAssets held = MapCache.Load(entityModels: ["models/player/scout.mdl"]);
        int loads = MapCache.LoadsSoFar;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        MapAssets again = MapCache.Load(entityModels: ["models/player/scout.mdl"]);

        (ReferenceEquals(held, again), MapCache.LoadsSoFar - loads).ShouldBe((true, 0));
    }
}
