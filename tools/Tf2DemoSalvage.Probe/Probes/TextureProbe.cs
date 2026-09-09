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

        if (arguments[0].Equals("census", StringComparison.OrdinalIgnoreCase))
        {
            Census(output, game);
            return;
        }

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

        // **Colourfulness, because a MEAN cannot tell grey from rainbow.** A texture of saturated
        // noise averages to a near-grey mean, so "mean RGBA (78 68 68 47)" was read as evidence that
        // `smokelit` is grey smoke while the viewer drew it as a rainbow. The per-pixel spread
        // between the largest and smallest channel is the number that distinguishes them: zero for
        // anything grey, whatever its brightness, and large for anything saturated
        // (`docs/memory/print-a-value-somebody-can-recognise.md`).
        (double spread, int mostSpread, int visible) = Colourfulness(first);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  colourfulness: mean channel spread {spread:0.0} over {visible} pixels with alpha, " +
            $"largest {mostSpread}"));

        // **The picture, coarsely, because a statistic cannot say WHAT is wrong.** A mean says grey,
        // a spread says saturated, and neither says whether the texture is a smoke puff, a rainbow,
        // or blocks read at the wrong stride. Sixteen columns of one character each does.
        Picture(output, first);

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

    /// <summary>How many shipped textures the computed image offset was wrong for.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="game">The game's content.</param>
    /// <remarks>
    /// **The denominator, because "we read one texture wrongly" and "we read a tenth of the game
    /// wrongly" are different findings** (`docs/memory/the-denominator-decides-what-can-be-lost.md`).
    /// The reader located pixels at `headerSize + thumbnail`, which is only where they are when the
    /// file carries nothing else; from 7.3 the resource table says. This counts the files where the
    /// two disagree, which is exactly the set that decoded to noise.
    ///
    /// **Only the header is parsed**, not the image — the question is where the pixels ARE, and
    /// decoding ten thousand textures to answer it would take minutes to say the same thing.
    /// </remarks>
    private static void Census(TextWriter output, GameContent game)
    {
        int textures = 0;
        int sevenThree = 0;
        int disagreed = 0;
        List<string> examples = [];

        foreach (string path in game.Archives.Paths())
        {
            if (!path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            textures++;

            if (game.Archives.Read(path) is not { Length: > 0x58 } bytes ||
                bytes[0] != (byte)'V' || bytes[1] != (byte)'T' || bytes[2] != (byte)'F')
            {
                continue;
            }

            if (BitConverter.ToInt32(bytes, 8) < 3)
            {
                continue;
            }

            sevenThree++;

            int resources = BitConverter.ToInt32(bytes, 0x44);

            if (resources is < 0 or > 32)
            {
                continue;
            }

            int headerSize = BitConverter.ToInt32(bytes, 12);
            int thumbnail = -1;
            int image = -1;

            for (int index = 0; index < resources && 0x50 + (index * 8) + 8 <= bytes.Length; index++)
            {
                uint type = BitConverter.ToUInt32(bytes, 0x50 + (index * 8));
                uint payload = BitConverter.ToUInt32(bytes, 0x50 + (index * 8) + 4);

                if (((type >> 24) & 0x02) != 0)
                {
                    continue;
                }

                if ((type & 0x00FFFFFF) == 0x01)
                {
                    thumbnail = (int)payload;
                }
                else if ((type & 0x00FFFFFF) == 0x30)
                {
                    image = (int)payload;
                }
            }

            if (image < 0)
            {
                continue;
            }

            // What the old reader computed: the header, then the thumbnail if the file declares one.
            int lowWidth = bytes[61];
            int lowHeight = bytes[62];

            int computed = headerSize +
                (thumbnail >= 0 && lowWidth > 0 && lowHeight > 0
                    ? Math.Max(1, (lowWidth + 3) / 4) * Math.Max(1, (lowHeight + 3) / 4) * 8
                    : 0);

            if (computed == image)
            {
                continue;
            }

            disagreed++;

            if (examples.Count < 8)
            {
                examples.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {path}: computed {computed}, table says {image} " +
                    $"({image - computed:+#;-#;0} bytes out)"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{textures} textures, {sevenThree} at 7.3 or later, " +
            $"{disagreed} whose pixels are NOT where header+thumbnail lands"));

        foreach (string one in examples)
        {
            output.WriteLine(one);
        }
    }

    /// <summary>Prints the texture as a coarse character grid.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="image">The decoded texture.</param>
    /// <remarks>
    /// **One character per block, chosen by what would be WRONG in different ways.** A transparent
    /// block is a space, a grey one is shaded by brightness, and a saturated one takes the letter of
    /// its dominant channel. Smoke reads as shading with no letters; a broken block decode reads as
    /// letters scattered at random; a texture that is genuinely coloured reads as letters in
    /// coherent regions.
    /// </remarks>
    private static void Picture(TextWriter output, VtfTexture image)
    {
        const int Columns = 64;
        const int Rows = 16;

        byte[] pixels = image.Pixels;

        output.WriteLine("  the top mip, one character per block — ' ' clear, .:-=+*#% grey by "
            + "brightness, RGB a dominant channel");

        for (int row = 0; row < Rows; row++)
        {
            char[] line = new char[Columns];

            for (int column = 0; column < Columns; column++)
            {
                long red = 0;
                long green = 0;
                long blue = 0;
                long alpha = 0;
                int counted = 0;

                int fromX = column * image.Width / Columns;
                int toX = (column + 1) * image.Width / Columns;
                int fromY = row * image.Height / Rows;
                int toY = (row + 1) * image.Height / Rows;

                for (int y = fromY; y < toY; y++)
                {
                    for (int x = fromX; x < toX; x++)
                    {
                        int at = ((y * image.Width) + x) * 4;

                        if (at + 3 >= pixels.Length)
                        {
                            continue;
                        }

                        red += pixels[at];
                        green += pixels[at + 1];
                        blue += pixels[at + 2];
                        alpha += pixels[at + 3];
                        counted++;
                    }
                }

                line[column] = counted == 0 ? ' ' : Character(
                    (int)(red / counted),
                    (int)(green / counted),
                    (int)(blue / counted),
                    (int)(alpha / counted));
            }

            output.WriteLine("  " + new string(line));
        }
    }

    /// <summary>One block's character.</summary>
    private static char Character(int red, int green, int blue, int alpha)
    {
        if (alpha < 24)
        {
            return ' ';
        }

        int most = Math.Max(red, Math.Max(green, blue));
        int least = Math.Min(red, Math.Min(green, blue));

        if (most - least > 48)
        {
            if (most == red)
            {
                return 'R';
            }

            return most == green ? 'G' : 'B';
        }

        const string Shades = ".:-=+*#%";

        return Shades[Math.Clamp(most * Shades.Length / 256, 0, Shades.Length - 1)];
    }

    /// <summary>How far from grey the visible pixels are.</summary>
    /// <param name="image">The decoded texture.</param>
    /// <returns>The mean spread, the largest spread, and how many pixels were counted.</returns>
    /// <remarks>
    /// **Only pixels with alpha are counted.** A sprite sheet's transparent margin is whatever the
    /// compressor left there and says nothing about what gets drawn, so including it moves the
    /// answer toward the padding rather than toward the image.
    /// </remarks>
    private static (double Mean, int Largest, int Counted) Colourfulness(VtfTexture image)
    {
        byte[] pixels = image.Pixels;

        long total = 0;
        int largest = 0;
        int counted = 0;

        for (int at = 0; at + 3 < pixels.Length; at += 4)
        {
            if (pixels[at + 3] == 0)
            {
                continue;
            }

            int most = Math.Max(pixels[at], Math.Max(pixels[at + 1], pixels[at + 2]));
            int least = Math.Min(pixels[at], Math.Min(pixels[at + 1], pixels[at + 2]));

            total += most - least;
            largest = Math.Max(largest, most - least);
            counted++;
        }

        return (counted == 0 ? 0d : (double)total / counted, largest, counted);
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
