using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe;

/// <summary>The install as the viewer finds and opens it, and a demo's timeline built with it and the watcher's interp (B874).</summary>
/// <remarks>
/// **Probes run the production path** (D126): `ViewerPresenter.LoadDemoAsync` hands `DecodedDemo.Read` the install's class
/// scripts (B437) and model facts (B870) and the watcher's `cl_interp` settings, so a probe that built with none of them
/// answered a pose, cycle or activity question about a timeline the viewer never builds.
///
/// **Found as the viewer finds it**: <see cref="MapProvider.Installed"/>'s <see cref="MapProvider.GameFolder"/> —
/// `TF2_FOLDER`, then the saved `cl_game_folder` (the picker's choice), then Steam's library — with its catch of a disk that
/// fails underneath the search. Maps are found by <see cref="Map"/>, the viewer's <see cref="MapProvider.Locate"/>, which
/// searches that folder's <c>maps</c> first. **Opened as the viewer opens it**: `LevelSystems.Opened.Of` precaches the weapon
/// scripts and builds the class scripts before the install is used, and the model facts object is made here too, so no lazy
/// is counted by a probe that measures the build. What the facts read per model during a build is cached by the install;
/// a heap probe warms it first (<see cref="Warm"/>) and prints it apart from the timeline.
///
/// **The watcher's interp, as the viewer has it before any demo loads**: the saved settings, then the watcher's TF2 configs
/// run over them (`ViewerSession` attaches the console to the settings and then runs `LoadUserConfig`), unless
/// `TF2VIEW_NO_CONFIGS=1`. **Three things the viewer reads that a probe cannot**: a `+cl_interp` on the viewer's own command
/// line; a config in the program's own `custom/` folder, which sits beside the viewer's executable rather than this one;
/// and an `autoexec.cfg` that sets `cl_game_folder`, which moves the viewer's folder after the configs ran, where this
/// finds its folder once, before running them.
///
/// **No install is not a failure**: the build is the viewer's with no game, and the first writer that asks is told once,
/// because a probe on a machine without TF2 still answers every question the demo alone can. Once per process, which is a
/// CLI's: the install is opened once and kept.
/// </remarks>
public static class ProbeInstall
{
    /// <summary>What is said when the game is not found.</summary>
    public const string NoInstall =
        "INSTALL not found: no game content is read, and timelines build without the class scripts or model facts, as the viewer builds with no game";

    private static readonly System.Threading.Lock Gate = new();
    private static (string? Folder, ClientInterp Interp)? _found;
    private static GameContent? _game;
    private static bool _said;

    /// <summary>The installed game, found and opened on the first ask and kept for the process.</summary>
    /// <param name="output">Where an absent install is said: the first non-null writer to ask is told, once.</param>
    /// <returns>The install, which answers "cannot say" when the game was not found.</returns>
    public static GameContent Game(TextWriter? output)
    {
        lock (Gate)
        {
            _game ??= Open(Found().Folder, output: null);
            _said = _said || Say(_game, output);
            return _game;
        }
    }

    /// <summary>The <c>tf</c> folder the viewer would use, or null when it finds none.</summary>
    /// <param name="output">Where an absent folder is said: the first non-null writer to ask, of this or <see cref="Game"/>, is told, once.</param>
    /// <returns>The folder <see cref="Game"/> opens.</returns>
    public static string? Folder(TextWriter? output)
    {
        lock (Gate)
        {
            string? folder = Found().Folder;

            if (folder is null && output is not null && !_said)
            {
                output.WriteLine(NoInstall);
                _said = true;
            }

            return folder;
        }
    }

    /// <summary>Where the viewer would find a map: the install's own <c>maps</c> first, then Steam's library, then our downloads.</summary>
    /// <param name="name">The map, without extension.</param>
    /// <returns>Its path, or null when no search root holds it.</returns>
    /// <remarks>
    /// <see cref="MapProvider.Locate"/>, the viewer's own search, so a map under <c>TF2_FOLDER</c>'s <c>maps</c> that no
    /// Steam library lists is found here as the viewer finds it (D203). It searches what <see cref="Folder"/> found.
    /// </remarks>
    public static string? Map(string name)
    {
        using MapProvider maps = new(
            MapProvider.SteamLibraryFile,
            MapProvider.OwnMapsFolder,
            () => MapDownloader.Create(MapProvider.OwnMapsFolder),
            () => Folder(output: null));

        return maps.Locate(name);
    }

    /// <summary>Builds one timeline and drops it, so the install's per-model caches already hold what this demo reads.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="game">The install.</param>
    /// <remarks>
    /// **For a heap reading**: the model facts cache what each model tells `OnNewModel` (B870) the first time a build asks,
    /// and that cache is the install's, kept after the timeline is gone. A probe that reads the heap before and after one
    /// build counts it as "held by the timeline"; a reading taken after this counts the timeline alone, and the difference
    /// across this call is the cache, which the probe prints as its own line.
    /// </remarks>
    public static void Warm(ReadOnlyMemory<byte> file, GameContent game) => _ = Timeline(file, game);

    /// <summary>The watcher's interp: the saved settings with the TF2 configs of <see cref="Folder"/> run over them.</summary>
    /// <returns>The interp every timeline here is built with; finding it opens no install.</returns>
    public static ClientInterp Interp()
    {
        lock (Gate)
        {
            return Found().Interp;
        }
    }

    /// <summary>TEST SEAM: forgets what was found and opened, so a test that moves the environment finds again.</summary>
    internal static void Forget()
    {
        lock (Gate)
        {
            _found = null;
            _game = null;
            _said = false;
        }
    }

    // Under the gate: the folder and the interp it decides, found once.
    private static (string? Folder, ClientInterp Interp) Found()
    {
        if (_found is not { } found)
        {
            string? folder = MapProvider.Installed().GameFolder();

            found = (folder, WatcherInterp(folder, ViewerSettings.Load()));
            _found = found;
        }

        return found;
    }

    /// <summary>Opens the install at <paramref name="folder"/> as the viewer opens it, saying so when there is none.</summary>
    /// <param name="folder">The <c>tf</c> folder, or null when none was found.</param>
    /// <param name="output">Where an absent install is said.</param>
    /// <returns>The install, its class scripts and model facts already built.</returns>
    public static GameContent Open(string? folder, TextWriter? output)
    {
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        // `LevelSystems.Opened.Of`'s order: the weapon scripts read before play (B595 part 6), then the class scripts built
        // before the install escapes. The model facts object is the viewer's first decode's, made here so a probe that
        // measures a build does not count it; what it reads per model stays in the build (see `Warm`).
        game.MuzzleFlashes.Precache();
        _ = game.ClassAnimation;
        _ = game.ModelFacts;

        Say(game, output);
        return game;
    }

    /// <summary>The viewer's `cl_interp` settings before a demo loads: <paramref name="saved"/>, then the TF2 configs.</summary>
    /// <param name="folder">The <c>tf</c> folder whose configs run, or null for none.</param>
    /// <param name="saved">The viewer's saved settings.</param>
    /// <returns>The interp the viewer would hand `DecodedDemo.Read`.</returns>
    /// <remarks>
    /// `ViewerSession`'s order: the settings are attached to a defaults console, so a config's ConVar lands in them
    /// (D215, B816), and then `ConfigConsole.LoadFrom` runs `config_default.cfg`, `config.cfg` and `autoexec.cfg` (D264).
    /// </remarks>
    public static ClientInterp WatcherInterp(string? folder, ViewerSettings saved)
    {
        SettingsPresenter settings = new(NullLoggerFactory.Instance, saved);

        if (Environment.GetEnvironmentVariable(ViewerSession.NoConfigsVariable) is not "1")
        {
            ConfigConsole console = ConfigConsole.WithDefaults();

            settings.Attach(console);
            _ = console.LoadFrom(folder, NullLoggerFactory.Instance, NullLogger.Instance);
        }

        return settings.Current.Interp;
    }

    /// <summary>A demo's timeline, built with the install found by <see cref="Game"/> and the watcher's interp.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="output">Where an absent install is said, once.</param>
    /// <returns>The timeline.</returns>
    public static DemoTimeline Timeline(ReadOnlyMemory<byte> file, TextWriter? output) => Timeline(file, Game(output));

    /// <summary>A demo's timeline, built with <paramref name="game"/> and the watcher's interp.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="game">The install.</param>
    /// <returns>The timeline.</returns>
    public static DemoTimeline Timeline(ReadOnlyMemory<byte> file, GameContent game) => Timeline(file, game, Interp());

    /// <summary>A demo's timeline, built with <paramref name="game"/> and <paramref name="interp"/>, as `DecodedDemo.Read` builds it.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="game">The install.</param>
    /// <param name="interp">The watcher's `cl_interp` settings.</param>
    /// <returns>The timeline.</returns>
    public static DemoTimeline Timeline(ReadOnlyMemory<byte> file, GameContent game, ClientInterp interp)
    {
        ArgumentNullException.ThrowIfNull(game);

        return DemoTimeline.Build(file, client: interp, classes: game.ClassAnimation, models: game.ModelFacts);
    }

    private static bool Say(GameContent game, TextWriter? output)
    {
        if (!game.Archives.IsEmpty || output is null)
        {
            return false;
        }

        output.WriteLine(NoInstall);
        return true;
    }
}
