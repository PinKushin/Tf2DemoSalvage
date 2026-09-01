using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which items hang extra models on themselves, read from the shipped schema.
/// </summary>
/// <remarks>
/// **The denominator for `attached_models`, measured rather than assumed.** The shipped
/// `items_game.txt` carries 29 `attached_models` blocks and 310 `attached_models_festive`, but the
/// number of ITEMS affected is larger, because the blocks sit on prefabs that many definitions
/// inherit from. A count of blocks is a fact about the file; this is a fact about the game.
///
/// **Reads the schema through the same `ItemSchema` the viewer uses**, so a parser that silently
/// found nothing would report nothing here rather than agreeing with a second implementation.
///
/// <code>
///   attachments
///   attachments 215
///   attachments pilotlight
/// </code>
/// </remarks>
public sealed class AttachmentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "attachments";

    /// <inheritdoc/>
    public string Summary =>
        "items that hang extra models on themselves: attachments [item index or model substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found, so there is no schema to read.");
            return;
        }

        string path = Path.Combine(folder, "scripts", "items", "items_game.txt");

        if (!File.Exists(path))
        {
            output.WriteLine($"No items_game.txt at {path}.");
            return;
        }

        ItemSchema schema = ItemSchema.Read(File.ReadAllBytes(path));

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;

        // **`pack <demo>` answers the other half of the question**: not "what does the schema say"
        // but "what does the PACKING set contain", which is what decides whether an attachment has
        // geometry when the draw asks for it. The two were measured disagreeing — the draw emitted
        // a pilot light and the packer had never heard of it, which shows as
        // "posed before its geometry was uploaded" rather than as anything about attachments.
        if (string.Equals(filter, "pack", StringComparison.OrdinalIgnoreCase) && arguments.Count > 1)
        {
            if (DemoCorpus.Find(arguments[1], output) is not { } demo)
            {
                output.WriteLine($"No demo named '{arguments[1]}'.");
                return;
            }

            GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));

            List<string> attachments = [.. game.Weapons.AllAttachmentsIn(timeline)];

            output.WriteLine(
                $"PACK {attachments.Count.ToString(CultureInfo.InvariantCulture)} attachment models "
                + $"from {timeline.Props.Count.ToString(CultureInfo.InvariantCulture)} prop tracks");

            // **Packing is the hop after the list, and it is where the pilot light was lost.** The
            // list contained it and the draw asked for it, and the renderer still reported
            // "posed before its geometry was uploaded" — so the question is not what the schema
            // says but whether `EntityModelSet` can actually pack the file.
            EntityModelSet packed = new();

            foreach (string model in attachments.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int before = packed.Count;
                bool added = packed.Precache([model]);

                output.WriteLine(
                    $"  {(added && packed.Count > before ? "packed " : "REFUSED")}  {model}");
            }

            return;
        }

        int wanted = int.TryParse(
            filter, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : -1;

        int items = 0;
        int plain = 0;
        int festive = 0;

        // **Every definition the schema declares**, because the question is which ITEMS are
        // affected rather than which blocks exist, and inheritance multiplies the first by the
        // second.
        foreach (int definition in schema.DefinitionIndices.Order())
        {
            // Asked for both teams and both festive states, since the report is about what the
            // schema HOLDS; the viewer applies the filters at draw time.
            List<AttachedModel> found =
            [
                .. schema.AttachedModelsFor(definition, team: null, festivized: true),
                .. schema.AttachedModelsFor(definition, team: 2, festivized: true),
                .. schema.AttachedModelsFor(definition, team: 3, festivized: true),
            ];

            List<AttachedModel> distinct =
                [.. found.DistinctBy(attached => (attached.Model, attached.Team))];

            if (distinct.Count == 0)
            {
                continue;
            }

            items++;
            plain += distinct.Count(attached => !attached.Festive);
            festive += distinct.Count(attached => attached.Festive);

            bool matches = filter.Length == 0
                || definition == wanted
                || distinct.Any(attached =>
                    attached.Model.Contains(filter, StringComparison.OrdinalIgnoreCase));

            if (!matches)
            {
                continue;
            }

            foreach (AttachedModel attached in distinct)
            {
                output.WriteLine(
                    $"item {definition,6}  flags {attached.DisplayFlags}  "
                    + $"{(attached.Festive ? "festive" : "plain  ")}  "
                    + $"team {(attached.Team.Length == 0 ? "any" : attached.Team),3}  "
                    + attached.Model);
            }
        }

        output.WriteLine(
            $"ATTACHMENTS {items.ToString(CultureInfo.InvariantCulture)} items carry one; "
            + $"{plain.ToString(CultureInfo.InvariantCulture)} plain and "
            + $"{festive.ToString(CultureInfo.InvariantCulture)} festive entries");
    }
}
