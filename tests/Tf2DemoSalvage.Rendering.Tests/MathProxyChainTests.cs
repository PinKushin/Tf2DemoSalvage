using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That a real map's materials carry the proxies this work evaluates (B337).
/// </summary>
/// <remarks>
/// **The hop nothing else watches, and the one this change could break silently.**
/// `MathProxyConformanceTests` proves the arithmetic against `mathproxy.cpp`; nothing there can say
/// whether the variable table reaches the materials that run these proxies. The table used to be
/// created only where `$colortint_base` existed — the paint chain was the only chain — and
/// `YellowLevel` writes `$yellow` on 7,570 materials, most carrying no paint at all. A version that
/// left the gate in place would evaluate nothing on those while every conformance test stayed
/// green.
/// </remarks>
public sealed class MathProxyChainTests
{
    /// <remarks>
    /// **Counted with the majority as the control**, the shape every wiring test here uses:
    /// "materials run proxies" and "every material runs proxies" are the same observation without
    /// it.
    /// </remarks>
    [Test]
    public void Proxies_ARealMapsMaterials_IncludeTheArithmeticOnes()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int running = assets.Proxies.Count(list => list.Count > 0);
        int none = assets.Proxies.Count(list => list.Count == 0);

        string[] names =
        [
            .. assets.Proxies.SelectMany(list => list).Select(proxy => proxy.Name).Distinct(),
        ];

        TestContext.Out.WriteLine(
            $"{running} of {running + none} materials run a proxy: {string.Join(", ", names)}");

        running.ShouldBeGreaterThan(0, "cp_process_final's materials run proxies");
        none.ShouldBeGreaterThan(running, "and the great majority run none");
    }

    /// <remarks>
    /// **The materials that would have been missed by the old gate.** A material running a
    /// variable-writing proxy and carrying no `$colortint_base` got no variable table at all, so
    /// every proxy in its chain was skipped. This asserts such materials EXIST on a real map —
    /// without them the widening is untested by construction, and the count is what says the fix
    /// was needed rather than tidy.
    /// </remarks>
    [Test]
    public void Proxies_MaterialsWithNoPaintButAChain_AreOnTheMap()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        string[] chained = ["Equals", "Add", "Subtract", "Multiply", "Divide", "Clamp",
            "YellowLevel", "SelectFirstIfNonZero"];

        int untinted = Enumerable.Range(0, assets.Proxies.Count)
            .Count(index =>
                assets.Proxies[index].Any(proxy => chained.Contains(proxy.Name)) &&
                (index >= assets.Textures.Count ||
                    assets.Textures[index] is not { TintBase: not null }));

        TestContext.Out.WriteLine(
            $"{untinted} materials run a chained proxy and carry no $colortint_base");

        untinted.ShouldBeGreaterThan(
            0,
            "these are the materials the variable table used to skip entirely; if this is zero the "
            + "widening in B337 is untested by construction");
    }

    /// <remarks>
    /// **Every source a real chain reads must be findable, and this is what B340 was about.** A
    /// proxy's source is looked up on the MATERIAL — `FindVar` — so a chain seeded from proxy
    /// outputs alone drops any operation reading a declared constant. `dec18_dumb_bell.vmt`
    /// multiplies `$saturatedTint` by `$tintMulti`, the constant `"10"`, and lost its phong and
    /// envmap tint multiplier entirely.
    ///
    /// **Asserted as a COUNT of unfindable sources rather than as a list**, because the map's own
    /// materials are Valve's to change: what must hold is that nothing a chain reads is missing,
    /// whatever the chains happen to be.
    /// </remarks>
    [Test]
    public void Variables_EverySourceARealMapsChainsRead_IsFindable()
    {
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        List<string> unfindable = [];
        int checkedSources = 0;

        for (int index = 0; index < assets.Proxies.Count; index++)
        {
            IReadOnlyDictionary<string, (float Red, float Green, float Blue)>? declared =
                index < assets.Variables.Count ? assets.Variables[index] : null;

            // What an earlier proxy in the same block wrote is findable too, so the set grows as
            // the chain runs — exactly as the renderer's table does.
            HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);

            foreach (MaterialProxy proxy in assets.Proxies[index])
            {
                foreach (string argument in new[] { "srcVar1", "srcVar2" })
                {
                    if (proxy.Argument(argument) is not { Length: > 0 } reference)
                    {
                        continue;
                    }

                    checkedSources++;

                    string name = MaterialProxies.Reference(reference).Name;

                    if (declared?.ContainsKey(name) != true && !written.Contains(name))
                    {
                        unfindable.Add($"{assets.Materials[index].Name}: {proxy.Name} reads {name}");
                    }
                }

                if (proxy.Argument("resultVar") is { Length: > 0 } result)
                {
                    written.Add(MaterialProxies.Reference(result).Name);
                }
            }
        }

        TestContext.Out.WriteLine(
            $"{checkedSources} proxy sources across the map; unfindable: "
            + (unfindable.Count == 0 ? "none" : string.Join(", ", unfindable)));

        checkedSources.ShouldBeGreaterThan(
            0, "the map's materials run proxies with sources, or this test measures nothing");

        unfindable.ShouldBeEmpty(
            "a source the material declares must be findable; anything here is an operation the "
            + "engine runs and this renderer refuses");
    }

    private static MapAssets? Assets
    {
        get
        {
            if (GameInstall.Root is not { } tf ||
                !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
            {
                return null;
            }

            return MapCache.Load();
        }
    }
}
