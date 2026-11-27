using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The installed `proto_defs.vpd` read as the HUD reads it: how many paint kits, and for the indices asked, each kit's
/// token and its English string (B754).
/// </summary>
/// <remarks>
/// **Its control**: kit 14 is the one `concealedkiller_sniperrifle_nightowl` (15000) names in the shipped
/// `items_game.txt`, so it must be present; an absent 14 means the reader, not the file, is wrong. It asserts nothing (D38).
/// </remarks>
public sealed class PaintKitsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "paint-kits";

    /// <inheritdoc/>
    public string Summary => "the installed proto_defs.vpd's paint kits: count, and each asked index's token and English name: paint-kits [index ...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        GameArchives archives = GameArchives.Open(new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder()).WithoutCustom();

        if (archives.Read(PaintKitDefinitions.FilePath) is not { } file)
        {
            output.WriteLine($"no {PaintKitDefinitions.FilePath}");
            return;
        }

        PaintKitDefinitions kits = PaintKitDefinitions.Read(file);
        VguiLocalize english = new("english");
        english.AddFile("resource/tf_%language%.txt", archives.Read);
        english.AddFile("resource/tf_proto_obj_defs_%language%.txt", archives.Read);

        output.WriteLine($"{file.Length} bytes, {kits.Count} paint kits");

        // `paint-kits composite <kit> <item def> <wear> <team> <seed> <size> <out.png>`: the composite, through the compositor.
        if (arguments.Count >= 8 && arguments[0] == "composite")
        {
            Composite(output, archives, kits, arguments);
            return;
        }

        // `paint-kits recipe <kit> <item def> <wear>`: the kit's items' definition indices, then the recipe's tree.
        if (arguments.Count >= 4 && arguments[0] == "recipe")
        {
            uint kit = uint.Parse(arguments[1], CultureInfo.InvariantCulture);
            ProtoMessage? kitMessage = kits.Definition(PaintKitDefinitions.TypeDefinition, kit);
            List<string> listed = [];

            List<ProtoMessage> items = kitMessage is null ? [] : [.. kitMessage.Messages(5), .. Singles(kitMessage)];

            foreach (ProtoMessage item in items)
            {
                if (item.Message(1) is { } template && template.Varint(1) is { } at
                    && kits.Definition(PaintKitDefinitions.TypeItemDefinition, (uint)at)?.Varint(2) is { } index)
                {
                    // `ItemData.material_override` (field 3), which `GetPaintKitMaterialOverride` hands the model.
                    listed.Add(index.ToString(CultureInfo.InvariantCulture) + " (material " + (item.Message(5)?.Text(3) ?? "none") + ")");
                }
            }

            output.WriteLine($"kit {kit} lists item definitions: {string.Join(", ", listed)}");
            PaintKitNode? recipe = PaintKitRecipe.Build(
                kits, kit, int.Parse(arguments[2], CultureInfo.InvariantCulture), int.Parse(arguments[3], CultureInfo.InvariantCulture));
            Print(output, recipe, 0);
            return;
        }

        if (arguments.Count >= 2 && arguments[0].EndsWith(".dem", StringComparison.OrdinalIgnoreCase))
        {
            Describe(output, arguments[0], int.Parse(arguments[1], CultureInfo.InvariantCulture), archives, kits, english);
            return;
        }

        foreach (string argument in arguments.Count > 0 ? arguments : ["14"])
        {
            uint index = uint.Parse(argument, CultureInfo.InvariantCulture);
            string? token = kits.DescriptionToken(index);
            string? text = token is null ? null : english.Find(token);

            output.WriteLine($"  {index}: token {token ?? "(no kit)"} -> {text ?? "(no string)"}");
        }
    }

    private static void Composite(TextWriter output, GameArchives archives, PaintKitDefinitions kits, IReadOnlyList<string> arguments)
    {
        int Int(int at) => int.Parse(arguments[at], CultureInfo.InvariantCulture);

        PaintKitNode? recipe = PaintKitRecipe.Build(kits, uint.Parse(arguments[1], CultureInfo.InvariantCulture), Int(2), Int(3));

        if (recipe is null)
        {
            output.WriteLine("no recipe");
            return;
        }

        int size = Int(6);
        TextureCompositor? compositor = TextureCompositor.Create(recipe, size, size, Int(4), ulong.Parse(arguments[5], CultureInfo.InvariantCulture));

        if (compositor is null)
        {
            output.WriteLine("the compositor refused the recipe");
            return;
        }

        // `AsyncFindTexture( name, "Runtime Composite" )`: the texture's whole mip chain, decoded.
        Dictionary<string, CompositorTexture?> cache = new(StringComparer.OrdinalIgnoreCase);

        CompositorTexture? Load(string name)
        {
            if (cache.TryGetValue(name, out CompositorTexture? cached))
            {
                return cached;
            }

            CompositorTexture? texture = null;

            if (archives.Read("materials/" + name + ".vtf") is { } file)
            {
                List<(int Width, int Height, byte[] Pixels)> levels = [];
                VtfTexture top = VtfTexture.Decode(file);
                levels.Add((top.Width, top.Height, top.Pixels));

                for (int edge = Math.Max(top.Width, top.Height) / 2; edge >= 1; edge /= 2)
                {
                    VtfTexture level = VtfTexture.Decode(file, edge);

                    if (Math.Max(level.Width, level.Height) == edge)
                    {
                        levels.Add((level.Width, level.Height, level.Pixels));
                    }
                }

                texture = new CompositorTexture(levels);
            }

            output.WriteLine($"  {name}: {(texture is null ? "missing" : $"{texture.Width}x{texture.Height}, {texture.Levels.Count} levels, mean {Mean(texture.Levels[0].Pixels)}")}");
            cache[name] = texture;
            return texture;
        }

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        byte[]? pixels = compositor.Composite(Load);

        if (pixels is null)
        {
            output.WriteLine("the composite failed");
            return;
        }

        // The colour opaque, and the alpha (the specular mask the weapon's material reads) as its own grey image.
        byte[] colour = (byte[])pixels.Clone();
        byte[] alpha = new byte[pixels.Length];

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            colour[i + 3] = 255;
            alpha[i] = alpha[i + 1] = alpha[i + 2] = pixels[i + 3];
            alpha[i + 3] = 255;
        }

        Tf2DemoSalvage.Render.PngWriter.Write(arguments[7], size, size, colour);
        Tf2DemoSalvage.Render.PngWriter.Write(Path.ChangeExtension(arguments[7], ".alpha.png"), size, size, alpha);
        output.WriteLine($"{compositor.TextureName($"{arguments[2]}_{arguments[1]}_wear_{Int(3):00}", 0u)}: {size}x{size} in {clock.ElapsedMilliseconds} ms -> {arguments[7]}");
    }

    /// <summary>The texels' mean R, G, B and A, the control that a texture decoded at all.</summary>
    private static string Mean(byte[] pixels)
    {
        long[] sums = new long[4];

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            for (int c = 0; c < 4; c++)
            {
                sums[c] += pixels[i + c];
            }
        }

        long count = Math.Max(1, pixels.Length / 4);
        return string.Join(" ", Array.ConvertAll(sums, sum => (sum / count).ToString(CultureInfo.InvariantCulture)));
    }

    private static IEnumerable<ProtoMessage> Singles(ProtoMessage kit)
    {
        for (int number = 6; number <= 50; number++)
        {
            if (kit.Message(number) is { } item)
            {
                yield return item;
            }
        }
    }

    private static void Print(TextWriter output, PaintKitNode? node, int depth)
    {
        if (node is null)
        {
            output.WriteLine("(no recipe)");
            return;
        }

        output.WriteLine($"{new string(' ', depth * 2)}{node.Name}{(node.Value is null ? string.Empty : " = " + node.Value)}");

        foreach (PaintKitNode child in node.Children)
        {
            Print(output, child, depth + 1);
        }
    }

    /// <summary>Every painted item the players carry at the tick, through the description the item panels draw.</summary>
    private static void Describe(TextWriter output, string demo, int tick, GameArchives archives, PaintKitDefinitions kits, VguiLocalize english)
    {
        if (archives.Read("scripts/items/items_game.txt") is not { } itemsGame)
        {
            output.WriteLine("no items_game.txt");
            return;
        }

        ItemSchema schema = ItemSchema.Read(itemsGame);
        TfItemDescriptionContext context = new()
        {
            Schema = schema,
            Find = english.Find,
            PaintKitToken = kits.DescriptionToken,
        };
        int painted = 0;

        foreach (Tf2DemoSalvage.Core.Scene.ScenePlayer player in ProbeInstall.Timeline(File.ReadAllBytes(demo), output).PlayersAt(tick))
        {
            foreach (Tf2DemoSalvage.Core.Scene.SceneItem item in player.Items ?? [])
            {
                if (TfItemView.From(item, schema) is not { } view || view.Attribute(schema, "paintkit_proto_def_index") is null)
                {
                    continue;
                }

                painted++;
                IReadOnlyList<TfDescriptionLine> lines = TfItemDescription.Generate(view, context);
                output.WriteLine($"player {player.EntityIndex}, item {item.DefinitionIndex}: {lines.Count} lines");

                foreach (TfDescriptionLine line in lines)
                {
                    output.WriteLine($"    [{line.Color,2} 0x{line.MetaType:x4}] {line.Text}");
                }
            }
        }

        // The control: a demo with no painted item says so, rather than printing nothing.
        output.WriteLine($"{painted} painted items at tick {tick}");

        // **What the world draw asks for, through the production resolver** (B754): every prop carrying an item, its
        // forced material and its composite, so an unpainted world weapon says which half is missing.
        WeaponModels weapons = new(archives.Read, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        List<Tf2DemoSalvage.Core.Scene.SceneProp> props = [];
        ProbeInstall.Timeline(File.ReadAllBytes(demo), output).PropsAt(tick, props);
        int asked = 0;

        foreach (Tf2DemoSalvage.Core.Scene.SceneProp prop in props)
        {
            if (prop.ItemDefinitionIndex is not { } item)
            {
                continue;
            }

            asked++;
            WeaponSkin? skin = weapons.WeaponSkinFor(prop, fullSize: false);

            if (skin is not null || prop.Econ is { Local.Count: > 0 } || prop.Econ is { NetworkedForDemos.Count: > 0 })
            {
                output.WriteLine(
                    $"prop {prop.EntityIndex} item {item} '{prop.ModelPath}': econ local {prop.Econ?.Local.Count ?? -1}"
                    + $" demos {prop.Econ?.NetworkedForDemos.Count ?? -1} team {prop.ItemTeam}, material "
                    + $"{weapons.MaterialOverrideFor(prop, prop.ItemTeam) ?? "none"}, skin {skin?.Name ?? "none"}");
            }
        }

        output.WriteLine($"{asked} props carrying an item");
    }
}
