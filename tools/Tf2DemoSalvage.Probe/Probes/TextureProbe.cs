using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One shipped VTF as the reader sees it, frames included (B338).
/// </summary>
/// <remarks>
/// **`vmt` cannot reach these**, and the texture that matters most is one of them: TF2's fire
/// overlay is `effects/tiledfire/fireLayeredSlowTiled512.vtf`, referenced straight from `$detail`
/// with no material of its own. 6,735 of the 7,027 materials running `AnimatedTexture` animate that
/// one file.
///
/// **It prints per FRAME**, which is the whole point: a sixteen-frame sheet and a still texture are
/// the same line otherwise, and the mean colour per frame is what says the frames actually differ —
/// a reader whose frame offset was wrong would return frame 0 every time and print sixteen
/// identical rows.
///
/// <code>
///   vtf effects/tiledfire/fireLayeredSlowTiled512
/// </code>
/// </remarks>
public sealed class TextureProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vtf";

    /// <inheritdoc/>
    public string Summary => "one shipped texture, frame by frame: vtf <path>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("Give a texture: vtf materials/effects/tiledfire/…");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        string path = arguments[0];

        if (!path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            path = "materials/" + path;
        }

        if (!path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
        {
            path += ".vtf";
        }

        if (game.Archives.Read(path) is not { } bytes)
        {
            output.WriteLine($"'{path}' is not in the game's content.");
            return;
        }

        VtfTexture first = VtfTexture.Decode(bytes);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{path}: {first.Width}x{first.Height}, {first.Format}, {first.MipCount} mips, " +
            $"{first.FrameCount} frames"));

        if (first.FrameCount <= 1)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("  frame   mean R  mean G  mean B  mean A");

        for (int frame = 0; frame < first.FrameCount; frame++)
        {
            VtfTexture image = VtfTexture.Decode(bytes, maximumSize: 0, face: 0, frame: frame);

            (double red, double green, double blue, double alpha) = Mean(image);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {frame,5}   {red,6:0.0}  {green,6:0.0}  {blue,6:0.0}  {alpha,6:0.0}"));
        }
    }

    /// <summary>The mean of each channel, which is what distinguishes one frame from another.</summary>
    private static (double Red, double Green, double Blue, double Alpha) Mean(VtfTexture image)
    {
        byte[] pixels = image.Pixels;

        if (pixels.Length < 4)
        {
            return (0d, 0d, 0d, 0d);
        }

        double red = 0d;
        double green = 0d;
        double blue = 0d;
        double alpha = 0d;

        int counted = pixels.Length / 4;

        for (int at = 0; at + 3 < pixels.Length; at += 4)
        {
            red += pixels[at];
            green += pixels[at + 1];
            blue += pixels[at + 2];
            alpha += pixels[at + 3];
        }

        return (red / counted, green / counted, blue / counted, alpha / counted);
    }
}
