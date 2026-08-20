using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Do the viewmodel arms models exist, and do they carry geometry?</summary>
public sealed class ArmsModelProbe
{
    [Test]
    [Explicit("diagnostic")]
    public void ArmsModels_InTheArchives_AreReported()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        List<VpkArchive> archives = [.. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(tf, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        if (archives.Count == 0)
        {
            Assert.Ignore("not installed");
            return;
        }

        byte[]? Find(string path)
        {
            byte[]? found = null;
            foreach (VpkArchive archive in archives)
            {
                found ??= archive.ReadFile(path);
            }
            return found;
        }

        foreach (string name in (string[])["c_demo_arms", "c_sniper_arms", "c_pyro_arms", "c_scout_arms"])
        {
            string mdl = $"models/weapons/c_models/{name}.mdl";
            string vtx = $"models/weapons/c_models/{name}.dx90.vtx";
            string vvd = $"models/weapons/c_models/{name}.vvd";

            TestContext.Out.WriteLine(
                $"{name}: mdl {Find(mdl)?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "MISSING"}, " +
                $"vtx {Find(vtx)?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "MISSING"}, " +
                $"vvd {Find(vvd)?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "MISSING"}");
        }

        archives.Count.ShouldBeGreaterThan(0);
    }
}
