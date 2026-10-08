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

        if (arguments[0] == "sentence" && arguments.Count > 2)
        {
            Dictionary<string, Sentence> cache = SoundCacheFile.Read(File.ReadAllBytes(arguments[1]));
            output.WriteLine($"{cache.Count} sentences");

            foreach ((string name, Sentence sentence) in cache.Where(p => p.Key.Contains(arguments[2], StringComparison.OrdinalIgnoreCase)).Take(100))
            {
                string phonemes = string.Join(" ", sentence.Phonemes.Take(8).Select(p =>
                    string.Create(CultureInfo.InvariantCulture, $"{p.Code}@{p.Start:0.###}-{p.End:0.###}")));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{name}: {sentence.Phonemes.Count} phonemes, {sentence.Emphasis.Count} emphasis, {sentence.SampleCount} samples at {sentence.SampleRate} = {sentence.Length:0.###} s; {phonemes}"));
            }

            return;
        }

        if (arguments[0] == "voices" && arguments.Count > 2 && DemoCorpus.Find(arguments[1], output) is { } demo)
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));

            foreach (SceneSound sound in timeline.Sounds.Where(s => s.Name.Contains(arguments[2], StringComparison.OrdinalIgnoreCase)).Take(40))
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"tick {sound.Tick} entity {sound.EntityIndex} channel {sound.Channel} '{sound.Name}' stop {sound.IsStop} ignore-phonemes {sound.IgnoresPhonemes} pitch {sound.Pitch}"));
            }

            return;
        }

        // Every played sound whose path the sound caches carry a sentence for — the control is the cache count.
        if (arguments[0] == "lipsync" && arguments.Count > 1 && DemoCorpus.Find(arguments[1], output) is { } spoken)
        {
            Dictionary<string, Sentence> sentences = new(StringComparer.OrdinalIgnoreCase);

            foreach (string cache in Directory.GetFiles(folder, "*.sound.cache"))
            {
                foreach ((string name, Sentence sentence) in SoundCacheFile.Read(File.ReadAllBytes(cache)))
                {
                    sentences.TryAdd(name, sentence);
                }
            }

            output.WriteLine($"{sentences.Count} cached sentences");
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(spoken));

            foreach (SceneSound sound in timeline.Sounds.Where(s => !s.IsStop &&
                sentences.ContainsKey(SoundCacheFile.Normalise("sound/" + s.Name.TrimStart('*', '#', '@', '>', '<', '^', ')', '}', '$', '!', '?', '&', '~', '`', '+', '%', '(')))).Take(40))
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"tick {sound.Tick} entity {sound.EntityIndex} channel {sound.Channel} '{sound.Name}' ignore-phonemes {sound.IgnoresPhonemes}"));
            }

            return;
        }

        // Every scene a recording plays: actors, start and stop, and what of it moves a face. Overlapping runs on one
        // actor are flagged. The control is the scene count, which `scene-wire` reports too.
        if (arguments[0] == "played" && arguments.Count > 1 && DemoCorpus.Find(arguments[1], output) is { } played &&
            game.Archives.Read("scenes/scenes.image") is { } imageBytes && SceneImage.Read(imageBytes) is { } image)
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(played));
            output.WriteLine($"{timeline.Scenes.Count} scene runs");
            Dictionary<int, List<(int Start, int? Stop)>> byActor = [];

            foreach (SceneChoreography run in timeline.Scenes)
            {
                SceneTaunt? plan = image.TauntFor(run.Scene, name =>
                    game.Archives.Read("expressions/" + name.Replace('\\', '/') + ".vfe") is { Length: > 0 } vfe ? FlexSettings.Read(vfe) : null);
                bool overlaps = false;

                foreach (int actor in run.Actors)
                {
                    if (!byActor.TryGetValue(actor, out List<(int Start, int? Stop)>? runs))
                    {
                        runs = [];
                        byActor[actor] = runs;
                    }

                    overlaps |= runs.Any(r => r.Stop is null || r.Stop > run.Tick);
                    runs.Add((run.Tick, run.StoppedTick));
                }

                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"tick {run.Tick}-{run.StoppedTick?.ToString(CultureInfo.InvariantCulture) ?? "?"} actors {string.Join(",", run.Actors)} '{run.Scene}': " +
                    $"{plan?.Expressions.Count ?? -1} expressions, {plan?.FlexAnimations.Count ?? -1} flex animations " +
                    $"({plan?.FlexAnimations.Sum(a => a.Tracks.Count) ?? 0} tracks){(overlaps ? " OVERLAPS" : "")}"));
            }

            return;
        }

        if (arguments[0] == "census")
        {
            Census(output, game);
            return;
        }

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
                $"{flex.Flexes.Sum(f => f.Vertices.Count)} vertanims, {flex.Flexes.Count(f => f.IsWrinkle)} wrinkle flexes " +
                $"({flex.Flexes.Where(f => f.IsWrinkle).Sum(f => f.Vertices.Count(v => v.RawWrinkle != 0))} nonzero wrinkle deltas)"));

            // The header's own name — `pszName()`, which `C_TFPlayer::InitPhonemeMappings` builds the phoneme file from.
            string internalName = System.Text.Encoding.ASCII.GetString(bytes, 12, 64).TrimEnd('\0');
            string stem = Path.ChangeExtension(internalName, null).Replace('\\', '/');
            output.WriteLine($"    name '{internalName}'; expressions/{stem}/phonemes/phonemes.vfe " +
                (game.Archives.Read($"expressions/{stem}/phonemes/phonemes.vfe") is null ? "absent" : "present"));

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
    /// Every model the game ships: how many carry flexes, how many wrinkle flexes, how many fixed-point deltas, and
    /// which non-player models flex. The control is the player count, which must be nine.
    /// </summary>
    private static void Census(TextWriter output, GameContent game)
    {
        int models = 0, flexed = 0, wrinkled = 0, fixedPoint = 0, unread = 0, slow = 0;
        List<string> named = [];

        foreach (string path in game.Archives.Paths().Where(p => p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                continue;
            }

            models++;

            StudioFlexData flex;

            try
            {
                flex = StudioFlex.Read(bytes);
            }
            catch (InvalidDataException)
            {
                unread++;
                continue;
            }

            if (!flex.HasVertexAnimation)
            {
                continue;
            }

            flexed++;
            named.Add(path);
            if (flex.Flexes.Any(f => f.IsWrinkle))
            {
                wrinkled++;
                output.WriteLine($"    wrinkle: {path}");
            }
            fixedPoint += (BitConverter.ToInt32(bytes, 152) & StudioFlex.FixedPointScaleFlag) != 0 ? 1 : 0;
            slow += flex.Flexes.Any(f => f.Vertices.Any(v => v.Speed != 255)) ? 1 : 0;

            // A flex whose ramp is not zero at weight zero moves even on an entity that sets no weights
            // (LockFlexWeights zeroes the buffer, studiorender 0x1800584b0).
            if (flex.Flexes.Any(f => StudioFlexRules.Ramp(f, 0f) != 0f))
            {
                output.WriteLine($"    moves at zero weight: {path}");
            }

            if ((BitConverter.ToInt32(bytes, 152) & StudioFlex.ConvertedFlag) != 0)
            {
                output.WriteLine($"    converted on disk: {path}");
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{models} models, {flexed} with vertex flex ({named.Count(p => p.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) && p.Count(c => c == '/') == 2)} class models), " +
            $"{wrinkled} with wrinkle flexes, {fixedPoint} fixed point, {slow} with a speed below 255, {unread} unreadable"));
        output.WriteLine("    " + string.Join(", ", named.Take(80)));
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
