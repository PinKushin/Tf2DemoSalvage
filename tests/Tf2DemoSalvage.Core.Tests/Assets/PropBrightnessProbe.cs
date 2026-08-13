using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;
using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Assets;

public sealed class PropBrightnessProbe
{
    [Test]
    public void HowBrightAreTheDarkestPropsAfterOverbright()
    {
        string mapPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!File.Exists(mapPath))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        PakFile pak = PakFile.ReadFrom(map);
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(map);

        Dictionary<string, (double Sum, int Count)> byModel = [];

        for (int index = 0; index < props.Count; index++)
        {
            foreach (string vhv in StudioVertexLighting.PathsFor(index))
            {
                if (pak.ReadFile(vhv) is not { } file || file.Length < 8)
                {
                    continue;
                }

                int stamped = BitConverter.ToInt32(file, 4);

                foreach (IReadOnlyList<(byte Red, byte Green, byte Blue)> mesh in
                    StudioVertexLighting.Read(file, stamped))
                {
                    foreach ((byte red, byte green, byte blue) in mesh)
                    {
                        // Doubled, as the engine's vertex-lit shader does.
                        double value = Math.Min(
                            1d,
                            ((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255d * 2d);

                        (double sum, int count) = byModel.GetValueOrDefault(props[index].Model);
                        byModel[props[index].Model] = (sum + value, count + 1);
                    }
                }

                break;
            }
        }

        TestContext.Out.WriteLine("PROPLIGHT darkest models after the engine's overbright");

        foreach ((string model, (double sum, int count)) in byModel
            .Where(entry => entry.Value.Count > 200)
            .OrderBy(entry => entry.Value.Sum / entry.Value.Count)
            .Take(10))
        {
            TestContext.Out.WriteLine($"PROPLIGHT {sum / count:F3}  {count,7} verts  {model}");
        }

        double overall = byModel.Values.Sum(entry => entry.Sum) / byModel.Values.Sum(entry => entry.Count);

        TestContext.Out.WriteLine($"PROPLIGHT overall mean {overall:F3} across {byModel.Count} models");

        byModel.ShouldNotBeEmpty();
    }
}
