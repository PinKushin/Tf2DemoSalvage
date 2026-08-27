using System;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What a wall stripe's material actually says, read out of the game's own archives.
/// </summary>
/// <remarks>
/// **The question this answers, cheaply, before any more decompiling.** An `info_overlay`'s material
/// is ordinarily `LightmappedGeneric`, and that shader sets neither `EnablePolyOffset` nor
/// `EnableDepthWrites(false)` — only the `decal*` shaders do. So how an overlay avoids fighting the
/// surface it lies on has to come from somewhere else, and the two candidates are the engine's
/// `COverlayMgr::RenderOverlays` (closed, in `engine\Overlay.cpp`) or **the material itself**.
///
/// `MATERIAL_VAR_DECAL` is a material flag, set by `$decal` in a VMT, and the material system —
/// not the shader — is what acts on it. If cp_process's stripe materials carry it, the poly offset
/// is explained without reading a line of disassembly, and this project should honour the same flag.
/// If they do not, the engine is doing something in the overlay path and the decompiler is the next
/// step rather than the first.
///
/// See `docs/memory/shipped-data-is-a-source.md`: VMTs have twice answered questions filed as needing
/// a decompiler, most notably `$modblend`, which turned out to be dead.
/// </remarks>
public sealed class OverlayMaterialProbe
{
    private static string GameFolder => GameInstall.Require();

    [Test]
    public void OverlayMaterials_TheWallStripes_AreReportedInFull()
    {
        if (!Directory.Exists(GameFolder))
        {
            Assert.Ignore("Team Fortress 2 is not installed.");
            return;
        }

        string[] wanted =
        [
            "materials/concrete/stripe_blue.vmt",
            "materials/overlays/stripe_red.vmt",
            "materials/signs/team_blue.vmt",
        ];

        int found = 0;

        foreach (string archiveName in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
        {
            string path = Path.Combine(GameFolder, archiveName);

            if (!File.Exists(path))
            {
                continue;
            }

            VpkArchive archive = VpkArchive.Open(path);

            foreach (string entry in archive.Paths)
            {
                if (!wanted.Any(name => entry.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                byte[]? bytes = archive.ReadFile(entry);

                if (bytes is null)
                {
                    continue;
                }

                found++;

                TestContext.Out.WriteLine($"VMT ==== {entry} ({archiveName})");

                foreach (string line in Encoding.UTF8.GetString(bytes)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    TestContext.Out.WriteLine("VMT   " + line.TrimEnd('\r').Trim());
                }
            }
        }

        // The control: finding none would print nothing and prove nothing, which is how an absence
        // gets recorded as a fact about the format rather than about the search.
        found.ShouldBeGreaterThan(0, "no stripe material was found in the archives");
    }
}
