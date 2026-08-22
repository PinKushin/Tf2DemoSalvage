using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which materials the map draws nearly black, and how much of it they cover.
/// </summary>
/// <remarks>
/// **Written because the picture could not be inspected.** Dark patches survived three explanations
/// - unlit faces, missing terrain, unlit props - and the last was falsified by lighting the props
/// and watching the patches not move. Rather than guess a fourth, this measures the thing the eye
/// was being asked about: the mean brightness of every material actually drawn, weighted by how
/// much screen it covers.
///
/// A blob is a large area of near-black output, so the material responsible must be both dark and
/// well covered. That is a property of the DATA, computable with no display attached, and it names
/// a culprit rather than ranking suspicions.
///
/// **It found nothing, and the way it misled is worth keeping.** The darkest drawn materials on
/// cp_process_final include props_ui/competitive_stage* - the main menu backdrop - and
/// props_island/island_plants01, tropical foliage. Both look absurd on a 2013 industrial map, and
/// the obvious reading was that material resolution had gone wrong somewhere.
///
/// It had not. **Mappers reuse assets from anywhere in the game**, and a crate modelled for the
/// competitive menu is just a crate. The owner named the pattern immediately - the dog loaf on
/// viaduct_pro is the same joke - and "this texture does not belong on this map" turned out to be
/// a statement about the reader's expectations rather than about the data.
///
/// So the tool stays and its conclusion does not. What it can honestly report is which materials
/// are dark and how much they cover; what it cannot do is tell a bug from an art choice, and any
/// list it produces has to be read by someone who knows the map.
/// </remarks>
public sealed class DarkMaterialsDiagnostic
{
    private static string? MapFile
    {
        get
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("TF2_FOLDER"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string map = Path.Combine(root, "maps", "cp_process_final.bsp");

                if (File.Exists(map))
                {
                    return map;
                }
            }

            return null;
        }
    }

    [Test]
    public void DarkestMaterials_ByArea_AreReported()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run these.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        GameArchives archives = GameArchives.Open(Path.GetDirectoryName(Path.GetDirectoryName(path)));
        MapAssets assets = MapAssets.Load(map, archives, 0);

        List<(string Name, double Luminance, long Pixels)> report = [];

        for (int index = 0; index < assets.Materials.Count; index++)
        {
            if (index >= assets.Textures.Count || assets.Textures[index] is not { } texture)
            {
                report.Add((assets.Materials[index].Name, -1d, 0));
                continue;
            }

            ReadOnlySpan<byte> pixels = texture.Pixels.Span;
            double total = 0;
            int counted = 0;

            // Every sixteenth pixel: enough to characterise a texture's brightness and fast enough
            // to run over two hundred of them.
            for (int at = 0; at + 3 < pixels.Length; at += 64)
            {
                total += (0.2126 * pixels[at]) + (0.7152 * pixels[at + 1]) + (0.0722 * pixels[at + 2]);
                counted++;
            }

            report.Add((
                assets.Materials[index].Name,
                counted == 0 ? -1d : total / counted / 255d,
                (long)texture.Width * texture.Height));
        }

        TestContext.Out.WriteLine("=== materials with no texture (drawn white or skipped) ===");

        foreach ((string name, _, _) in report.Where(entry => entry.Luminance < 0))
        {
            TestContext.Out.WriteLine("  MISSING " + name);
        }

        TestContext.Out.WriteLine("=== darkest materials that resolved ===");

        foreach ((string name, double luminance, long pixels) in report
            .Where(entry => entry.Luminance >= 0)
            .OrderBy(entry => entry.Luminance)
            .Take(15))
        {
            TestContext.Out.WriteLine(
                $"  {luminance:F3}  {pixels,9:N0}px  {name}");
        }

        report.ShouldNotBeEmpty();
    }
}
