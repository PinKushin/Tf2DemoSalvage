using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A shipped material as the game stores it, and as this project reads it.
/// </summary>
/// <remarks>
/// **The game's own data is a source and it is the one nobody looks in** — CLAUDE.md's fifth
/// source. A VMT is a few lines of KeyValues and it answers questions that measuring our output
/// only narrows: which shader, which textures, and every parameter we might be ignoring.
///
/// Prints the raw text FIRST and our parse second, because the interesting failure is a key the
/// file carries and the reader drops — a `$basetexturetransform` scale, a `$detail`, a `Patch`
/// include. A parse printed alone can only report what it already understood.
///
/// <code>
///   vmt models/props_gameplay/door_grate001
/// </code>
/// </remarks>
public sealed class MaterialProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vmt";

    /// <inheritdoc/>
    public string Summary => "a material as the game ships it, raw then parsed: vmt <path>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("vmt <path> — for example: vmt models/props_gameplay/door_grate001");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        string name = arguments[0].Replace('\\', '/').Trim('/');

        if (name.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        string path = name.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)
            ? name + ".vmt"
            : "materials/" + name + ".vmt";

        if (game.Archives.Read(path) is not { } bytes)
        {
            output.WriteLine($"'{path}' is not in the game's content.");
            return;
        }

        output.WriteLine(
            $"{path} — {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes");
        output.WriteLine("--- as shipped");
        output.WriteLine(Encoding.UTF8.GetString(bytes).Trim());

        output.WriteLine("--- as this project reads it");

        VmtMaterial material = VmtMaterial.Parse(bytes);

        output.WriteLine($"shader '{material.Shader}'");

        foreach (string key in material.Keys)
        {
            output.WriteLine($"  {key} = '{material.Value(key) ?? string.Empty}'");
        }

        // **And the TEXTURE it names, because a material that parses correctly can still paint the
        // wrong pixels.** A mean colour is a blunt instrument and a decisive one here: the owner's
        // question is why a metal frame TF2 draws orange comes out grey, and orange and grey differ
        // in the red channel by more than any lighting can explain.
        if (material.Value("$basetexture") is not { Length: > 0 } texture)
        {
            output.WriteLine("no $basetexture to read");
            return;
        }

        string vtf = "materials/" + texture.Replace((char)92, '/').Trim('/') + ".vtf";

        if (game.Archives.Read(vtf) is not { } pixels)
        {
            output.WriteLine($"'{vtf}' is not in the game's content — this WOULD chequer");
            return;
        }

        VtfTexture image = VtfTexture.Decode(pixels);

        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;
        int counted = 0;

        for (int at = 0; at + 3 < image.Pixels.Length; at += 4)
        {
            red += image.Pixels[at];
            green += image.Pixels[at + 1];
            blue += image.Pixels[at + 2];
            alpha += image.Pixels[at + 3];
            counted++;
        }

        if (counted == 0)
        {
            output.WriteLine($"{vtf} decoded to no pixels at all");
            return;
        }

        output.WriteLine(
            $"{vtf}: {image.Width.ToString(CultureInfo.InvariantCulture)}"
            + $"x{image.Height.ToString(CultureInfo.InvariantCulture)}, "
            + $"mean RGBA ({red / counted:0} {green / counted:0} {blue / counted:0} "
            + $"{alpha / counted:0})"

            // **The frame count, because an animated texture is invisible without it** (B338). A
            // sixteen-frame fire sheet and a still texture print identically otherwise, and 7,027
            // shipped materials run `AnimatedTexture` over one.
            + (image.FrameCount > 1
                ? $", {image.FrameCount.ToString(CultureInfo.InvariantCulture)} animation frames"
                : string.Empty));
    }
}
