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

/// <summary>Which of a model's sequences declare animation events, and what they are (B172).</summary>
/// <remarks>The control for "no footstep fired": if the sequence a player is given carries no 7001, the walk cannot fire one.</remarks>
public sealed class SequenceEventsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "seqevents";

    /// <inheritdoc/>
    public string Summary => "a model's sequences that declare events: seqevents [model] [label substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        string model = arguments.Count > 0 ? arguments[0] : "models/player/heavy_animations.mdl";
        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read(model) is not { } bytes)
        {
            output.WriteLine($"{model}: not found.");
            return;
        }

        IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(bytes);
        int withEvents = 0;

        for (int index = 0; index < sequences.Count; index++)
        {
            StudioSequence sequence = sequences[index];

            if (!sequence.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<StudioEvent> events = sequence.FiredEvents;
            withEvents += events.Count > 0 ? 1 : 0;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {index,4} {sequence.Label,-32} {sequence.Activity,-28} blend {(sequence.Blend is null ? "no " : "yes")} " +
                $"events {string.Join(' ', events.Select(e => $"{e.Id}@{e.Cycle:0.00}{(e.Options.Length > 0 ? ":" + e.Options : string.Empty)}"))}"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{model}: {sequences.Count} sequences, {withEvents} shown with events"));
    }
}
