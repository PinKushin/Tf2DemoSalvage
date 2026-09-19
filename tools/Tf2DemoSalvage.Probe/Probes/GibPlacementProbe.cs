using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Whether a class model and its gibs carry the <c>placementOrigin</c> attachment <c>CreateGibsFromList</c> places by (B409).</summary>
/// <remarks>
/// `CreateGibsFromList` (`props_shared.cpp:1370-1466`) puts each piece at <c>matrix · (offset − (gib.placementOrigin −
/// parent.placementOrigin))</c>, or at <c>matrix · (offset − parent.placementOrigin)</c> for a gib without one. Whether that
/// moves a TF2 gib at all is a question of data: this prints the attachment's translation for the class model and each piece.
/// **The control is the class model's own attachment count**, printed first — a reader that read none would report every
/// piece as unplaced.
/// <code>
///   gib-placement &lt;class model path&gt;      e.g. models/player/soldier.mdl
/// </code>
/// </remarks>
public sealed class GibPlacementProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "gib-placement";

    /// <inheritdoc/>
    public string Summary => "the placementOrigin attachment of a class model and each of its gibs: gib-placement <class model path>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("gib-placement <class model path>");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        string parent = arguments[0];

        Report(output, game, parent);

        foreach (PhysicsBreakPiece piece in DemoModels.BreakPiecesOf(parent, game))
        {
            Report(output, game, PhysicsModel.GibPath(piece.Model));
        }
    }

    private static void Report(TextWriter output, GameContent game, string path)
    {
        if (game.Archives.Read(path) is not { } file)
        {
            output.WriteLine($"{path}: not found");
            return;
        }

        IReadOnlyList<StudioAttachment> attachments = StudioAttachment.Read(file);
        StudioAttachment? placement = attachments.FirstOrDefault(
            attachment => string.Equals(attachment.Name, "placementOrigin", StringComparison.OrdinalIgnoreCase));

        output.WriteLine(placement is { Name: not null } found
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{path}: {attachments.Count} attachments, placementOrigin on bone {found.Bone} at " +
                $"({found.Local[3]:0.##}, {found.Local[7]:0.##}, {found.Local[11]:0.##})")
            : $"{path}: {attachments.Count} attachments, no placementOrigin");
    }
}
