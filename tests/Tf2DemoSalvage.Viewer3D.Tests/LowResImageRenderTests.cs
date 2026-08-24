using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// <c>mat_showlowresimage</c> draws the material from its own VTF thumbnail.
/// </summary>
/// <remarks>
/// **The last of B153's debug draws, and the only one that needed the ASSET rather than a shader
/// branch.** Every VTF stores a tiny copy of itself between the header and the mip chain; this
/// reader had always skipped past it correctly and never kept it, so the mode had nothing to draw
/// until `VtfLowResolutionConformanceTests` and the retention behind it landed.
///
/// **This test exists because the component tests cannot fail when the wiring is absent.** The
/// thumbnail decoding is covered in Content, the flag reaching the constant buffer is trivially
/// true, and neither says whether a pixel on screen changed. This project has shipped three no-ops
/// with a green suite in one session for exactly that gap — see
/// `docs/memory/output-level-assertion-or-it-is-not-done.md` — so the assertion is on the rendered
/// frame.
///
/// **Asserting a DIFFERENCE rather than a colour, deliberately.** Which thumbnail a material has is
/// a fact about the shipped texture, and pinning its exact RGB would be a change-detector that
/// reddens whenever Valve reships an asset. What must be true is that the substitution happened at
/// all: with the mode on, the surface is drawn from a 16x16 image instead of a 2048x2048 one, and
/// those cannot agree by construction on a textured wall.
/// </remarks>
public sealed class LowResImageRenderTests
{
    private static MapAssets? Assets
    {
        get
        {
            string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
            string map = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            return Directory.Exists(tf) && File.Exists(map)
                ? MapAssets.Load(
                    File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256)
                : null;
        }
    }

    [Test]
    public void ShowLowResImage_OnAMaterialWithAThumbnail_DrawsADifferentPicture()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // **A material that actually carries a thumbnail, found rather than assumed.** Picking one
        // by name and hoping would make an absent thumbnail look like a broken substitution, which
        // is the failure this whole session kept meeting from the other side.
        int material = -1;

        for (int index = 0; index < assets.Textures.Count; index++)
        {
            if (assets.Textures[index] is { Thumbnail: not null })
            {
                material = index;
                break;
            }
        }

        if (material < 0)
        {
            Assert.Ignore("no loaded material carries a thumbnail");
            return;
        }

        (int Red, int Green, int Blue) Draw(bool lowRes)
        {
            (List<WorldVertex> wall, WorldBatch batch) = Quad(material);

            target.Clear(0f, 0f, 0f);
            target.DrawWorld(
                wall, [batch], Identity, assets,
                debug: new DebugModes(ShowLowResImage: lowRes));

            return target.PixelAt(32, 32);
        }

        (int Red, int Green, int Blue) textured = Draw(lowRes: false);
        (int Red, int Green, int Blue) thumbnail = Draw(lowRes: true);

        TestContext.Out.WriteLine(
            $"LOWRES material {material}: textured {textured} / thumbnail {thumbnail}");

        // **The control comes first**: a black frame would satisfy "the two differ" while meaning
        // the draw failed entirely, and that is indistinguishable from a working substitution if
        // only the difference is asserted.
        (textured.Red + textured.Green + textured.Blue).ShouldBeGreaterThan(
            0, "the ordinary draw produced a black pixel, so nothing was drawn to compare against");

        thumbnail.ShouldNotBe(
            textured,
            "mat_showlowresimage did not change the picture, so the thumbnail is not reaching the " +
            "shader — the flag can be set and the texture unbound and nothing would say so");
    }

    /// <summary>A wall filling the view, drawn with one material.</summary>
    /// <remarks>
    /// The texture coordinate spans the whole quad so the sample is well inside the image rather
    /// than on a seam, and the lightmap coordinate avoids the atlas's reserved white texel for the
    /// reason `FullbrightRenderTests` records: that corner is already fully lit, so a substitution
    /// touching lighting would be invisible there.
    /// </remarks>
    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(int material)
    {
        const float depth = 0.9f;
        const float lit = 0.5f;

        List<WorldVertex> vertices =
        [
            new(-1f, -1f, depth, 0f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, 1f, depth, 1f, 1f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, -1f, depth, 1f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(-1f, -1f, depth, 0f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(-1f, 1f, depth, 0f, 1f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, 1f, depth, 1f, 1f, lit, lit, 0f, 1f, 1f, 1f),
        ];

        return (vertices, new WorldBatch(material, 0, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
