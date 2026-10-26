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
    /// <summary>Opens a demo, or prints why it cannot be measured, sets a non-zero exit code and answers null.</summary>
    /// <remarks>"Not measured" must not exit like "measured": every null sets <see cref="System.Environment.ExitCode"/>.</remarks>
    public static PovMatch? Open(string path, TextWriter output)
    {
        System.ArgumentNullException.ThrowIfNull(output);

        byte[] file = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(file);
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string mapName = Core.Container.DemoHeader.Parse(file).MapName;

        if (timeline.RecorderEntityIndex is not { } recorder)
        {
            return Refused(output, "error: control failed: the demo has no recorder (SourceTV?), so there is nothing to measure.");
        }

        if (mapName.Length == 0 || locator.Find(mapName) is not { } mapPath || locator.FindGameFolder() is not { } folder)
        {
            return Refused(output, "error: the demo's map or the game could not be found.");
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance, mapPath);

        if (map.Assets is not { } assets)
        {
            return Refused(output, "error: the map loaded with no assets.");
        }

        return new PovMatch(timeline, recorder, map, assets, game);
    }

    private static PovMatch? Refused(TextWriter output, string reason)
    {
        output.WriteLine(reason);
        System.Environment.ExitCode = 1;
        return null;
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
