using System;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What colour the setup gate's frame material actually decodes to — a MEASUREMENT.
/// </summary>
/// <remarks>
/// **The owner's report is about a COLOUR, and every instrument so far has answered about
/// geometry:** *"our issue is we are dropping or not drawing the yellow pipe frame"*, with
/// screenshots of TF2 showing a chainlink mesh crossed by orange pipe bars.
///
/// Everything measurable from the render log says the geometry is present:
///
/// <code>
///   drawing door_grate003_top.mdl: body 0, 1 parts, drawing 2 of 2 batches
///     — kept [960:opaque/textured@0+30, 961:opaque/textured@30+1200]
///   door_grate003_top draws materials: 960:opaque, 961:opaque
/// </code>
///
/// **1,200 of the model's 1,230 corners are the frame**, on material
/// <c>models/props_gameplay/door_grate001</c>; only 30 are the mesh panel on
/// <c>door_grate001_metalgrate</c>. So the orange bars are the bulk of the model, they are drawn,
/// and the model has a single skin family — which rules out the B229 skin-reference bug.
///
/// That leaves the material itself, and this asks the one question the logs cannot: **when this
/// project decodes that texture, what colour comes out.** A mean that is orange says the decode is
/// right and the fault is downstream in lighting or blending; a grey one says the fault is here.
///
/// **Numbers rather than a reference image, deliberately.** A specific visual property can be
/// asserted without one (`docs/memory/a-picture-is-assertable.md`), and a mean channel value is
/// exactly that kind of property: orange paint cannot have a blue channel above its red.
///
/// Reports numbers, asserts only that the walk ran (D38). Needs the game installed, so it skips on
/// CI.
/// </remarks>
[Explicit("Diagnostic: reports the decoded colour of the setup gate's materials.")]
public sealed class GateMaterialProbe
{
    /// <summary>Where the game is, on this machine.</summary>
    private const string Game =
        @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

    /// <summary>The gate's two materials: the frame, then the mesh panel.</summary>
    private static readonly string[] Materials =
    [
        "models/props_gameplay/door_grate001",
        "models/props_gameplay/door_grate001_metalgrate",
    ];

    [Test]
    public void Materials_TheSetupGateFrame_ReportTheirDecodedColour()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return;
        }

        GameArchives archives = GameArchives.Open(Game);

        foreach (string material in Materials)
        {
            byte[]? vmt = archives.Read($"materials/{material}.vmt");

            if (vmt is null)
            {
                TestContext.Out.WriteLine($"VMT {material}: NOT FOUND");
                continue;
            }

            // The VMT verbatim: what it declares is the other half of what it looks like, and a
            // `$color` or `$detail` there would change the answer without changing the texture.
            TestContext.Out.WriteLine(
                $"VMT {material}:\n{Encoding.UTF8.GetString(vmt).Trim()}");

            string basetexture = BaseTextureOf(Encoding.UTF8.GetString(vmt)) ?? material;

            byte[]? vtf = archives.Read($"materials/{basetexture}.vtf");

            if (vtf is null)
            {
                TestContext.Out.WriteLine($"VTF {basetexture}: NOT FOUND");
                continue;
            }

            VtfTexture texture = VtfTexture.Decode(vtf);

            TestContext.Out.WriteLine(
                $"VTF {basetexture}: {texture.Width}x{texture.Height}, "
                + $"{texture.Pixels.Length.ToString(CultureInfo.InvariantCulture)} bytes, "
                + $"mean {Mean(texture)}");
        }
    }

    /// <summary>The mean RGBA of a decoded texture, weighted only where alpha is non-zero.</summary>
    /// <remarks>
    /// **Alpha-weighted because the mesh panel is mostly holes.** An unweighted mean over an
    /// alpha-tested chainlink texture averages in whatever the transparent pixels happen to hold,
    /// which on a DXT5 image is usually black — and would report every gate material as dark
    /// regardless of its paint.
    /// </remarks>
    private static string Mean(VtfTexture texture)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        long counted = 0;

        byte[] pixels = texture.Pixels;

        for (int at = 0; at + 3 < pixels.Length; at += 4)
        {
            if (pixels[at + 3] == 0)
            {
                continue;
            }

            red += pixels[at];
            green += pixels[at + 1];
            blue += pixels[at + 2];
            counted++;
        }

        return counted == 0
            ? "every pixel transparent"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"R{red / counted} G{green / counted} B{blue / counted} over {counted} opaque pixels");
    }

    /// <summary>The <c>$basetexture</c> a VMT names, if it names one.</summary>
    private static string? BaseTextureOf(string vmt)
    {
        foreach (string line in vmt.Split('\n'))
        {
            int at = line.IndexOf("$basetexture", StringComparison.OrdinalIgnoreCase);

            if (at < 0)
            {
                continue;
            }

            string[] quoted = line[(at + "$basetexture".Length)..].Split('"');

            if (quoted.Length > 1 && quoted[1].Trim().Length > 0)
            {
                return quoted[1].Trim().Replace('\\', '/');
            }
        }

        return null;
    }
}
