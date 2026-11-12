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
/// The cycle a server-animated prop carries, read where production keeps it, over a demo (B726).
/// </summary>
/// <remarks>
/// **The production path**: `TimelineMoments` over the demo's timeline, `MomentPresenter` building and posing each moment,
/// `EntityModelSet.ModelResolved` wired to the source as the viewer wires it, and the value read back is
/// `EntityModelSet.ShadowAnimation` — the cycle `Simulate` stored, which is what the render-to-texture shadow's
/// `ANIMATION_CHANGED` compares (B723). A prop is counted by whether its model's PLAYED sequence loops.
///
/// **What it asserts.** A one-shot that finished is held by the server at 1.0 and sent as TF2's top step, 1023/1024
/// (10 bits, `SPROP_ROUNDDOWN`); `ClampCycle` stored 0.999 for it. So a one-shot must be seen at 0.9990234375 (or past
/// one, an interpolated overshoot) and never at 0.999. **The control** is the looping props: a looping cycle is wrapped
/// by the interpolator and must never leave [0, 1); and both groups must be non-empty, or the instrument saw nothing.
///
/// <code>
///   wire-cycle tf2-2013-build1729296-spec-pov-cp_granary          every 33rd tick
///   wire-cycle z1800 0 60000 11
/// </code>
/// </remarks>
public sealed class WireCycleProbe : IProbe
{
    /// <summary>TF2's highest wire cycle: ten bits, rounded down from one.</summary>
    private const float TopStep = 1023f / 1024f;

    /// <summary>What `ClampCycle` stored for a finished one-shot before B726.</summary>
    private const float ClampCeiling = 0.999f;

    /// <inheritdoc/>
    public string Name => "wire-cycle";

    /// <inheritdoc/>
    public string Summary =>
        "server-animated props' stored cycle through the production path, one-shot against looping, with a PASS/FAIL (B726): " +
        "wire-cycle <demo> [from] [to] [step]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        int from = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : timeline.FirstTick;
        int to = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : timeline.LastTick;
        int step = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 33;

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

        EntityModelSet models = new() { Geometry = assets.Geometry };
        MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Lighting = map.Lighting,
            Weapons = game.Weapons,
            StaticProps = assets.StaticModels,
            SequenceEra = SequenceEras.Shipped.EraFor(timeline.NetworkProtocol),
            Viewmodels = new TimelineViewmodels(timeline),
        };
        MomentPresenter moments = new(moment, new FrameLedger(), NullLogger.Instance)
        {
            Appearances = new PlayerAppearances(NullLogger.Instance) { Game = game, Timeline = timeline },
            Source = new TimelineMoments(timeline),
        };

        // The viewer's own wiring (`ViewerPresenter`).
        models.ModelResolved = (entity, looping, staticProp, sequenceLoops) =>
            moments.Source?.OnNewModel(entity, looping, staticProp, sequenceLoops);

        Tally oneShots = new();
        Tally loops = new();
        Dictionary<string, int> topByModel = new(StringComparer.Ordinal);

        for (int tick = from; tick <= to; tick += step)
        {
            moments.Show(tick, new MomentView(tick, false, null, null, 54f, Playing: true));
            moments.PoseNow();

            foreach (SceneProp prop in moment.Drawn)
            {
                if (prop.ClientSideAnimated || assets.Geometry(prop.ModelPath) is not { } frames)
                {
                    continue;
                }

                bool looping = frames.Skinned is { } skinned
                    ? skinned.Loops(skinned.Played(prop.Pose.Sequence))
                    : frames.LoopsAsPlayed(prop.Pose.Sequence);
                float cycle = models.ShadowAnimation(prop).Cycle;

                (looping ? loops : oneShots).Add(cycle);

#pragma warning disable S1244 // the stored value is compared bit for bit, as the engine's dirt test is
                if (cycle == TopStep)
#pragma warning restore S1244
                {
                    string key = (looping ? "looping  " : "one-shot ") + prop.ModelPath;
                    topByModel[key] = topByModel.GetValueOrDefault(key) + 1;
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ticks {from}..{to} every {step}"));
        output.WriteLine($"one-shot {oneShots}");
        output.WriteLine($"looping  {loops}");

        foreach ((string model, int count) in topByModel.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  top step {count,5}  {model}"));
        }

        bool pass = oneShots.Count > 0 && loops.Count > 0 &&
            oneShots.AtTop + oneShots.PastOne > 0 && oneShots.AtClampCeiling == 0 &&
            loops.PastOne == 0 && loops.Negative == 0;

        output.WriteLine(pass
            ? "PASS: one-shots reach the top step (or past one) and never 0.999; looping cycles stay in [0, 1)"
            : "FAIL: see the counts above (a group with no samples means the instrument saw nothing)");
    }

    /// <summary>Counts of stored cycles in one group.</summary>
    private sealed class Tally
    {
        public int Count { get; private set; }

        public int AtTop { get; private set; }

        public int AtClampCeiling { get; private set; }

        public int PastOne { get; private set; }

        public int Negative { get; private set; }

        public float Max { get; private set; } = float.NegativeInfinity;

        public void Add(float cycle)
        {
            Count++;
            Max = MathF.Max(Max, cycle);

#pragma warning disable S1244 // bit-for-bit, as the engine compares the stored cycle
            AtTop += cycle == TopStep ? 1 : 0;
            AtClampCeiling += cycle == ClampCeiling ? 1 : 0;
#pragma warning restore S1244
            PastOne += cycle >= 1f ? 1 : 0;
            Negative += cycle < 0f ? 1 : 0;
        }

        public override string ToString() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"samples {Count}, at 1023/1024 {AtTop}, at 0.999 {AtClampCeiling}, >= 1 {PastOne}, < 0 {Negative}, max {Max:0.#########}");
    }
}
