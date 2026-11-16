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

        foreach (Tf2DemoSalvage.Core.Scene.ScenePlayer player in Tf2DemoSalvage.Core.Scene.DemoTimeline.Build(File.ReadAllBytes(demo)).PlayersAt(tick))
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
    }
}
