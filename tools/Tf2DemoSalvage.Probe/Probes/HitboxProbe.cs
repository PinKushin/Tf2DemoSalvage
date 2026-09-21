using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A model's hitbox sets as <see cref="StudioHitboxes"/> reads them (B415).</summary>
/// <remarks>The control for "a bullet hit no player": if the reader finds no boxes on a player model, the trace cannot.</remarks>
public sealed class HitboxProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "hitboxes";

    /// <inheritdoc/>
    public string Summary => "a model's hitbox sets, boxes and bone contents: hitboxes [model]";

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

        string model = arguments.Count > 0 ? arguments[0] : "models/player/scout.mdl";
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read(model) is not { } bytes)
        {
            output.WriteLine($"{model}: not found.");
            return;
        }

        IReadOnlyList<IReadOnlyList<StudioHitbox>> sets = StudioHitboxes.Read(bytes);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{model}: {sets.Count} sets"));

        for (int set = 0; set < sets.Count; set++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  set {set}: {sets[set].Count} boxes"));

            foreach (StudioHitbox box in sets[set])
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    bone {box.Bone,3} group {box.Group} contents 0x{box.Contents:x} " +
                    $"({box.Min.X:0.#} {box.Min.Y:0.#} {box.Min.Z:0.#}) - ({box.Max.X:0.#} {box.Max.Y:0.#} {box.Max.Z:0.#})"));
            }
        }
    }
}
