using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Assets;

public sealed class StaleLightingProbe
{
    [Test]
    public void ReportPropsWhoseBakedLightingDoesNotMatchTheirModel()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
        string mapPath = Path.Combine(tf, "maps", "cp_process_final.bsp");

        if (!File.Exists(mapPath))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(map);
        VpkArchive archive = VpkArchive.Open(Path.Combine(tf, "tf2_misc_dir.vpk"));

        Dictionary<string, (int Stale, int Total, int Stamped, int Actual)> byModel = [];

        for (int index = 0; index < props.Count; index++)
        {
            BspStaticProp prop = props[index];

            if (archive.ReadFile(prop.Model) is not { } modelFile)
            {
                continue;
            }

            int actual;

            try
            {
                actual = StudioModel.Read(modelFile).Checksum;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            foreach (string vhv in StudioVertexLighting.PathsFor(index))
            {
                if (pak.ReadFile(vhv) is not { } file || file.Length < 8)
                {
                    continue;
                }

                int stamped = BitConverter.ToInt32(file, 4);
                (int stale, int total, int _, int _) = byModel.GetValueOrDefault(prop.Model);

                byModel[prop.Model] = (
                    stale + (stamped == actual ? 0 : 1), total + 1, stamped, actual);

                break;
            }
        }

        foreach ((string model, (int stale, int total, int stamped, int actual)) in
            byModel.Where(entry => entry.Value.Stale > 0))
        {
            TestContext.Out.WriteLine(
                $"STALE {model}: {stale} of {total} placements, map says {stamped}, model is {actual}");
        }

        TestContext.Out.WriteLine(
            $"STALE {byModel.Values.Sum(entry => entry.Stale)} stale of {byModel.Values.Sum(entry => entry.Total)} placements with lighting, across {byModel.Count} models");

        byModel.ShouldNotBeEmpty();
    }
}
