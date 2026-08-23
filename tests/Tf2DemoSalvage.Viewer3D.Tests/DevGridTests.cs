using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The category view draws Valve's measurement grid, and it has to be a grid.
/// </summary>
/// <remarks>
/// **The owner's reason for using Valve's texture rather than one generated here**: "if our
/// placeholders match valves, and our colors match valves then things become easily compared and
/// you only have one legend to remember". A capture from this viewer and a shot of the same place
/// in Hammer then read the same way.
///
/// **Valve's dev set is not one colour**, which is the trap this guards. Half-Life 2 ships
/// twenty-four measure textures and TF2 adds `blu` and `red` variants, so the first attempt —
/// multiplying the category tint by the grid's own colour — dragged every category toward orange,
/// because `dev_measuregeneric01` is orange. The shader now takes luminance only. That makes the
/// result independent of which candidate an install happens to resolve, and it is why this test
/// asserts VARIATION rather than any particular hue.
/// </remarks>
public sealed class DevGridTests
{
    [Test]
    public void DevGrid_LoadedFromTheGame_CarriesStructureRatherThanOneColour()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
        string map = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!Directory.Exists(tf) || !File.Exists(map))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        MapAssets assets = MapAssets.Load(
            File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);

        // **Not null**, because the fallback chain has three candidates and TF2 mounts Half-Life 2's
        // archives after its own — so an install that can open a map can reach at least one.
        assets.DevGrid.ShouldNotBeNull(
            "no dev measurement texture resolved, so the category view has no grid to draw");

        MapTexture grid = assets.DevGrid.Value;

        grid.Width.ShouldBeGreaterThan(8);
        grid.Height.ShouldBeGreaterThan(8);

        byte[] pixels = grid.Image.ToRgba(grid.Width, grid.Height);

        // **The whole point is the printed lines and dimensions.** A solid texture would satisfy
        // "a texture loaded" and give exactly the flat colours this replaced — the shader would
        // multiply the tint by a constant and nothing would be gained. So the assertion is that the
        // image varies, measured on luminance because the shader reads luminance.
        HashSet<int> tones = [];

        for (int at = 0; at + 2 < pixels.Length; at += 4)
        {
            // Rec.601, matching the shader, and to whole luminance units. The first version of
            // this bucketed to eights and reported four tones for a texture that has far more —
            // an instrument too coarse to see the thing it was measuring, which is the failure
            // this project keeps meeting from the other direction.
            tones.Add(((pixels[at] * 299) + (pixels[at + 1] * 587) + (pixels[at + 2] * 114)) / 1000);
        }

        TestContext.Out.WriteLine($"DEV GRID {grid.Width}x{grid.Height}, {tones.Count} tones");

        // **Three, and the number comes from the measurement rather than from an expectation.**
        // `dev_measuregeneric01` has SEVEN distinct luminance levels at 128x128: it is flat-shaded
        // — a background, its grid lines, and the printed dimensions — not a photograph. A floor of
        // sixteen was written first, from a guess about what "a texture" looks like, and it failed
        // against a perfectly good grid.
        //
        // Three leaves margin against a different candidate resolving on another install while
        // still failing hard on the case that matters: a solid texture gives exactly one.
        tones.Count.ShouldBeGreaterThan(
            3, "the dev texture is nearly one tone, so it cannot carry a grid");
    }
}
