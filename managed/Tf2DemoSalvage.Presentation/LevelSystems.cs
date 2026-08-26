using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.GameSystems;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Tells every system about a newly-read level, and about the one being torn down.</summary>
/// <remarks>
/// **This was the wiring half of <c>MainForm.ReadMap</c> and all of <c>ClearMap</c>'s system half**
/// (B188, D90). Its line count was the least interesting thing about it: almost none of it read a
/// map — <see cref="LoadedMap.Read"/> already did that — and the rest told six collaborators about
/// the result, one statement each. **That is exactly the class of code B193 and B196 keep breaking**:
/// a system that stops being told does not fail, it keeps answering with whatever it last held.
///
/// **Valve's shape, and its name.** `LevelInitPreEntityAllSystems( pMapName )`
/// (`game/shared/igamesystem.h:77`) walks a LIST of registered systems and lets each initialise
/// itself. So does this. A new system is added by implementing <see cref="IGameSystem"/> and
/// appearing in the list, not by finding every place a level is loaded.
///
/// **Only three of our types are game systems, and checking that was the point.** Valve models the
/// renderables-list builder as one — `IClientLeafSystem : IClientLeafSystemEngine, IGameSystemPerFrame`
/// (`clientleafsystem.h:135`) — along with `C_SoundscapeSystem` and `CSoundEmitterSystem`. It does
/// NOT model model-geometry or the sound cache that way: `IVModelInfo` and `IEngineSound` are plain
/// `abstract_class` interfaces, set up once at init. So <see cref="EntityModelSet"/> and
/// <see cref="SoundCache"/> are configured here rather than walked, which is why their sources are
/// settable properties in the first place.
///
/// **The payload is assigned outside the walk, because Valve's hooks carry none.**
/// `LevelInitPreEntity()` takes no parameters; a system pulls what it needs from globals, and we
/// have none. So `Load` hands each system its data explicitly and THEN walks the list — the walk
/// carries the lifecycle, the assignments carry the level.
/// </remarks>
public sealed class LevelSystems
{
    private readonly MomentScene _moment;
    private readonly EntityModelSet _models;
    private readonly SoundCache _sounds;
    private readonly SoundscapeSystem _soundscape;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _audio;
    private readonly IReadOnlyList<IGameSystem> _systems;

    /// <summary>Binds the systems that will be told about levels.</summary>
    /// <param name="moment">The scene rebuilt for each tick; Valve's client leaf system.</param>
    /// <param name="models">The packed entity geometry; Valve's <c>modelinfo</c>, not a system.</param>
    /// <param name="sounds">The sample cache; Valve's <c>enginesound</c>, not a system.</param>
    /// <param name="soundscape">The ambience system.</param>
    /// <param name="sound">The sound emitter.</param>
    /// <param name="loggers">Where each system's own log goes.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    /// <remarks>
    /// **Every collaborator is required.** A null one is a system that silently stops being told,
    /// which is the failure this type exists to prevent — so it is refused at construction, the
    /// earliest point at which the caller still has a stack that names the mistake.
    /// </remarks>
    public LevelSystems(
        MomentScene moment,
        EntityModelSet models,
        SoundCache sounds,
        SoundscapeSystem soundscape,
        SoundPresenter sound,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(soundscape);
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(loggers);

        _moment = moment;
        _models = models;
        _sounds = sounds;
        _soundscape = soundscape;
        _loggers = loggers;
        _audio = loggers.CreateLogger("audio");

        // The registered list, in the order the engine would have added them. `EntityModelSet` and
        // `SoundCache` are absent on purpose — see the note on the type.
        //
        // `sound` is held only here rather than as a field: the emitter is walked like every other
        // system and nothing else in this type addresses it directly.
        _systems = [moment, soundscape, sound];
    }

    /// <summary>The systems this walks, in registration order.</summary>
    /// <remarks>
    /// Exposed so a test can assert WHICH systems are registered rather than only that a load ran.
    /// A system quietly dropped from the list is the exact regression this type exists to prevent,
    /// and a count is the only thing that notices it.
    /// </remarks>
    public IReadOnlyList<IGameSystem> Systems => _systems;

    /// <summary>Tells the systems about a newly-opened game install.</summary>
    /// <param name="game">What the install provides.</param>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// **Once per process, on the first map read rather than at startup**, because finding and
    /// opening the archives is slow and a viewer with no demo open needs none of it. That lateness
    /// is also why <see cref="DemoAppearance"/> has to build lazily.
    ///
    /// **The soundscape catalog is null when there are no archives, not empty.** An empty catalog
    /// would claim the install HAS no soundscapes, which is a statement about TF2 rather than about
    /// whether we could read it.
    /// </remarks>
    public void OpenGame(GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        _sounds.Read = game.Archives.Read;
        _moment.Weapons = game.Weapons;

        _soundscape.Catalog = game.Archives.IsEmpty
            ? null
            : SoundscapeCatalog.Load(game.Archives.Read);
    }

    /// <summary>Reads a level and tells every system about it.</summary>
    /// <param name="bytes">The BSP.</param>
    /// <param name="game">What the install provides.</param>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="textureQuality">How far to downscale textures.</param>
    /// <param name="colourByClass">Whether to build the surface-category view.</param>
    /// <returns>The level, for the caller to keep.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <exception cref="System.IO.InvalidDataException">The file is not a readable BSP.</exception>
    public LoadedMap Load(
        ReadOnlyMemory<byte> bytes,
        GameContent game,
        DemoTimeline? timeline,
        int textureQuality,
        bool colourByClass)
    {
        ArgumentNullException.ThrowIfNull(game);

        LoadedMap map = LoadedMap.Read(bytes, game, timeline, textureQuality, colourByClass, _loggers);

        // **The payload, assigned before the walk.** Valve's hooks take no arguments because a
        // system reads globals; ours are handed their data here and then told the level has begun.
        _moment.Lighting = map.Lighting;

        _models.Geometry = map.Assets is { } content
            ? content.Geometry
            : EntityModelSet.NoGeometry;

        _soundscape.Placements = _soundscape.Catalog is { } loaded
            ? SoundscapePlacements.From(map.Level.Entities, loaded, map.Level.Leaves)
            : null;

        _soundscape.Leaves = map.Level.Leaves;
        _soundscape.Visibility = map.Level.Visibility;

        foreach (IGameSystem system in _systems)
        {
            system.LevelInitPreEntity();
        }

        foreach (IGameSystem system in _systems)
        {
            system.LevelInitPostEntity();
        }

        Report(map);

        return map;
    }

    /// <summary>Tells every system the level is going away.</summary>
    /// <remarks>
    /// **The half that did not exist before, and its absence was a real asymmetry.** Teardown was
    /// split across two places: `ClearMap` reset the model geometry and the scene's upload flag,
    /// `Load` cleared the soundscape inline, and nothing tore down the sound schedule at all. Two
    /// of the systems in one place, one in another, one nowhere — and adding a fifth meant guessing
    /// which. Valve has `LevelShutdownPreEntity` and `LevelShutdownPostEntity` for exactly this.
    ///
    /// **Reverse registration order**, as a teardown should be: a system that was told last is told
    /// first that the level is going, so nothing is dismantled out from under something still using
    /// it.
    /// </remarks>
    public void Shutdown()
    {
        foreach (IGameSystem system in _systems.Reverse())
        {
            system.LevelShutdownPreEntity();
        }

        foreach (IGameSystem system in _systems.Reverse())
        {
            system.LevelShutdownPostEntity();
        }

        // Not a game system, so it is reset here rather than walked — `IVModelInfo` is an interface
        // the engine sets up once, and the geometry source is the same shape.
        _models.Geometry = EntityModelSet.NoGeometry;
    }

    /// <summary>Says what the level gave the ambience system to work with.</summary>
    /// <remarks>
    /// **Both lines describe a CAPABILITY rather than an event**, which is why they stay at
    /// `Information`: they run once per map and they are what a later "why is the ambience wrong"
    /// question is answered from. Without the visibility line, a soundscape chosen from across the
    /// map is indistinguishable from a chooser that ignores distance.
    /// </remarks>
    private void Report(LoadedMap map)
    {
        _audio.LogInformation(
            "{Message}",
            map.Level.Visibility is { HasData: true } pvs
                ? $"visibility: {pvs.ClusterCount.ToString(CultureInfo.InvariantCulture)} " +
                  "clusters, so soundscape selection is restricted to what the listener can see"
                : "no visibility data, so every soundscape on the map contends");

        _audio.LogInformation(
            "{Message}",
            _soundscape.Placements is { } placed
                ? $"{placed.Placements.Count} soundscape placements, " +
                  string.Join(
                      ", ",
                      placed.Placements
                          .GroupBy(placement => placement.Name)
                          .Select(group => $"{group.Count()}x {group.Key}"))
                : "no archives, so no soundscapes");
    }
}
