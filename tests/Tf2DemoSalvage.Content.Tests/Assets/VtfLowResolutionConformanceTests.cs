using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The thumbnail every VTF carries, which <c>mat_showlowresimage</c> draws instead of the texture.
/// </summary>
/// <remarks>
/// **A VTF stores a tiny copy of itself ahead of the mip chain.** The header declares it in three
/// fields — <c>lowResImageFormat</c>, <c>lowResImageWidth</c>, <c>lowResImageHeight</c>
/// (<c>vtf.h:485-487</c>) — and the data sits between the header and the images, which is why a
/// reader that ignores it lands in the wrong place for every image after it.
///
/// This project's reader has always skipped it correctly and never kept it:
///
/// <code>
/// if (lowResFormat is not VtfFormat.None &amp;&amp; lowResWidth > 0 &amp;&amp; lowResHeight > 0)
/// {
///     at += SizeOf(VtfFormat.Dxt1, lowResWidth, lowResHeight);
/// }
/// </code>
///
/// **`mat_showlowresimage` is the last of B153's debug draws, and it is the one that needed data
/// rather than a shader branch.** It is a retail cvar, not a Hammer facility — the string is in
/// the shipped `materialsystem.dll`, measured alongside `mat_drawflat` and `mat_normalmaps` (D79).
///
/// The claim under test is the one the skip already encodes and nothing has ever checked: that the
/// thumbnail is **always DXT1**, whatever the texture's own format is. If that were false the skip
/// would be the wrong size and every mip after it would be misread — so this is a load-bearing
/// assumption that has been carried on a comment.
/// </remarks>
public sealed class VtfLowResolutionConformanceTests
{
    private static string Game => GameInstall.Require();

    private static GameArchives? Archives()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return null;
        }

        return GameArchives.Open(Game);
    }

    /// <summary>A handful of shipped textures, chosen to span formats rather than subjects.</summary>
    private static readonly string[] Textures =
    [
        "materials/models/player/soldier/soldier_red.vtf",
        "materials/models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.vtf",
        "materials/concrete/concretefloor001a.vtf",
        "materials/metal/metalwall001a.vtf",
        "materials/models/player/scout/scout_red.vtf",
    ];

    [Test]
    public void Read_TheLowResolutionThumbnail_IsAlwaysDxt1()
    {
        if (Archives() is not { } archives)
        {
            return;
        }

        List<string> checkedFiles = [];

        foreach (string path in Textures)
        {
            if (archives.Read(path) is not { } file)
            {
                continue;
            }

            VtfTexture texture = VtfTexture.Read(file);

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: {texture.Width}x{texture.Height} {texture.Format}, " +
                $"thumbnail {texture.LowResolutionWidth}x{texture.LowResolutionHeight} " +
                $"{texture.LowResolutionFormat}");

            checkedFiles.Add(path);

            if (texture.LowResolutionFormat is VtfFormat.None)
            {
                continue;
            }

            // **The load-bearing claim.** The skip is sized as DXT1 unconditionally, so a thumbnail
            // in any other format would put every mip after it at the wrong offset — and the
            // symptom would be a picture assembled from the wrong bytes rather than an error.
            texture.LowResolutionFormat.ShouldBe(
                VtfFormat.Dxt1,
                $"{path} declares a thumbnail that is not DXT1, which the reader's skip assumes");
        }

        // **A control on the search itself.** Zero files read would pass every assertion above
        // while measuring nothing, which is this project's most repeated instrument failure.
        checkedFiles.ShouldNotBeEmpty("none of the sampled textures were found in this install");
    }

    [Test]
    public void Read_TheLowResolutionThumbnail_IsRetainedForDrawing()
    {
        if (Archives() is not { } archives)
        {
            return;
        }

        string path = Textures.FirstOrDefault(name => archives.Read(name) is not null)
            ?? string.Empty;

        if (path.Length == 0 || archives.Read(path) is not { } file)
        {
            Assert.Ignore("none of the sampled textures were found in this install");
            return;
        }

        VtfTexture texture = VtfTexture.Read(file);

        if (texture.LowResolutionFormat is VtfFormat.None)
        {
            Assert.Ignore($"{path} carries no thumbnail");
            return;
        }

        // **Decoded, not raw**, because the drawing path takes RGBA like every other texture here.
        // Four bytes a pixel at the declared size is an exact prediction: a reader that returned
        // the compressed block bytes, or the wrong mip, gives a different number.
        texture.LowResolutionPixels.Length.ShouldBe(
            texture.LowResolutionWidth * texture.LowResolutionHeight * 4,
            "the thumbnail should be decoded to RGBA at its declared size");
    }
}
