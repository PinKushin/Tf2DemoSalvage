using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Does any shipped map actually layer its overlays? Scans every stock BSP and reports.
/// </summary>
/// <remarks>
/// **Written to decide whether an open gap is worth closing.** `doverlay_t` packs a two-bit render
/// order beside the face count, this project parses it and nothing sorts by it, and that was filed
/// as a divergence during B135. Then `OverlayPassConformanceTests` measured cp_process and found
/// **every overlay at order 0** — so on the map this project renders, implementing the sort would
/// change nothing at all.
///
/// One map is not an answer about the game. This walks the whole stock map list and counts, so the
/// risk entry can carry a number instead of a worry.
///
/// `[Explicit]` because it opens every BSP Valve ships; it is a measurement, not a gate.
/// </remarks>
[Explicit("Scans every installed stock map; run deliberately.")]
public sealed class OverlayRenderOrderProbe
{
    [Test]
    public void RenderOrder_AcrossEveryStockMap_IsCounted()
    {
        string maps = Path.Combine(GameInstall.Require(), "maps");

        if (!Directory.Exists(maps))
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        Dictionary<int, int> byOrder = [];
        List<string> layered = [];
        int scanned = 0;
        int failed = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*.bsp"))
        {
            IReadOnlyList<BspOverlay> overlays;

            try
            {
                overlays = BspOverlays.Read(File.ReadAllBytes(file));
            }
            catch (InvalidDataException error)
            {
                // Reported rather than swallowed: a map this reader cannot open is a finding of its
                // own, and counting it as "no layered overlays" would be the wrong answer quietly.
                TestContext.Out.WriteLine($"  UNREADABLE {Path.GetFileName(file)}: {error.Message}");
                failed++;
                continue;
            }

            scanned++;
            HashSet<int> here = [];

            foreach (BspOverlay overlay in overlays)
            {
                byOrder[overlay.RenderOrder] = byOrder.GetValueOrDefault(overlay.RenderOrder) + 1;
                here.Add(overlay.RenderOrder);
            }

            if (here.Count > 1)
            {
                layered.Add($"{Path.GetFileName(file)} ({string.Join(",", here)})");
            }
        }

        TestContext.Out.WriteLine($"maps scanned: {scanned}, unreadable: {failed}");
        TestContext.Out.WriteLine("overlays by render order:");

        foreach (KeyValuePair<int, int> entry in byOrder)
        {
            TestContext.Out.WriteLine($"  order {entry.Key}: {entry.Value}");
        }

        TestContext.Out.WriteLine($"maps using more than one order: {layered.Count}");

        foreach (string map in layered)
        {
            TestContext.Out.WriteLine($"  {map}");
        }

        scanned.ShouldBeGreaterThan(0, "no map was read, so the counts above mean nothing");
    }
}
