using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every player's pose at a tick, landed on by a skip through the production seek path, once per way of spreading the
/// skip's batches over drawn frames (B712).
/// </summary>
/// <remarks>
/// **The production path, not a copy of it.** `FramePresenter` walks the skip (`StepSkip`, B708), `MomentPresenter`
/// builds each moment over `TimelineMoments` and poses it, and the pose read back is what `EntityModelSet` built —
/// `FrameOf` and `LayersOf`, the skeleton's own fields (B243). The only thing the probe chooses is what the window
/// chooses: how many batches each drawn frame runs, which in the viewer is the frame's wall-clock length
/// (`DemoSkip.BatchesThisFrame`). Each spread gets a fresh scene and model set; the demo, map and game are shared.
///
/// **The control** is the first spread run twice: two fresh walks with the same spread must print the same digest. If
/// they differ, the instrument (or something process-global) is nondeterministic and no other line means anything.
///
/// <code>
///   seek-pose f12 6662                    spreads 1, 5, 6, 0 (0 = no drawn frame, the whole skip in one call)
///   seek-pose f12 6662 5 6 5,6,4          a spread may be a list, cycled: 5,6,4 runs 5, then 6, then 4, ...
/// </code>
/// </remarks>
public sealed class SeekPoseProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "seek-pose";

    /// <inheritdoc/>
    public string Summary =>
        "each player's sequence, frame and layers at a tick landed on by a skip, per batches-per-drawn-frame spread (B712): " +
        "seek-pose <demo> <tick> [spread...]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        string[] spreads = arguments.Count > 2 ? [.. arguments.Skip(2)] : ["1", "5", "6", "0"];

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Core.Container.DemoHeader.Parse(file).MapName;

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        SoundScriptCatalog scripts = SoundScriptCatalog.Load(game.Archives.Read);

        // The control first: the first spread twice, each on a fresh walk.
        List<string> runs = [spreads[0], .. spreads];
        string? control = null;

        for (int run = 0; run < runs.Count; run++)
        {
            int[] spread = [.. runs[run].Split(',').Select(part => int.Parse(part, CultureInfo.InvariantCulture))];
            (string digest, string detail) = Land(timeline, game, map, assets, scripts, tick, spread);

            string role = run switch
            {
                0 => " (control A)",
                1 => " (control B)",
                _ => string.Empty,
            };

            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"spread {runs[run]}{role}: digest {digest}"));
            output.Write(detail);

            if (run == 0)
            {
                control = digest;
            }
            else if (run == 1)
            {
                output.WriteLine(digest == control
                    ? "CONTROL pass: the same spread twice lands on the same poses"
                    : "CONTROL FAIL: the same spread twice landed differently, so the instrument is not deterministic");
            }
        }
    }

    /// <summary>One fresh seek to <paramref name="tick"/>, its batches spread over drawn frames as asked, then the landing posed.</summary>
    private static (string Digest, string Detail) Land(
        DemoTimeline timeline,
        GameContent game,
        LoadedMap map,
        MapAssets assets,
        SoundScriptCatalog scripts,
        int tick,
        int[] spread)
    {
        EntityModelSet models = new() { Geometry = assets.Geometry };
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Lighting = map.Lighting,
            Weapons = game.Weapons,
            StaticProps = assets.StaticModels,
            SequenceEra = SequenceEras.Shipped.EraFor(timeline.NetworkProtocol),
            Viewmodels = new TimelineViewmodels(timeline),
        };
        MomentPresenter moments = new(moment, new FrameLedger(), NullLogger.Instance);
        PlayerAppearances appearances = new(NullLogger.Instance) { Game = game, Timeline = timeline };

        moments.Appearances = appearances;
        moments.Source = new TimelineMoments(timeline);
        models.ModelResolved = (entity, looping, staticProp) => moments.Source?.OnNewModel(entity, looping, staticProp);

        SoundPresenter sound = new(
            new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
            new ActiveLoops(),
            static _ => null,
            NullLogger.Instance)
        {
            Scripts = scripts,
            Schedule = new SoundSchedule(timeline.Sounds),
        };

        FramePresenter frames = new(models, moment, moments, sound, NullLogger.Instance, NullLogger.Instance, NullLogger.Instance)
        {
            SkipsAcrossFrames = spread[0] > 0,
        };

        // Paused, as a `--shot` is: the frame covers no demo time.
        ViewFrame Frame(double shown, float frameSeconds) =>
            new(timeline, null, null, (int)shown, shown, false, false, null, 1280, 720, frameSeconds, 0d, true, true, true, 2048, 50);

        ViewFrame Show(double shown)
        {
            moments.Show(shown, new MomentView((int)shown, false, null, null, 54f, Playing: false));
            return Frame(shown, 0f);
        }

        // The opening seek, from nothing: `ApplyOpeningState`'s `ShowMoment`.
        frames.ShowMoment(Frame(tick, 0f), tick, null, Show, jumps: 1);

        // Each drawn frame: the batches its length allows, then the moment drawn — `PoseNow` with the whole view.
        int drawn = 0;

        while (frames.Skipping)
        {
            int batches = spread[drawn++ % spread.Length];
            float seconds = (batches + 0.5f) * timeline.IntervalPerTick;

            frames.StepSkip(Frame(tick, seconds));
            moments.PoseNow();
        }

        // The frames after the landing that `--shot` waits through, paused at the tick.
        for (int after = 0; after < 3; after++)
        {
            frames.ShowMoment(Frame(tick, 0f), tick, null, Show, jumps: 1);
            moments.PoseNow();
        }

        StringBuilder detail = new();
        StringBuilder digest = new();

        foreach (SceneProp prop in moment.Drawn.Where(static prop => prop.Pose.Speed is not null).OrderBy(static prop => prop.EntityIndex))
        {
            if (models.FrameOf(prop.EntityIndex) is not { } at || assets.Geometry(prop.ModelPath)?.Skinned is not { } skinned)
            {
                continue;
            }

            IReadOnlyList<PoseLayer> layers = models.LayersOf(prop.EntityIndex) ?? [];
            string layered = string.Join(
                " ",
                layers.Select(layer => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{skinned.LabelOf(layer.Sequence)}@{layer.Frame}+{layer.FrameFraction:0.###}w{layer.Weight:0.##}")));
            string gestures = string.Join(
                " ",
                (prop.Pose.Gestures ?? []).Select(gesture => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{gesture.Slot}:{gesture.ActivityName}@{gesture.StartedSeconds:0.###}")));
            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {prop.EntityIndex} {Path.GetFileNameWithoutExtension(prop.ModelPath)} at ({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0}) " +
                $"main {skinned.LabelOf(at.Sequence)}@{at.Frame}+{at.Fraction:0.###}; layers [{layered}]; gestures [{gestures}]");

            digest.AppendLine(line);
            detail.AppendLine(line);
        }

        return (SequenceHash.Add(SequenceHash.Empty, digest.ToString()).ToString("x16", CultureInfo.InvariantCulture), detail.ToString());
    }
}
