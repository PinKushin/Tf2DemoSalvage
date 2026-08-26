using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the installed game provides, opened once and asked many times.</summary>
/// <remarks>
/// **Named for the CONTENT rather than the install, because `GameInstall` was already taken** — by
/// `Tf2DemoSalvage.SdkReference.GameInstall`, a static locator that finds the `tf` folder for the
/// conformance suites. Two types called `GameInstall` in one solution compiles wherever their
/// namespaces do not meet and is confusing everywhere, so this one says what it holds: the archives,
/// the palette, the class scripts and the item schema, already opened.
///
/// **This was <c>MainForm.ReadMap</c>'s "first map only" branch, plus <c>LoadEntityPalette</c> and
/// <c>FindGameFolder</c>** (B188, D90). None of it is per-map and none of it is window work — it is
/// the archives, the editor palette, the class scripts, the item schema and the soundscape catalog,
/// every one of which comes off disk once and is asked on every frame afterwards.
///
/// **It sat inside a map read because that is where the first caller happened to be**, which is the
/// drift D89 names: the home was chosen by proximity rather than by what the thing is.
///
/// **Every member degrades to "cannot say" rather than throwing.** The viewer is meant to open a
/// demo on a machine that has never had TF2 — the map outline still draws, the players still move,
/// and what could not be found is reported. Refusing here would refuse exactly the salvage cases
/// this project exists for (D5).
/// </remarks>
public sealed class GameContent
{
    private GameContent(
        GameArchives archives,
        FgdClasses? entityClasses,
        PlayerClassModels? classes,
        WeaponModels weapons)
    {
        Archives = archives;
        EntityClasses = entityClasses;
        Classes = classes;
        Weapons = weapons;
    }

    /// <summary>Every content source: the VPKs, plus any loose folders beside them.</summary>
    public GameArchives Archives { get; }

    /// <summary>Valve's entity palette from the shipped FGDs, or null when there are none.</summary>
    /// <remarks>
    /// **Editor data, so a dedicated-server or content-only copy has none.** Losing it costs one
    /// colour in one diagnostic view, which is why its absence is reported rather than fatal.
    /// </remarks>
    public FgdClasses? EntityClasses { get; }

    /// <summary>The class scripts, which is where a player's model actually comes from.</summary>
    /// <remarks>
    /// ICE-encrypted KeyValues in the install. Nothing in a demo carries a player's model path
    /// unless the server overrode it, so the class number on the wire is all that is needed — and
    /// this is what turns it into a model.
    /// </remarks>
    public PlayerClassModels? Classes { get; }

    /// <summary>What model is in a player's hands, from <c>items_game.txt</c>.</summary>
    /// <remarks>A real resolver that answers nothing when there is no install, never null (D83).</remarks>
    public WeaponModels Weapons { get; }

    // **The soundscape catalog is NOT here, deliberately.** It reads from these same archives, so it
    // looks like it belongs — but it lives in the Audio project, and putting it here would mean
    // Scene referencing Audio: a sideways model-to-model edge that forbids nothing, which is the
    // test D92 sets for adding one. The audio side loads it from `Archives.Read` instead.

    /// <summary>Opens the install, reporting whatever it could not find.</summary>
    /// <param name="folder">The <c>tf</c> folder, or null when none was located.</param>
    /// <param name="loggers">Where each piece reports itself, by category (D83).</param>
    /// <returns>The install, which is never null and answers "cannot say" when empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggers"/> is null.</exception>
    public static GameContent Open(string? folder, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);

        ILogger assets = loggers.CreateLogger("assets");
        ILogger render = loggers.CreateLogger("render");

        assets.LogInformation("{Message}", $"game folder: {folder ?? "not found"}");

        GameArchives archives = GameArchives.Open(folder);

        assets.LogInformation(
            "{Message}",
            archives.IsEmpty
                ? "content sources: none"
                : $"content sources: archives plus {archives.FolderCount.ToString(CultureInfo.InvariantCulture)} folders");

        PlayerClassModels? classes = archives.IsEmpty
            ? null
            : PlayerClassModels.Read(archives.Read);

        if (classes is not null)
        {
            assets.LogInformation(
                "{Message}", $"class models: {string.Join(", ", ModelPaths(classes))}");
        }

        return new GameContent(
            archives,
            ReadEntityPalette(folder, assets),
            classes,
            archives.IsEmpty
                ? WeaponModels.None(render)
                : new WeaponModels(archives.Read, render));
    }

    /// <summary>The model every playable class wears.</summary>
    /// <remarks>
    /// **Read from the install rather than listed here.** <c>CTFPlayerClassShared::GetModelName</c>
    /// returns <c>m_iszCustomModel</c> when a server has overridden it and otherwise
    /// <c>GetPlayerClassData( m_iClass )-&gt;GetModelName()</c>, which is the class script — so the
    /// class number is the only thing a demo needs to carry, and it does.
    ///
    /// The custom model is networked and is NOT honoured yet: nothing decodes
    /// <c>m_iszCustomModel</c>, so a server that replaced a player's model draws the stock one.
    /// Rare outside events and plugins, and stated rather than hidden.
    /// </remarks>
    public IEnumerable<string> ModelPaths()
    {
        if (Classes is not { } models)
        {
            yield break;
        }

        foreach (string path in ModelPaths(models))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> ModelPaths(PlayerClassModels models)
    {
        for (int playerClass = PlayerClassModels.FirstClass;
            playerClass <= PlayerClassModels.LastPlayingClass;
            playerClass++)
        {
            if (models.Model(playerClass) is { } model)
            {
                yield return model;
            }
        }
    }

    /// <summary>Reads Valve's entity palette out of the FGDs beside the game.</summary>
    /// <remarks>
    /// **Best effort, and silent about being absent rather than about failing.** The FGDs are editor
    /// data: a game install has them, and a dedicated-server or content-only copy may not. Losing
    /// them costs one colour in one diagnostic view, so it must not interrupt opening a demo — but a
    /// file that exists and will not parse is a different thing and says so.
    /// </remarks>
    private static FgdClasses? ReadEntityPalette(string? folder, ILogger assets)
    {
        if (folder is null || Path.GetDirectoryName(folder) is not { } install)
        {
            return null;
        }

        string bin = Path.Combine(install, "bin");
        List<string> read = [];

        // In mount order, so a later file's redefinition wins — which is what tf.fgd's own
        // `@include "base.fgd"` amounts to without needing to resolve includes.
        foreach (string name in PaletteFiles)
        {
            string path = Path.Combine(bin, name);

            try
            {
                if (File.Exists(path))
                {
                    read.Add(File.ReadAllText(path));
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                assets.LogWarning(failure, "{Message}", $"reading {path}");
            }
        }

        if (read.Count == 0)
        {
            assets.LogInformation(
                "{Message}", $"no FGD files in {bin}; entities draw as brushwork");

            return null;
        }

        FgdClasses classes = FgdClasses.Parse([.. read]);

        assets.LogInformation(
            "{Message}",
            $"entity palette: {classes.Count.ToString(CultureInfo.InvariantCulture)} classes from " +
            $"{read.Count.ToString(CultureInfo.InvariantCulture)} FGD files");

        return classes;
    }

    /// <summary>The FGDs, in mount order.</summary>
    private static readonly string[] PaletteFiles = ["base.fgd", "halflife2.fgd", "tf.fgd"];
}
