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
/// Move children hung on a parent's attachment that the world pass lists BEFORE their parent, and whether their bones are
/// the ones a parent-first order gives (B729).
/// </summary>
/// <remarks>
/// **The production path** (`recycled-slot`'s wiring): `TimelineMoments`, `MomentPresenter`, `MomentScene.Drawn`. Each
/// frame's world list is also handed, unchanged except that every parent precedes its children, to a second model set —
/// the order-independent answer the engine gives. **The carried signal is `SkinningOf`**, the matrices the draw skins by,
/// for each child of an attachment in each set.
///
/// **The control is the children listed AFTER their parent**: their bones must agree between the two sets too, or the
/// second set differs for some reason other than order and nothing it says about the first group means anything. PASS
/// needs at least one child-first sighting, every one matching, and every control matching.
///
/// <code>
///   child-before-parent z1800 0 60000 11
/// </code>
/// </remarks>
public sealed class ChildBeforeParentProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "child-before-parent";

    /// <inheritdoc/>
    public string Summary =>
        "move children on an attachment listed before their parent, and whether their bones match a parent-first order " +
        "(B729): child-before-parent <demo> [from] [to] [step]";

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
        int step = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 11;

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
        models.ModelResolved = (entity, looping, staticProp, sequenceLoops) =>
            moments.Source?.OnNewModel(entity, looping, staticProp, sequenceLoops);

        EntityModelSet ordered = new() { Geometry = assets.Geometry };
        List<ModelInstance> orderedInstances = [];
        Dictionary<string, int> byModel = new(StringComparer.Ordinal);
        (int Seen, int Matched) childFirst = (0, 0);
        (int Seen, int Matched) control = (0, 0);
        double worst = 0d;
        int parented = 0;
        int onAttachment = 0;
        int parentElsewhere = 0;
        int unskinned = 0;

        for (int tick = from; tick <= to; tick += step)
        {
            moments.Show(tick, new MomentView(tick, false, null, null, 54f, Playing: true));
            moments.PoseNow();

            List<SceneProp> drawn = [.. moment.Drawn];
            List<SceneProp> parentFirst = ParentFirst(drawn);
            // `MomentInfo.Seconds`, from the interval the presenter carried for this moment.
            double seconds = (double)tick * moments.LastInterval;

            ordered.Add(parentFirst, assets.Geometry);
            ordered.PlaceFrame(parentFirst);
            ordered.UpdateClientSideAnimations(parentFirst);
            ordered.Instances(parentFirst, orderedInstances, seconds: seconds);

            Dictionary<int, int> at = [];

            for (int index = 0; index < drawn.Count; index++)
            {
                at[drawn[index].EntityIndex] = index;
            }

            for (int index = 0; index < drawn.Count; index++)
            {
                SceneProp child = drawn[index];

                if (child is { AttachedTo: not null })
                {
                    parented++;
                }

                if (child is not { AttachedTo: { } parent, AttachmentPoint: > 0, BoneMerged: false })
                {
                    continue;
                }

                onAttachment++;

                if (!at.ContainsKey(parent))
                {
                    parentElsewhere++;
                }

                if (!at.TryGetValue(parent, out int parentAt))
                {
                    continue;
                }

                // Counted, not dropped silently: a baked child has no skinning to compare (B729 review).
                if (models.SkinningOf(child.EntityIndex) is not { } mine ||
                    ordered.SkinningOf(child.EntityIndex) is not { } theirs)
                {
                    unskinned++;
                    continue;
                }

                double gap = Gap(mine.Bones, theirs.Bones);
                bool matched = gap < 1e-3;

                if (index < parentAt)
                {
                    childFirst = (childFirst.Seen + 1, childFirst.Matched + (matched ? 1 : 0));
                    byModel[child.ModelPath] = byModel.GetValueOrDefault(child.ModelPath) + 1;
                    worst = Math.Max(worst, gap);
                }
                else
                {
                    control = (control.Seen + 1, control.Matched + (matched ? 1 : 0));
                }
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ticks {from}..{to} every {step}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"drawn sightings with a move parent {parented}; on an attachment, not merged {onAttachment}, of which the parent is not in the world list {parentElsewhere}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"child listed before its parent: {childFirst.Seen}, bones matching a parent-first order {childFirst.Matched}, worst gap {worst:0.###} units"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"control, child after its parent: {control.Seen}, matching {control.Matched}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"attachment children with no skinning to compare (baked): {unskinned}"));

        foreach ((string model, int count) in byModel.OrderByDescending(static entry => entry.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,6}  {model}"));
        }

        bool pass = childFirst.Seen > 0 && childFirst.Matched == childFirst.Seen && control.Matched == control.Seen;

        if (childFirst.Seen == 0)
        {
            output.WriteLine("NONE: no child was listed before its parent here; the order does not occur in this span");
            return;
        }

        // A PASS needs its control: with no child after its parent, the instrument has compared nothing it knows the answer of.
        if (control.Seen == 0)
        {
            output.WriteLine("NO CONTROL: no child was listed after its parent, so a match proves nothing about the instrument");
            return;
        }

        output.WriteLine(pass
            ? "PASS: every child listed first has the bones a parent-first order gives, and so has every control"
            : "FAIL: see the counts above");
    }

    /// <summary>The list with every parent moved ahead of its children, otherwise in its own order.</summary>
    private static List<SceneProp> ParentFirst(List<SceneProp> drawn)
    {
        Dictionary<int, SceneProp> byEntity = [];

        foreach (SceneProp prop in drawn)
        {
            byEntity[prop.EntityIndex] = prop;
        }

        List<SceneProp> sorted = [];
        HashSet<SceneProp> added = new(ReferenceEqualityComparer.Instance);

        foreach (SceneProp prop in drawn)
        {
            Add(prop, AnimatingDepth);
        }

        return sorted;

        void Add(SceneProp prop, int budget)
        {
            if (added.Contains(prop))
            {
                return;
            }

            if (budget > 0 && prop.AttachedTo is { } parent && byEntity.TryGetValue(parent, out SceneProp? above))
            {
                Add(above, budget - 1);
            }

            if (added.Add(prop))
            {
                sorted.Add(prop);
            }
        }
    }

    private const int AnimatingDepth = 16;

    /// <summary>The largest difference between two sets of bone matrices; infinity when their shapes differ.</summary>
    private static double Gap(IReadOnlyList<float[]> mine, IReadOnlyList<float[]> theirs)
    {
        if (mine.Count != theirs.Count)
        {
            return double.PositiveInfinity;
        }

        double worst = 0d;

        for (int bone = 0; bone < mine.Count; bone++)
        {
            for (int at = 0; at < mine[bone].Length; at++)
            {
                worst = Math.Max(worst, Math.Abs(mine[bone][at] - theirs[bone][at]));
            }
        }

        return worst;
    }
}
