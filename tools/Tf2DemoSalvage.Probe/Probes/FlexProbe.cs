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

/// <summary>A model's vertex flex: controllers, rules, and how many vertices each flex moves.</summary>
/// <remarks>
/// <code>
///   flex models/player/heavy.mdl
/// </code>
/// The control is the controller list: a player model that reports controllers and no flexes would be
/// the reader's fault, not the model's.
/// </remarks>
public sealed class FlexProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "flex";

    /// <inheritdoc/>
    public string Summary => "a model's flex controllers, rules and vertex animations: flex <path>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("flex <path> — for example: flex models/player/heavy.mdl");
            return;
        }

        string? folder = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        if (folder is null)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (arguments[0] == "scenes")
        {
            Scenes(output, game, arguments.Skip(1).ToList());
            return;
        }

        foreach (string path in arguments)
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                output.WriteLine($"'{path}' is not in the game's content.");
                continue;
            }

            StudioFlexData flex = StudioFlex.Read(bytes);
            int flags = BitConverter.ToInt32(bytes, 152);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{path}: fixed-point flag {(flags & StudioFlex.FixedPointScaleFlag) != 0}, " +
                $"{flex.Descriptors.Count} descriptors, {flex.Controllers.Count} controllers, " +
                $"{flex.Rules.Count} rules, {flex.Flexes.Count} mesh flexes, " +
                $"{flex.Flexes.Sum(f => f.Vertices.Count)} vertanims"));

            output.WriteLine("    controllers: " + string.Join(", ", flex.Controllers.Select(c =>
                string.Create(CultureInfo.InvariantCulture, $"{c.Name}[{c.Type} {c.Min:0.##}..{c.Max:0.##}]"))));

            // Which controllers no op names directly (FETCH1, 2WAY_0/1, NWAY, DME eyelids' own index) —
            // a controller here can only reach a face through a DME eyelid's stacked constant.
            HashSet<int> named = [.. flex.Rules.SelectMany(rule => rule.Ops)
                .Where(op => op.Op is 2 or 15 or 16 or 17 or 20 or 21).Select(op => op.Index)];
            output.WriteLine("    named by no op: " + string.Join(", ", Enumerable.Range(0, flex.Controllers.Count)
                .Where(index => !named.Contains(index)).Select(index => flex.Controllers[index].Name)));

            foreach (StudioMeshFlex mesh in flex.Flexes.Take(12))
            {
                float largest = mesh.Vertices.Count == 0 ? 0f : mesh.Vertices.Max(v =>
                    MathF.Sqrt((v.Delta.X * v.Delta.X) + (v.Delta.Y * v.Delta.Y) + (v.Delta.Z * v.Delta.Z)));

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {flex.Descriptors[mesh.FlexDesc]} pair {mesh.FlexPair} targets " +
                    $"{mesh.Target0:0.###}/{mesh.Target1:0.###}/{mesh.Target2:0.###}/{mesh.Target3:0.###} " +
                    $"{mesh.Vertices.Count} verts, largest {largest:0.###}, speeds " +
                    $"{string.Join("/", mesh.Vertices.Select(v => v.Speed).Distinct().Take(4))}, sides " +
                    $"{string.Join("/", mesh.Vertices.Select(v => v.Side).Distinct().Take(4))}"));
            }

            foreach (StudioFlexRule rule in flex.Rules.Take(6))
            {
                output.WriteLine(
                    $"    rule {flex.Descriptors[rule.Flex]}: " +
                    string.Join(" ", rule.Ops.Select(op => op.Op == 1
                        ? op.Value.ToString("0.###", CultureInfo.InvariantCulture)
                        : $"op{op.Op}:{op.Index}")));
            }
        }
    }

    /// <summary>
    /// Which event types the whole scene archive uses and which carry flex tracks, or with names, one
    /// scene's flex events in full.
    /// </summary>
    private static void Scenes(TextWriter output, GameContent game, List<string> names)
    {
        if (game.Archives.Read("scenes/scenes.image") is not { } bytes || SceneImage.Read(bytes) is not { } image)
        {
            output.WriteLine("No scenes.image.");
            return;
        }

        if (names.Count > 0)
        {
            foreach (string name in names)
            {
                foreach (SceneEvent one in image.EventsFor(name))
                {
                    string tracks = string.Join(", ", one.FlexTracks.Select(t =>
                        $"{t.Controller}({t.Samples.Count}{(t.Combo ? "c" : "")}{(t.Active ? "" : " off")})"));
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{name}: type {one.Type} {one.Start:0.###}..{one.End:0.###} '{one.Parameters}' ramp {one.Ramp.Count}, tracks {one.FlexTracks.Count}: {tracks}"));
                }
            }

            return;
        }

        Dictionary<int, int> types = [];
        Dictionary<int, int> withTracks = [];
        Dictionary<int, int> curves = [];
        Dictionary<string, int> controllers = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < image.Count; index++)
        {
            foreach (SceneEvent one in image.EventsAt(index))
            {
                types[one.Type] = types.GetValueOrDefault(one.Type) + 1;

                if (one.FlexTracks.Count > 0)
                {
                    withTracks[one.Type] = withTracks.GetValueOrDefault(one.Type) + 1;
                }

                foreach (SceneFlexTrack track in one.FlexTracks)
                {
                    controllers[track.Controller] = controllers.GetValueOrDefault(track.Controller) + 1;

                    foreach (int curve in track.Samples.Select(sample => sample.CurveType))
                    {
                        curves[curve] = curves.GetValueOrDefault(curve) + 1;
                    }
                }
            }
        }

        output.WriteLine("event types: " + string.Join(", ", types.OrderBy(p => p.Key).Select(p =>
            $"{p.Key}={p.Value} (with tracks {withTracks.GetValueOrDefault(p.Key)})")));
        output.WriteLine("curve types: " + string.Join(", ", curves.OrderByDescending(p => p.Value).Take(12)
            .Select(p => $"0x{p.Key:X4}={p.Value}")));
        output.WriteLine("controllers: " + string.Join(", ", controllers.OrderByDescending(p => p.Value).Take(60)
            .Select(p => $"{p.Key}={p.Value}")));
    }
}
