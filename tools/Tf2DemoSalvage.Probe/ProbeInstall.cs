using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe;

/// <summary>The install as the viewer opens it, and a demo's timeline built with it as the viewer builds it (B874).</summary>
/// <remarks>
/// **Probes run the production path** (D126): the viewer builds with the install's class scripts (B437) and model facts
/// (B870) — `ViewerPresenter.LoadDemoAsync` hands `DecodedDemo.Read` `game.ClassAnimation` and `game.ModelFacts` — so a
/// probe that built with neither answered a pose, cycle or activity question about a timeline the viewer never builds.
///
/// **No install is not a failure**: the build is the viewer's with no game, said once (the install is opened once per
/// process), because a probe on a machine without TF2 still answers every question the demo alone can.
/// </remarks>
public static class ProbeInstall
{
    /// <summary>What is said when the game is not found.</summary>
    public const string NoInstall =
        "INSTALL not found: timelines build without the class scripts or model facts, as the viewer builds with no game";

    private static readonly System.Threading.Lock Gate = new();
    private static GameContent? _game;

    /// <summary>The installed game, found and opened on the first ask and kept for the process.</summary>
    /// <param name="output">Where an absent install is said, once.</param>
    /// <returns>The install, which answers "cannot say" when the game was not found.</returns>
    public static GameContent Game(TextWriter? output)
    {
        lock (Gate)
        {
            return _game ??= Open(new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder(), output);
        }
    }

    /// <summary>Opens the install at <paramref name="folder"/>, saying so when there is none.</summary>
    /// <param name="folder">The <c>tf</c> folder, or null when none was found.</param>
    /// <param name="output">Where an absent install is said.</param>
    /// <returns>The install.</returns>
    public static GameContent Open(string? folder, TextWriter? output)
    {
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.IsEmpty)
        {
            output?.WriteLine(NoInstall);
        }

        return game;
    }

    /// <summary>A demo's timeline, built with the install found by <see cref="Game"/>.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="output">Where an absent install is said, once.</param>
    /// <returns>The timeline.</returns>
    public static DemoTimeline Timeline(ReadOnlyMemory<byte> file, TextWriter? output) => Timeline(file, Game(output));

    /// <summary>A demo's timeline, built with <paramref name="game"/>.</summary>
    /// <param name="file">The whole demo file.</param>
    /// <param name="game">The install.</param>
    /// <returns>The timeline.</returns>
    public static DemoTimeline Timeline(ReadOnlyMemory<byte> file, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        return DemoTimeline.Build(file, classes: game.ClassAnimation, models: game.ModelFacts);
    }
}
