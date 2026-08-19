using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Material proxies, drawn on a real device and measured in pixels.
/// </summary>
/// <remarks>
/// **The arithmetic was ported and tested for months and nothing called it.** `MaterialProxies.Sine`
/// and `.TextureScroll` had parity tests against Valve's own routines, the transforms and the
/// modulation colour were plumbed to the shader, and no production code evaluated a single proxy —
/// found by grepping for callers, not by any test failing. Fourth instance of that pattern in this
/// project.
///
/// So the assertion that matters is not "does Sine compute the right number" — that is covered —
/// but **does the number reach a pixel**. The discriminator is playback time: a proxied material
/// must draw differently at two different times, and an unproxied one must not.
/// </remarks>
public sealed class ProxyRenderTests
{
    private const string MapName = "cp_process_final";

    private static float[] Camera =>
        new FreeCamera
        {
            Origin = (0f, -600f, 64f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    [Test]
    public void AProxiedMaterialDrawsDifferentlyAtDifferentTimes()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        if (Proxied(assets) is not { } material)
        {
            Assert.Ignore($"no material on {MapName} runs a proxy this renderer evaluates");
            return;
        }

        // Half a second apart. The capture point sign's Sine has a period of .3, so half a second
        // is not a whole number of cycles for it — a full period apart would return the same value
        // and the test could not fail.
        (int R, int G, int B) early = Draw(target, assets, material, seconds: 0d);
        (int R, int G, int B) later = Draw(target, assets, material, seconds: 0.5d);

        TestContext.Out.WriteLine($"material {material}: t=0 {early}, t=0.5 {later}");

        (early.R + early.G + early.B).ShouldBeGreaterThan(
            0, "the surface must be drawn before its proxy can be measured");

        (early.R + early.G + early.B).ShouldNotBe(
            later.R + later.G + later.B,
            "a proxy is a function of time, so two times must give two pictures");
    }

    [Test]
    public void AMaterialWithNoProxyIsUnaffectedByTime()
    {
        // **The control.** Without it, "the picture changed" could be anything else in the renderer
        // that varies with time, and there would be no evidence the proxy caused it.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        int material = Enumerable.Range(0, assets.Proxies.Count)
            .First(index => assets.Proxies[index].Count == 0 && assets.Textures[index] is not null);

        (int R, int G, int B) early = Draw(target, assets, material, seconds: 0d);
        (int R, int G, int B) later = Draw(target, assets, material, seconds: 0.5d);

        TestContext.Out.WriteLine($"unproxied material {material}: t=0 {early}, t=0.5 {later}");

        early.ShouldBe(later, "nothing but a proxy makes a world material vary with time");
    }

    [Test]
    public void TheMapRunsProxiesWorthEvaluating()
    {
        // A count, so the two tests above cannot both skip silently on a map that happens to
        // declare none — which would leave the whole feature unmeasured with a green suite.
        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        foreach (MaterialProxyName proxy in assets.Proxies.SelectMany(Named))
        {
            byName[proxy.Name] = byName.GetValueOrDefault(proxy.Name) + 1;
        }

        TestContext.Out.WriteLine(
            string.Join(", ", byName.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Key} x{pair.Value}")));

        assets.Proxies.Count(list => list.Count > 0).ShouldBeGreaterThan(
            0, $"{MapName} declares material proxies and they must survive the load");
    }

    private readonly record struct MaterialProxyName(string Name);

    private static IEnumerable<MaterialProxyName> Named(
        IReadOnlyList<Tf2DemoSalvage.Content.Assets.MaterialProxy> proxies) =>
        proxies.Select(proxy => new MaterialProxyName(proxy.Name));

    /// <summary>Draws a full-view quad of one material at one playback time.</summary>
    private static (int R, int G, int B) Draw(
        OffscreenTarget target, MapAssets assets, int material, double seconds)
    {
        List<WorldVertex> vertices =
        [
            new(-256f, 0f, -256f, 0f, 0f, 0f, 0f, 0f),
            new(256f, 0f, -256f, 1f, 0f, 0f, 0f, 0f),
            new(256f, 0f, 256f, 1f, 1f, 0f, 0f, 0f),
            new(-256f, 0f, -256f, 0f, 0f, 0f, 0f, 0f),
            new(256f, 0f, 256f, 1f, 1f, 0f, 0f, 0f),
            new(-256f, 0f, 256f, 0f, 1f, 0f, 0f, 0f),
        ];

        target.Clear(0f, 0f, 0f);
        target.Seconds = seconds;
        target.DrawWorld(vertices, [new WorldBatch(material, 0, vertices.Count)], Camera, assets);

        return target.PixelAt(32, 32);
    }

    /// <summary>The first material running a proxy this renderer evaluates, or null.</summary>
    private static int? Proxied(MapAssets assets) =>
        Enumerable.Range(0, assets.Proxies.Count)
            .Cast<int?>()
            .FirstOrDefault(index =>
                assets.Textures[index!.Value] is not null &&
                assets.Proxies[index.Value].Any(proxy =>
                    proxy.Name.Equals("Sine", StringComparison.OrdinalIgnoreCase) ||
                    proxy.Name.Equals("TextureScroll", StringComparison.OrdinalIgnoreCase)));

    private static MapAssets? Assets
    {
        get
        {
            // **Entity models named, because that is where a time-driven proxy lives.** A map on
            // its own yields only entity-state proxies — Subtract, PlayerProximity, Clamp — and
            // this test would skip forever without ever being wrong. The capture point's materials
            // run the Sine and the TextureScroll that this renderer evaluates.
            // Shared with EntityModelProxyTests, which asks for the same models — so the two files
            // together cost one load rather than three.
            return MapCache.Load(
                entityModels:
                ["models/props_gameplay/cap_point_base.mdl", "models/effects/cappoint_hologram.mdl"]);
        }
    }
}
