using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Probe;

/// <summary>A point-of-view demo opened as `DemoSystems.Open` opens it, with its map, for the recorder probes.</summary>
/// <param name="Timeline">The demo.</param>
/// <param name="Recorder">Its recorder's entity.</param>
/// <param name="Map">Its map.</param>
/// <param name="Assets">The map's models.</param>
/// <param name="Game">The install.</param>
/// <remarks>The prediction's `Hooks` and `GroundSurface` are left null, as in the 2026-10-26 seek-scan measurement.</remarks>
public sealed record PovMatch(DemoTimeline Timeline, int Recorder, LoadedMap Map, MapAssets Assets, GameContent Game)
{
    /// <summary>Opens a demo, or prints why it cannot be measured and answers null.</summary>
    public static PovMatch? Open(string path, TextWriter output)
    {
        System.ArgumentNullException.ThrowIfNull(output);

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Core.Container.DemoHeader.Parse(file).MapName;

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            output.WriteLine("control failed: the demo has no recorder (SourceTV?), so there is nothing to measure.");
            return null;
        }

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return null;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return null;
        }

        return new PovMatch(timeline, recorder, map, assets, game);
    }

    /// <summary>The moment source as `DemoSystems.Open` builds it.</summary>
    /// <param name="player">The demo player it holds.</param>
    /// <param name="prediction">The recorder's prediction it holds.</param>
    /// <param name="wired">
    /// Whether the prediction gets the viewer's item-attribute hooks and ground surfaces (B450), as
    /// `CorpusRecorderPredictionTests` wires them; false is the 2026-10-26 seek-scan measurement's lower-bound wiring.
    /// </param>
    public TimelineMoments Moments(out DemoPlayer player, out RecorderPrediction prediction, bool wired = false)
    {
        player = new DemoPlayer(Timeline);
        prediction = new RecorderPrediction(Timeline, () => Map.Level);

        if (wired)
        {
            AttributeHooks? hooks = Game.Weapons.Items is { } items ? new AttributeHooks(items) : null;
            prediction = new RecorderPrediction(Timeline, () => Map.Level)
            {
                Hooks = () => hooks,
                GroundSurface = trace => Map.ImpactDecals is { } decals ? Game.Surfaces.GetSurfaceData(decals.SurfacePropOfTrace(trace)) : null,
            };
        }

        return new TimelineMoments(Timeline) { Player = player, Prediction = prediction };
    }
}
