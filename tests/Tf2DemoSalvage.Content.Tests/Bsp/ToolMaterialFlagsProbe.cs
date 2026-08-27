using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class ToolMaterialFlagsProbe
{
    [Test]
    public void ToolMaterialFlags_EveryToolMaterial_IsReported()
    {
        // The authored f12 map wins when it is there, because it carries tool materials the stock
        // compile does not. The repository path is derived rather than typed: it used to be an
        // absolute path under one user's home, which no other machine could ever satisfy.
        string custom = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..",
            "tools", "corpus", "local", "maps", "cp_process_f12.bsp"));

        string path = File.Exists(custom)
            ? custom
            : GameInstall.RequireFile("maps/cp_process_final.bsp");

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
