using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class BrushModelProbe
{
    [Test]
    public void BrushModels_TheirOwnOrigin_IsReported()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        BspHeader header = BspHeader.Parse(map.Span);
        ReadOnlySpan<byte> models = BspLumpData.ReadStructures(map, header.Lump(14), 48, "models").Span;

        int count = models.Length / 48;
        int moved = 0;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> model = models.Slice(index * 48, 48);

            float x = BitConverter.ToSingle(model.Slice(24, 4));
            float y = BitConverter.ToSingle(model.Slice(28, 4));
            float z = BitConverter.ToSingle(model.Slice(32, 4));
            int firstFace = BitConverter.ToInt32(model.Slice(40, 4));
            int faces = BitConverter.ToInt32(model.Slice(44, 4));

            if (x != 0f || y != 0f || z != 0f)
            {
                moved++;

                if (moved <= 5)
                {
                    TestContext.Out.WriteLine(
                        $"MODEL {index} origin ({x:F0},{y:F0},{z:F0}) faces {firstFace}..{firstFace + faces}");
                }
            }
        }

        TestContext.Out.WriteLine($"MODEL {count} models, {moved} with a non-zero origin");

        // And how many entities name a brush model at all.
        int brushEntities = BspEntities.ReadFrom(map)
            .Count(entity => entity.TryGetValue("model", out string model) && model.StartsWith('*'));

        TestContext.Out.WriteLine($"MODEL {brushEntities} entities reference a brush model");

        count.ShouldBeGreaterThan(0);
    }
}
