using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

public sealed class FindMaterialProbe
{
    [Test]
    public void HydroPipeMaterial_ItsLocation_IsReported()
    {
        string tf = GameInstall.Require();

        foreach (string name in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
        {
            string path = Path.Combine(tf, name);

            if (!File.Exists(path))
            {
                continue;
            }

            VpkArchive archive = VpkArchive.Open(path);

            string[] hits = [.. archive.Paths
                .Where(entry => entry.Contains("PLAYER/SPY/EYE", StringComparison.OrdinalIgnoreCase))
                .Take(20)];

            TestContext.Out.WriteLine($"FIND {name}: {hits.Length} hits");

            foreach (string hit in hits)
            {
                TestContext.Out.WriteLine("FIND   " + hit);
            }
        }

        // And the map's own pakfile, which is searched before the archives.
        string mapPath = Path.Combine(tf, "maps", "cp_process_final.bsp");

        if (File.Exists(mapPath))
        {
            PakFile pak = PakFile.ReadFrom(File.ReadAllBytes(mapPath));

            VpkArchive[] all =
            [
                .. new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" }
                    .Select(n => Path.Combine(tf, n))
                    .Where(File.Exists)
                    .Select(VpkArchive.Open),
            ];

            foreach (string candidate in new[]
            {
                "materials/models/player/scout/eyeball_l.vmt",
                "materials/models/player/scout/eyeball_r.vmt",
                "materials/models/player/scout/scout_head.vmt",
                "materials/models/player/spy/eyeball_l.vmt",
            })
            {
                string where = pak.ReadFile(candidate) is not null ? "map pak" : "absent from pak";

                foreach (VpkArchive archive in all)
                {
                    if (archive.ReadFile(candidate) is not null)
                    {
                        where += ", FOUND in an archive";
                        break;
                    }
                }

                TestContext.Out.WriteLine($"FIND {candidate}: {where}");

                foreach (VpkArchive archive in all)
                {
                    if (archive.ReadFile(candidate) is { } vmt)
                    {
                        TestContext.Out.WriteLine(
                            "FINDVMT wrote " + candidate);
                        File.WriteAllBytes(
                            Path.Combine(Path.GetTempPath(), "vmtdump.txt"), vmt);
                        break;
                    }
                }
            }
        }

        // Every VMT anywhere under props_hydro, in either archive.
        foreach (string name in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
        {
            string path = Path.Combine(tf, name);

            if (!File.Exists(path))
            {
                continue;
            }

            int vmts = VpkArchive.Open(path).Paths
                .Count(entry => entry.Contains("PROPS_HYDRO", StringComparison.OrdinalIgnoreCase)
                    && entry.EndsWith(".VMT", StringComparison.OrdinalIgnoreCase));

            TestContext.Out.WriteLine($"FIND {name}: {vmts} VMTs under props_hydro");

            foreach (string entry in VpkArchive.Open(path).Paths
                .Where(entry => entry.Contains("PROPS_HYDRO", StringComparison.OrdinalIgnoreCase)
                    && entry.EndsWith(".VMT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .Take(10))
            {
                TestContext.Out.WriteLine("FINDVMT " + entry);
            }
        }

        Assert.Pass();
    }
}
