using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which models in a real demo animate themselves — the <c>STUDIO_AUTOPLAY</c> census.
/// </summary>
/// <remarks>
/// **Written because B291 implemented a mechanism nothing had yet observed in the wild.** The unit
/// tests build a model with the flag set and prove the loop reads it; that says nothing about
/// whether TF2 ships anything carrying it, and a mechanism that fires on no real content is one
/// whose absence nobody could have noticed.
///
/// <code>
///   autoplay z1800
///   autoplay z1800 20000
/// </code>
///
/// **Through the production loader, not a second reader.** The geometry comes off the loaded map's
/// own `Geometry` callback and the flag is read by
/// <see cref="PropModels.SkinnedModel.AutoplaySequences"/> — the accessor the draw path calls. A
/// probe that reimplements the rule it is checking agrees with whoever wrote the probe (D126).
///
/// **The denominator is printed whether or not anything is found**, because an empty answer is
/// otherwise indistinguishable from a scan that opened nothing
/// (<c>docs/memory/an-empty-search-needs-a-control.md</c>).
/// </remarks>
public sealed class AutoplayProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "autoplay";

    /// <inheritdoc/>
    public string Summary =>
        "models that animate themselves, STUDIO_AUTOPLAY: autoplay <demo> [tick]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("autoplay <demo> [tick]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);

        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader.Parse(file).MapName;

        if (mapName.Length == 0 ||
            locator.Find(mapName) is not { } mapPath ||
            locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        // The production resolution step, for the same reason `props` runs it: a model named only
        // by an item is absent until this happens (B263).
        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} on {mapName} at tick {tick}, {props.Count} props"));

        int opened = 0;
        int carrying = 0;
        int looping = 0;
        int sequences = 0;
        int layered = 0;

        // **Distinct model PATHS, because the flag is a property of the model.** Reporting per prop
        // would multiply one flagged model by however many copies the map places and say nothing
        // more.
        foreach (string model in props
            .Select(prop => prop.ModelPath)
            .Where(model => model.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.Ordinal))
        {
            if (assets.Geometry(model)?.Skinned is not { } skinned)
            {
                continue;
            }

            opened++;

            // **The control, and without it a zero answer is worthless.** `Loops` and `AutoPlays`
            // are two bits of the same `mstudioseqdesc_t::flags` word read through the same merged
            // table, so a looping count that is also zero means the flags are not being read at
            // all — a fact about this probe, not about TF2
            // (`docs/memory/an-empty-search-needs-a-control.md`).
            for (int sequence = 0; sequence < skinned.Sequences.Count; sequence++)
            {
                sequences++;

                if (skinned.Loops(sequence))
                {
                    looping++;
                }

                // **`numautolayers`, counted for the same reason `proctype` was.** A sequence can
                // automatically play other sequences over itself, and `AddSequenceLayers`
                // (`bone_setup.cpp:2125`) is absent here. Whether that matters is one number.
                if (skinned.Sequences.At(sequence) is { } where &&
                    where.Group < skinned.Groups.Count &&
                    where.Local < skinned.Groups[where.Group].Sequences.Count &&
                    skinned.Groups[where.Group].Sequences[where.Local].AutoLayers > 0)
                {
                    layered++;
                }
            }

            IReadOnlyList<int> autoplay = skinned.AutoplaySequences();

            if (autoplay.Count == 0)
            {
                continue;
            }

            carrying++;

            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{model}: {autoplay.Count} autoplay of {skinned.Sequences.Count} sequences"));

            foreach (int sequence in autoplay)
            {
                output.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"    [{sequence}] {skinned.CyclesPerSecond(sequence):0.###} cycles a " +
                        $"second, {skinned.Frames(sequence)} frames, " +
                        $"loops {skinned.Loops(sequence)}, delta {skinned.IsDelta(sequence)}"));
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"AUTOPLAY {carrying} models carry it, of {opened} skinned models opened; " +
                $"control: {looping} of {sequences} sequences carry STUDIO_LOOPING; " +
                $"{layered} sequences declare autolayers"));
    }
}
