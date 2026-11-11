using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A shipped VMT's text and its <c>$lightwarptexture</c>'s header and texels (B559).</summary>
/// <remarks>
/// **Read through the production archive reader and VTF decoder**, so the bytes are the ones the viewer loads. The
/// control is the VMT itself: a material that does not print means the path is wrong, not that it has no warp.
/// </remarks>
public sealed class MaterialRampProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "material-ramp";

    /// <inheritdoc/>
    public string Summary => "a shipped VMT's text and its $lightwarptexture's header, rows and texels: material-ramp <materials/....vmt | materials/....vtf>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Summary);
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read(arguments[0]) is not { } bytes)
        {
            output.WriteLine($"{arguments[0]}: not in the archives");
            return;
        }

        string path = arguments[0];
        byte[] vtf = bytes;

        if (!path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine(Encoding.UTF8.GetString(bytes));

            if (VmtMaterial.Parse(bytes).LightWarpTexture is not { } warp)
            {
                output.WriteLine("no $lightwarptexture");
                return;
            }

            path = $"materials/{warp.Replace('\\', '/')}.vtf";

            if (game.Archives.Read(path) is not { } warpBytes)
            {
                output.WriteLine($"{path}: not in the archives");
                return;
            }

            vtf = warpBytes;
        }

        VtfTexture texture = VtfTexture.Decode(vtf);
        output.WriteLine(
            $"{path}: {texture.Width}x{texture.Height} {texture.Format} mips {texture.MipCount} flags 0x{texture.Flags:x8}");

        byte[] pixels = texture.Pixels;
        int width = texture.Width;
        int rowsDiffering = Enumerable.Range(1, texture.Height - 1)
            .Count(row => !pixels.AsSpan(row * width * 4, width * 4).SequenceEqual(pixels.AsSpan(0, width * 4)));
        output.WriteLine($"rows differing from row 0: {rowsDiffering}");

        // Every 16th column, red channel of every row: the rows are the question (which one does tex1D read?).
        output.WriteLine("  col  red per row 0..last");

        for (int x = 0; x < width; x += Math.Max(1, width / 16))
        {
            IEnumerable<string> reds = Enumerable.Range(0, texture.Height)
                .Select(row => pixels[((row * width) + x) * 4].ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(3));
            output.WriteLine($"  {x,4} {string.Join(' ', reds)}");
        }
    }
}
