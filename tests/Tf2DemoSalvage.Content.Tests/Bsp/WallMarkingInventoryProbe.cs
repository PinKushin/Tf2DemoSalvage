using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Every mechanism a map uses to put a marking on a wall, counted.
/// </summary>
/// <remarks>
/// **Written because "decal" was being used for all of them, and the loose word cost a real
/// lookup.** This project reads lump 45 — <c>doverlay_t</c>, what Hammer calls an
/// <c>info_overlay</c> — and then stores it in a field called <c>_decals</c>, draws it in
/// <c>DrawDecals</c>, and logs "222 decals placed". Reading that log led a session to Valve's DECAL
/// shaders (<c>DecalModulate_dx9.cpp</c>, <c>EnablePolyOffset( SHADER_POLYOFFSET_DECAL )</c>) as the
/// reference for how to draw them, and those are a different subsystem: an overlay's material is
/// ordinarily <c>LightmappedGeneric</c>, which sets neither a poly offset nor a depth-write
/// override.
///
/// Source has several ways to mark a surface and they are not interchangeable:
///
/// - <c>info_overlay</c> — lump 45, clipped to a named face list at compile time, drawn with the
///   material's own shader. This is what a team stripe is.
/// - <c>infodecal</c> — an entity in lump 0, projected onto whatever is behind it by the engine at
///   load, and drawn through the decal shaders with their poly offset.
/// - <c>info_overlay_transition</c> — the water variant, emitted by vbsp's
///   <c>OverlayTransition_EmitOverlayFace</c> into its own lump.
/// - an ordinary brush face painted with a decal-ish material, which is just geometry.
///
/// The count is the point: a mechanism with zero instances on the corpus map is one this project
/// need not implement, and one with hundreds is a gap worth knowing about.
/// </remarks>
public sealed class WallMarkingInventoryProbe
{
    /// <summary>The install's <c>maps</c>, or a path that cannot exist when there is no install.</summary>
    /// <remarks>
    /// Deliberately not skipping here: the test below reports which maps it managed to read, and a
    /// gate on the folder is how it says "none". <see cref="string.Empty"/> for the root makes the
    /// combined path relative, so <c>Directory.Exists</c> is false and that gate still fires.
    /// </remarks>
    private static string MapsFolder =>
        Path.Combine(GameInstall.Root ?? string.Empty, "maps");

    [Test]
    public void WallMarkings_OnTheCorpusMaps_AreCountedByMechanism()
    {
        if (!Directory.Exists(MapsFolder))
        {
            Assert.Ignore("Team Fortress 2 is not installed, so its maps cannot be read.");
            return;
        }

        int reported = 0;

        foreach (string name in new[] { "cp_process_f12", "cp_granary", "koth_viaduct" })
        {
            string path = Path.Combine(MapsFolder, name + ".bsp");

            if (!File.Exists(path))
            {
                continue;
            }

            byte[] bytes = File.ReadAllBytes(path);

            // Lump 45: what this project reads and draws.
            int overlays = BspOverlays.Read(bytes).Count;

            // Lump 0: everything else that marks a surface arrives as an entity.
            Dictionary<string, int> byClass = new(StringComparer.OrdinalIgnoreCase);

            foreach (BspEntity entity in BspEntities.ReadFrom(bytes))
            {
                if (!entity.TryGetValue("classname", out string className))
                {
                    continue;
                }

                if (className.Contains("decal", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("overlay", StringComparison.OrdinalIgnoreCase))
                {
                    byClass[className] = byClass.GetValueOrDefault(className) + 1;
                }
            }

            string entities = byClass.Count == 0
                ? "none"
                : string.Join(", ", byClass.OrderByDescending(pair => pair.Value)
                    .Select(pair => $"{pair.Value}x {pair.Key}"));

            TestContext.Out.WriteLine(
                $"MARKINGS {name}: lump 45 overlays {overlays}; surface-marking entities: {entities}");

            reported++;
        }

        reported.ShouldBeGreaterThan(0, "no corpus map was readable");
    }
}
