using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Bsp;

public sealed class ToolMaterialFlagsProbe
{
    [Test]
    public void ReportFlagsForEveryToolMaterial()
    {
        string path = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/maps/cp_process_final.bsp";
        string custom = "C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/maps/cp_process_f12.bsp";

        if (File.Exists(custom))
        {
            path = custom;
        }

        if (!File.Exists(path))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);
        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(map);

        Dictionary<string, (int Faces, SurfaceProperties Flags, int Visible)> byName = [];

        foreach (BspSurface surface in surfaces)
        {
            if (surface.MaterialIndex < 0 || surface.MaterialIndex >= materials.Count)
            {
                continue;
            }

            string name = materials[surface.MaterialIndex].Name;

            if (!name.Contains("TOOLS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (int faces, SurfaceProperties flags, int visible) = byName.GetValueOrDefault(name);

            byName[name] = (faces + 1, flags | surface.Flags, visible + (surface.IsVisible ? 1 : 0));
        }

        foreach ((string name, (int faces, SurfaceProperties flags, int visible)) in
            byName.OrderByDescending(entry => entry.Value.Faces))
        {
            TestContext.Out.WriteLine($"TOOL {faces,5} faces, {visible,5} visible, flags {flags}  {name}");
        }

        byName.ShouldNotBeEmpty();
    }
}
