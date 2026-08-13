using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;

namespace Tf2DemoSalvage.Core.Tests.Assets;

public sealed class FindMaterialProbe
{
    [Test]
    public void WhereDoesTheHydroPipeMaterialLive()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        if (!Directory.Exists(tf))
        {
            Assert.Ignore("game missing");
            return;
        }

        foreach (string name in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
        {
            string path = Path.Combine(tf, name);

            if (!File.Exists(path))
            {
                continue;
            }

            VpkArchive archive = VpkArchive.Open(path);

            string[] hits = [.. archive.Paths
                .Where(entry => entry.Contains("PROPS_HYDRO", StringComparison.OrdinalIgnoreCase)
                    && entry.Contains("PIPE", StringComparison.OrdinalIgnoreCase))
                .Take(12)];

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

            foreach (string candidate in new[]
            {
                "materials/models/props_hydro/water_pipes.vmt",
                "materials/models/props_hydro/water_pipes.vtf",
                "materials/models/props_hydro/2pipe.vmt",
            })
            {
                TestContext.Out.WriteLine(
                    $"FIND pak {candidate}: {(pak.ReadFile(candidate) is null ? "absent" : "present")}");
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
