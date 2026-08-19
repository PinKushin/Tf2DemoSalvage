using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Which materials cover the most surface area in a map, by name.
/// </summary>
/// <remarks>
/// **Written to settle what the black boxes over cp_process's last points are.** The category
/// view says they are ordinary world brush — present, drawn, not missing and not displacement —
/// and no surface in the map is unlit. That leaves the texture itself, and the obvious candidate
/// is a material that is simply black: mappers cap rooms with one so the sky does not show
/// through from inside, and the player never sees the outside of it.
///
/// If that is what these are, the viewer is drawing them correctly and the defect is only that an
/// overhead camera stands somewhere no player ever does.
/// </remarks>
public sealed class BlackMaterialProbe
{
    [Test]
    [Explicit("Diagnostic. Lists the materials covering the most faces.")]
    public void MaterialCoverage_TheMap_IsReported()
    {
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage",
            "maps",
            "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore($"No map at {map}; open a demo in the viewer first.");
            return;
        }

        byte[] file = File.ReadAllBytes(map);

        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(file);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(file);

        Dictionary<string, int> faces = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (float Red, float Green, float Blue)> reflectivity =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsVisible ||
                surface.MaterialIndex < 0 ||
                surface.MaterialIndex >= materials.Count)
            {
                continue;
            }

            BspMaterial material = materials[surface.MaterialIndex];

            faces[material.Name] = faces.GetValueOrDefault(material.Name) + 1;
            reflectivity[material.Name] = material.Reflectivity;
        }

        foreach ((string name, int count) in faces.OrderByDescending(entry => entry.Value).Take(12))
        {
            (float red, float green, float blue) = reflectivity[name];

            TestContext.Out.WriteLine($"MATERIAL {count,6}  {red:F3} {green:F3} {blue:F3}  {name}");
        }

        // **Reflectivity is vbsp's own average of the texture**, so a material that is actually
        // black says so here without anything having to decode a VTF. Anything under a few
        // percent is black to the eye.
        foreach ((string name, int count) in faces
            .Where(entry => Sum(reflectivity[entry.Key]) < 0.05f)
            .OrderByDescending(entry => entry.Value)
            .Take(12))
        {
            (float red, float green, float blue) = reflectivity[name];

            TestContext.Out.WriteLine($"DARK {count,6}  {red:F3} {green:F3} {blue:F3}  {name}");
        }

        // **Which way the black faces point, because the name alone is not the answer.** Skipping
        // toolsblack by material was tried once and reverted: it is genuinely drawn behind windows,
        // under grates and inside vents, and removing it left millions of square units showing the
        // background through. A lid over a room is a different thing from a vent wall, and the
        // difference is orientation rather than material.
        int up = 0;
        int down = 0;
        int side = 0;

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsVisible ||
                surface.MaterialIndex < 0 ||
                surface.MaterialIndex >= materials.Count ||
                !materials[surface.MaterialIndex].Name.Contains(
                    "toolsblack", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (surface.Normal.Z > 0.7f)
            {
                up++;
            }
            else if (surface.Normal.Z < -0.7f)
            {
                down++;
            }
            else
            {
                side++;
            }
        }

        TestContext.Out.WriteLine($"TOOLSBLACK up {up}, down {down}, vertical {side}");

        faces.Count.ShouldBeGreaterThan(0);
    }

    private static float Sum((float Red, float Green, float Blue) colour) =>
        colour.Red + colour.Green + colour.Blue;
}
