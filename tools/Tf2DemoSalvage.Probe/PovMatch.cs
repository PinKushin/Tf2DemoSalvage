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
    public TimelineMoments Moments(out DemoPlayer player, out RecorderPrediction prediction)
    {
        player = new DemoPlayer(Timeline);
        prediction = new RecorderPrediction(Timeline, () => Map.Level);
        return new TimelineMoments(Timeline) { Player = player, Prediction = prediction };
    }
}
