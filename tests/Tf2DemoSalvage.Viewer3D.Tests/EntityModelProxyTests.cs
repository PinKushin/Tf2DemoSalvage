using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whether an entity model's materials bring their proxies with them.
/// </summary>
/// <remarks>
/// **This is the path the proxy work was aimed at and could not reach.** The materials that run a
/// <c>Sine</c> on a TF2 map are the capture point's — <c>models/effects/cappoint_logo_blue</c>
/// pulses its <c>$alpha</c> between .6 and .7 over three tenths of a second — and those live on
/// entity models rather than on brushwork or static props.
///
/// **Entity models are named by a demo's <c>modelprecache</c>**, so a map loaded on its own has
/// none, which is why <c>ProxyRenderTests</c> correctly skipped: cp_process_final's own seven
/// proxies are all entity-state ones this does not evaluate. <c>MapAssets.Load</c> takes the model
/// list directly, so the path can be exercised by naming the model rather than by finding a demo
/// that happens to contain it.
///
/// Worth stating because it generalises: this project can also **author** a demo containing
/// whatever it needs to test, since the writer round-trips and the 2007 client plays what it
/// produces. A case the corpus does not contain is not automatically a case that cannot be tested.
/// </remarks>
public sealed class EntityModelProxyTests
{

    /// <summary>Capture point models, which are what carry a time-driven proxy in TF2.</summary>
    /// <remarks>
    /// Named rather than discovered, because the point is to exercise a specific known material.
    /// A model that is not installed contributes nothing and is skipped by the loader, so listing
    /// several costs nothing and does not make the test depend on one path being exactly right.
    /// </remarks>
    private static readonly string[] CapturePointModels =
    [
        "models/props_gameplay/cap_point_base.mdl",
        "models/effects/cappoint_hologram.mdl",
    ];

    [Test]
    public void AnEntityModelsMaterialsBringTheirProxies()
    {
        MapAssets assets = LoadWithEntityModels();

        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (MaterialProxy proxy in assets.Proxies.SelectMany(list => list))
        {
            byName[proxy.Name] = byName.GetValueOrDefault(proxy.Name) + 1;
        }

        TestContext.Out.WriteLine(
            "proxies: " + string.Join(", ", byName.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} x{pair.Value}")));

        TestContext.Out.WriteLine(
            $"{assets.Materials.Count} materials, " +
            $"{assets.Proxies.Count(list => list.Count > 0)} running a proxy");

        // **The claim, and it is specifically about the kind this renderer can evaluate.** The map
        // alone yields only entity-state proxies; naming the capture point models must bring in at
        // least one time-driven one, or the entity path is still dropping them.
        byName.Keys
            .Any(name =>
                name.Equals("Sine", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TextureScroll", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue(
                "a capture point's materials run a Sine, and entity models must carry it through");
    }

    [Test]
    public void NamingNoEntityModelsBringsNoneOfThem()
    {
        // **The control**, and it is what makes the test above evidence rather than a coincidence.
        // The same map without the model list must NOT contain those proxies — otherwise they were
        // coming from the brushwork all along and the entity path proves nothing.
        MapAssets without = LoadTheMap(entityModels: null);

        bool timeDriven = without.Proxies
            .SelectMany(list => list)
            .Any(proxy =>
                proxy.Name.Equals("Sine", StringComparison.OrdinalIgnoreCase) ||
                proxy.Name.Equals("TextureScroll", StringComparison.OrdinalIgnoreCase));

        TestContext.Out.WriteLine(
            $"without entity models: {without.Proxies.Count(list => list.Count > 0)} materials run a proxy");

        timeDriven.ShouldBeFalse(
            "this map's own materials run only entity-state proxies, so any Sine came from a model");
    }

    [Test]
    public void AnEntityModelsMaterialsExtendTheTable()
    {
        // The precondition for either of the above meaning anything: naming models must actually
        // add materials. If the models are not installed this is where it says so, rather than the
        // proxy assertions failing for a reason that is not about proxies.
        MapAssets with = LoadWithEntityModels();
        MapAssets without = LoadTheMap(entityModels: null);

        TestContext.Out.WriteLine(
            $"{without.Materials.Count} materials without models, {with.Materials.Count} with");

        with.Materials.Count.ShouldBeGreaterThan(
            without.Materials.Count, "an entity model's materials continue the map's table");
    }

    private static MapAssets LoadWithEntityModels() => LoadTheMap(CapturePointModels);

    /// <summary>
    /// Shared with every other test asking for the same map and model list.
    /// </summary>
    /// <remarks>
    /// **This test used to load the map twice and was the second-slowest in the suite at 50s**, one
    /// load for the entity models and one for the control without them. Both are now cache entries,
    /// so the pair costs what one load costs and the control is free for anyone else who wants it.
    /// </remarks>
    private static MapAssets LoadTheMap(IReadOnlyCollection<string>? entityModels) =>
        MapCache.Load(entityModels: entityModels);
}
