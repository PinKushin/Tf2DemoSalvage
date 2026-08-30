using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What a stock water material actually declares.
/// </summary>
/// <remarks>
/// **Read the game's shipped DATA, which CLAUDE.md lists as a source and which is easy to forget
/// because it is not code.** `water/water_well_beneath` is the one material on `cp_fulgur` this
/// project draws as the magenta chequer, and the owner's report is decisive: *"the real tf2 doesnt
/// show the purple and black texture anywhere on this map, its not a new map"*.
///
/// The inventory said "MISSING 1 with no base texture resolved", which is B62 — *a material can name
/// no `$basetexture` at all*. A water shader does not need one: it refracts and reflects. Whether
/// that is what this VMT says is a question about a file Valve ships, so this reads it rather than
/// reasoning about it.
///
/// Explicit: it reports, and needs the game installed.
/// </remarks>
[Explicit("Probe: prints a stock water VMT.")]
public sealed class WaterMaterialProbe
{
    [Test]
    public void WaterWellBeneath_AsShipped_IsPrinted()
    {
        if (GameInstall.Root is not { } game)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        foreach (string path in new[]
        {
            "materials/water/water_well_beneath.vmt",
            "materials/water/water_well.vmt",
        })
        {
            byte[]? file = GameArchives.Open(game).Read(path);

            TestContext.Out.WriteLine($"=== {path}: {(file is null ? "NOT FOUND" : $"{file.Length} bytes")}");

            if (file is not null)
            {
                TestContext.Out.WriteLine(Encoding.UTF8.GetString(file));
            }
        }

        Assert.Pass("printed");
    }
}
