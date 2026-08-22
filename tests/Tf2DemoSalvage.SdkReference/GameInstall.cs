using System;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// Where Team Fortress 2 is installed, for the tests that read what the game ships.
/// </summary>
/// <remarks>
/// **The same argument <see cref="SourceSdk"/> makes, at a larger scale.** That type exists because
/// three files each carried their own copy of the SDK path; this one exists because SEVENTY-THREE
/// carry their own copy of the install path, each with its own list of Steam library roots and its
/// own idea of which file proves the folder is really the game.
///
/// **One of those copies was already corrupt, and it failed silently.** `BspModelsTests` held
///
/// <code>
/// @"F:SteamLibrarysteamappsmmonTeam Fortress 2	f"
/// </code>
///
/// — every backslash eaten and <c>\t</c> collapsed to a literal tab, the signature of an edit made
/// by a script rather than by a file tool. A path that cannot exist makes
/// <c>File.Exists</c> false, which sends the test down its `Assert.Ignore` branch, which reports as
/// a skip rather than a failure. The test had stopped measuring the map entirely and nothing said
/// so. That is the whole case for one copy: a hardcoded path is a claim about a machine, and a claim
/// repeated seventy-three times is one that cannot be checked.
///
/// **Shipped game data is a source in its own right**, not merely test scaffolding — VMTs, `.res`
/// files and soundscript headers have answered questions that were filed as needing a decompiler.
/// Reading the owner's install is exactly that: nothing here copies game content into the
/// repository, and the install is only ever read.
/// </remarks>
public static class GameInstall
{
    /// <summary>A file that only the real <c>tf</c> folder has, used to recognise it.</summary>
    /// <remarks>
    /// Chosen over <c>tf2_misc_dir.vpk</c> because a partial or content-stripped install may lack
    /// one archive and still have another; the textures archive is present in every install this
    /// has been checked against. It is a recogniser, not a dependency — a caller that needs a
    /// particular archive still checks for that archive.
    /// </remarks>
    private const string Recogniser = "tf2_textures_dir.vpk";

    /// <summary>The <c>tf</c> folder, or null when the game is not installed here.</summary>
    /// <remarks>
    /// <c>TF2_FOLDER</c> overrides, so a machine that keeps its library elsewhere runs these tests
    /// rather than skipping them. The fallbacks are the ordinary Steam locations; a machine with
    /// none of them gets null and the caller skips.
    /// </remarks>
    public static string? Root =>
        new[]
        {
            Environment.GetEnvironmentVariable("TF2_FOLDER"),
            @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
            @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
        }
        .FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            File.Exists(Path.Combine(candidate, Recogniser)));

    /// <summary>Whether a test that reads game data can run at all.</summary>
    public static bool Available => Root is not null;

    /// <summary>The reason to give when skipping.</summary>
    public const string Missing =
        "Team Fortress 2 is not installed; set TF2_FOLDER to an install to run this.";

    /// <summary>An absolute path under the install, or null when it is not there.</summary>
    /// <param name="relativePath">Path under <c>tf</c>, such as <c>maps/cp_process_final.bsp</c>.</param>
    /// <returns>The absolute path, or null when the install or that file is absent.</returns>
    /// <remarks>
    /// Returns null rather than throwing for the same reason <see cref="SourceSdk.Text"/> does: a
    /// machine without the file should skip the test, and a caller that treats null as "skip" reads
    /// better than one wrapping every path in its own existence check.
    ///
    /// **Existence is checked here rather than left to the caller** precisely because the corrupt
    /// path above proved the check is where the silence gets in.
    /// </remarks>
    public static string? Find(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (Root is not { } root)
        {
            return null;
        }

        string full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(full) ? full : null;
    }

    /// <summary>An absolute path to one of the game's VPK directory files, or null.</summary>
    /// <param name="archive">The archive's name, such as <c>tf2_misc</c>.</param>
    /// <returns>The path to its <c>_dir.vpk</c>, or null when absent.</returns>
    /// <remarks>
    /// Kept separate from <see cref="Find"/> because every caller spells the suffix itself
    /// otherwise, and a misspelt suffix skips rather than fails — the same silent-skip trap the
    /// corrupt path above fell into.
    /// </remarks>
    public static string? Vpk(string archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        return Find($"{archive}_dir.vpk");
    }
}
