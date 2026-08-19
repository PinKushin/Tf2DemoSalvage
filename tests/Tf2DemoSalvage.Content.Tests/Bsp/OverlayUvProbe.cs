using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What a real map actually stores in an overlay's UV points.
/// </summary>
/// <remarks>
/// **The renderer that interprets these is not published.** <c>vbsp/overlay.cpp</c> copies
/// <c>uv0</c>–<c>uv3</c> straight from Hammer's keys without interpreting them, and the engine code
/// that turns them into texture coordinates is closed. So the file itself is the remaining
/// authority, and this prints what it holds.
///
/// The question: are the four corners written in a fixed winding, so that corner <c>i</c> always
/// carries the same texture corner — or is the mapping implied by each corner's own position in
/// the basis plane? cp_process's CAPTURE ZONE decal renders a quarter turn out, which says the
/// assumption in the viewer is wrong for at least some overlays.
/// </remarks>
public sealed class OverlayUvProbe
{
    [Test]
    [Explicit("Diagnostic. Prints the UV points a map stores for its overlays.")]
    public void Overlays_TheirStoredFields_AreReported()
    {
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage",
            "maps",
            "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore($"No map at {map}; open a demo in the viewer first.");
            return;
        }

        byte[] file = File.ReadAllBytes(map);

        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(file);
        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(file);

        overlays.Count.ShouldBeGreaterThan(0);

        TestContext.Out.WriteLine($"OVERLAYS {overlays.Count}");

        foreach (BspOverlay overlay in overlays.Take(12))
        {
            string name = overlay.MaterialIndex >= 0 && overlay.MaterialIndex < materials.Count
                ? materials[overlay.MaterialIndex].Name
                : "none";

            string corners = string.Join(
                "  ",
                overlay.Corners.Select(corner => $"({corner.X,7:F1},{corner.Y,7:F1})"));

            TestContext.Out.WriteLine(
                $"  U {overlay.U.Start,5:F2}..{overlay.U.End,-5:F2} " +
                $"V {overlay.V.Start,5:F2}..{overlay.V.End,-5:F2}  {corners}  {name}");
        }

        // The capture zone decal is the one that renders a quarter turn out, so it is worth
        // finding by name rather than hoping it is in the first dozen.
        foreach (BspOverlay overlay in overlays.Where(one =>
            one.MaterialIndex >= 0 &&
            one.MaterialIndex < materials.Count &&
            materials[one.MaterialIndex].Name.Contains("capture", StringComparison.OrdinalIgnoreCase)))
        {
            string corners = string.Join(
                "  ",
                overlay.Corners.Select(corner => $"({corner.X,7:F1},{corner.Y,7:F1})"));

            TestContext.Out.WriteLine(
                $"CAPTURE U {overlay.U.Start:F2}..{overlay.U.End:F2} " +
                $"V {overlay.V.Start:F2}..{overlay.V.End:F2}  {corners}");
        }
    }
}
