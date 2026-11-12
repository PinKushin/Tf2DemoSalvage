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
/// Slots recycled by a new entity with the SAME model, and whether the newcomer started as a new entity (B727).
/// </summary>
/// <remarks>
/// **The production path**, as `wire-cycle`: `TimelineMoments`, `MomentPresenter`, and `EntityModelSet.ModelResolved` wired
/// to the source as the viewer wires it. **The carried signal is `ModelResolved` itself** — the model set's
/// `OnNewModel`, fired only when the slot's state was forgotten (its record of what it reported goes with the rest), so a
/// newcomer that inherited the old occupant's state is never reported. Counted per index; a drawn prop whose index was
/// last drawn with the same model under another serial is a recycled sighting, and it is fresh when the index was
/// reported since that last sighting.
///
/// **The control is the continuing entities**: one drawn again under the same serial and model must NOT have been
/// reported again — a set that forgot every frame would pass the first half. PASS needs a recycled sighting, every one
/// fresh, and no continuing one reset.
///
/// <code>
///   recycled-slot z1800 0 60000 11
/// </code>
/// </remarks>
public sealed class RecycledSlotProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "recycled-slot";

    /// <inheritdoc/>
    public string Summary =>
        "slots reused by a new entity with the same model, and whether it started fresh, with a PASS/FAIL (B727): " +
        "recycled-slot <demo> [from] [to] [step]";

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

        Dictionary<int, int> reports = [];

        // The viewer's own wiring (`ViewerPresenter`), counted per entity on the way through.
        models.ModelResolved = (entity, looping, staticProp, sequenceLoops) =>
        {
            reports[entity] = reports.GetValueOrDefault(entity) + 1;
            moments.Source?.OnNewModel(entity, looping, staticProp, sequenceLoops);
        };

        Dictionary<int, (int Serial, string Model, int Reports)> last = [];
        Dictionary<string, (int Fresh, int Stale)> byModel = new(StringComparer.Ordinal);
        int fresh = 0;
        int stale = 0;
        int continuing = 0;
        int continuingReset = 0;

        for (int tick = from; tick <= to; tick += step)
        {
            moments.Show(tick, new MomentView(tick, false, null, null, 54f, Playing: true));
            moments.PoseNow();

            foreach (SceneProp prop in moment.Drawn)
            {
                int now = reports.GetValueOrDefault(prop.EntityIndex);

                if (last.TryGetValue(prop.EntityIndex, out (int Serial, string Model, int Reports) was) &&
                    string.Equals(was.Model, prop.ModelPath, StringComparison.Ordinal) &&
                    assets.Geometry(prop.ModelPath) is not null)
                {
                    bool reported = now > was.Reports;

                    if (was.Serial == prop.SerialNumber)
                    {
                        continuing++;
                        continuingReset += reported ? 1 : 0;
                    }
                    else
                    {
                        (int Fresh, int Stale) seen = byModel.GetValueOrDefault(prop.ModelPath);
                        byModel[prop.ModelPath] = reported ? (seen.Fresh + 1, seen.Stale) : (seen.Fresh, seen.Stale + 1);
                        fresh += reported ? 1 : 0;
                        stale += reported ? 0 : 1;
                    }
                }

                last[prop.EntityIndex] = (prop.SerialNumber, prop.ModelPath, now);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ticks {from}..{to} every {step}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"recycled same-model sightings {fresh + stale}: fresh {fresh}, inherited {stale}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"continuing sightings {continuing}: reset {continuingReset}"));

        foreach ((string model, (int Fresh, int Stale) count) in byModel.OrderByDescending(entry => entry.Value.Fresh + entry.Value.Stale))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count.Fresh,5} fresh {count.Stale,5} inherited  {model}"));
        }

        bool pass = fresh > 0 && stale == 0 && continuing > 0 && continuingReset == 0;

        output.WriteLine(pass
            ? "PASS: every same-model newcomer was told its model as a new entity; no continuing entity was"
            : "FAIL: see the counts above (no recycled or no continuing sighting means the instrument saw nothing)");
    }
}
