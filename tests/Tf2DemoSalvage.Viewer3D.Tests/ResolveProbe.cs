using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

public sealed class ResolveProbe
{
    [Test]
    public void SearchPath_TheHydroPipeMaterial_IsReported()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        if (!Directory.Exists(tf))
        {
            Assert.Ignore("game missing");
            return;
        }

        GameArchives archives = GameArchives.Open(tf);

        TestContext.Out.WriteLine($"RESOLVE folders={archives.FolderCount} empty={archives.IsEmpty}");

        foreach (string path in new[]
        {
            "materials/models/props_hydro/2pipe.vmt",
            "materials/models/props_hydro/water_pipes.vmt",
            "materials/models/props_hydro/2pipe.vtf",
            "materials/models/props_hydro/water_pipes.vtf",
            "materials/concrete/concretewall012.vmt",
        })
        {
            byte[]? found = archives.Read(path);

            TestContext.Out.WriteLine(
                $"RESOLVE {path}: {(found is null ? "MISSING" : found.Length + " bytes")}");
        }

        // The VMT and VTF both resolve, so any failure is in decoding. Report exactly why.
        foreach (string name in new[] { "2pipe", "water_pipes" })
        {
            byte[]? vmt = archives.Read($"materials/models/props_hydro/{name}.vmt");
            byte[]? vtf = archives.Read($"materials/models/props_hydro/{name}.vtf");

            if (vmt is not null)
            {
                string text = System.Text.Encoding.UTF8.GetString(vmt);
                string flat = text[..Math.Min(200, text.Length)].Replace((char)10, ' ').Replace((char)13, ' ');
                TestContext.Out.WriteLine("RESOLVE " + name + ".vmt says: " + flat);
            }

            if (vtf is null)
            {
                continue;
            }

            try
            {
                Tf2DemoSalvage.Content.Assets.VtfTexture decoded =
                    Tf2DemoSalvage.Content.Assets.VtfTexture.Decode(vtf, 0);

                TestContext.Out.WriteLine(
                    $"RESOLVE {name}.vtf decoded {decoded.Width}x{decoded.Height}");
            }
            catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or ArgumentException)
            {
                TestContext.Out.WriteLine(
                    $"RESOLVE {name}.vtf FAILED {failure.GetType().Name}: {failure.Message}");
            }
        }

        Assert.Pass();
    }
}
