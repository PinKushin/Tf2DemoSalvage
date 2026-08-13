using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class ControlPointProbe
{
    [Test]
    public void WhereAreTheControlPoints()
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

        foreach (BspEntity entity in BspEntities.ReadFrom(map))
        {
            if (!entity.TryGetValue("classname", out string classname))
            {
                continue;
            }

            if (!classname.Contains("control_point", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entity.TryGetValue("origin", out string origin);
            entity.TryGetValue("targetname", out string name);
            entity.TryGetValue("point_printname", out string printed);

            TestContext.Out.WriteLine($"POINT {classname} {name} {printed} at {origin}");
        }

        Assert.Pass();
    }
}
