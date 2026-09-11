using System.IO;
using System.Threading.Tasks;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests that a demo opened from the playlist reads its map with ITS OWN timeline (B393).
/// </summary>
/// <remarks>
/// **The map read decides which demo content loads with the map.** `LoadedMap.Read` hands the
/// timeline to `MapAssets.Load` as three lists — the models, the worn items and the entity sprites —
/// and a null timeline gives two of them empty. `LoadDemoAsync` read the map before `Apply` had
/// assigned the new timeline, so it passed the PREVIOUS demo's, or none on the first open.
///
/// **The folder on the command line, not the file.** One file on the command line is opened by the
/// constructor through the synchronous path, which assigns the timeline first — so the async load
/// after it would find the right timeline already in the field and pass for the wrong reason.
///
/// **Needs the corpus demo and a Team Fortress 2 install**, and skips without either: the sprite's
/// material comes out of the game's archives. The demo's one sprite, `light_glow03.vmt` on six
/// `CSprite` entities, was found with the `props` probe; the model count from the same run is the
/// control that the probe was reading the right file.
/// </remarks>
/// <remarks>Serial, because this constructs a Windows Form — see B178.</remarks>
[NonParallelizable]
public sealed class PlaylistMapLoadTests
{
    /// <summary>The committed era specimen, whose map every install has.</summary>
    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", "tf2-2013-build1729296-pov-cp_badlands.dem"));

    /// <summary>The sprite the demo networks, as the demo names it.</summary>
    private const string DemoSprite = "materials/Sprites/light_glow03.vmt";

    [Test]
    public async Task LoadDemoAsync_TheFirstDemoOpened_ReadsItsMapWithItsOwnTimeline()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
        }

        GameInstall.RequireFile("maps/cp_badlands.bsp");

        using MainForm form = new(Path.GetDirectoryName(DemoPath)!);

        form.Demo.ShouldBeNull("a folder lists its demos and opens none, so no timeline is set yet");

        DemoLoadResult result = await form.LoadDemoAsync(DemoPath).ConfigureAwait(false);

        result.Loaded.ShouldBeTrue(result.Message);

        MapAssets assets = form.Loaded.ShouldNotBeNull("the map is installed")
            .Assets.ShouldNotBeNull("the install's archives are readable");

        assets.SpriteMaterials.Keys.ShouldContain(
            DemoSprite,
            "the map read must be handed the timeline being opened: a null one asks for no sprites");
    }
}
