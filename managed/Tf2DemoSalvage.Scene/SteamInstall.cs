using System;
using System.IO;
using Microsoft.Win32;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where Steam is installed, and through its library list, where TF2 is.</summary>
/// <remarks>
/// **The one resolver for "where is the game"** — <c>MapProvider</c>, <c>Tf2ConfigFiles</c> and the
/// tests' <c>GameInstall</c> all ask this, so a fix lands once (D6).
///
/// **Found the way Steam records it, not guessed.** Until 2026-10-02 only
/// <c>Program Files (x86)\Steam</c> was tried, and a beta tester with Steam on <c>D:\Steam</c> was
/// told TF2 was not installed. The Steam client writes its own path to
/// <c>HKCU\Software\Valve\Steam</c> <c>SteamPath</c> (forward slashes), with
/// <c>HKLM\SOFTWARE\WOW6432Node\Valve\Steam</c> <c>InstallPath</c> as the machine-wide copy; those
/// come first, then the Program Files default. The registry is only ever READ.
///
/// The registry sits behind a <c>(key, value) =&gt; string?</c> seam, so tests feed values and never
/// touch the real one.
/// </remarks>
/// <param name="readRegistry">Reads one registry value, or null when absent.</param>
/// <param name="programFilesX86">The Program Files (x86) folder, or null/empty when there is none.</param>
/// <param name="overrideFolder">
/// A <c>tf</c> folder named explicitly (<see cref="OverrideVariable"/>), which beats any detection.
/// </param>
/// <param name="fileExists">Whether a file exists; null means the real file system.</param>
public sealed class SteamInstall(
    Func<string, string, string?> readRegistry,
    string? programFilesX86,
    string? overrideFolder,
    Func<string, bool>? fileExists = null)
{
    /// <summary>Names a <c>tf</c> folder that beats detection (D109's <c>TF2_FOLDER</c>).</summary>
    public const string OverrideVariable = "TF2_FOLDER";

    private const string UserKey = @"HKEY_CURRENT_USER\Software\Valve\Steam";
    private const string MachineKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam";

    /// <summary>A file every TF2 <c>tf</c> folder holds — the same recogniser the tests' <c>GameInstall</c> uses.</summary>
    /// <remarks>
    /// A file, not the folder's existence: Steam keeps a library directory for a game that has been
    /// uninstalled, so a <c>tf</c> folder can exist and hold nothing.
    /// </remarks>
    public const string Recogniser = "tf2_textures_dir.vpk";

    /// <summary>
    /// This machine: its registry, its Program Files, and the folder named by
    /// <see cref="ChosenFolder"/> — <see cref="OverrideVariable"/>, else the live <c>cl_game_folder</c>
    /// (<see cref="ConfiguredFolder"/>).
    /// </summary>
    public static SteamInstall Machine =>
        Environment.GetEnvironmentVariable(SteamRootVariable) is { Length: > 0 } steamRoot
            ? new(
                (key, name) => key == UserKey && name == "SteamPath" ? steamRoot : null,
                null,
                ChosenFolder(Environment.GetEnvironmentVariable(OverrideVariable), ConfiguredFolder()))
            : new(
                ReadWindowsRegistry,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                ChosenFolder(Environment.GetEnvironmentVariable(OverrideVariable), ConfiguredFolder()));

    /// <summary>The live <c>cl_game_folder</c>: what a ConVar's value is, not what the cfg file says.</summary>
    /// <remarks>
    /// **The viewer points this at its in-memory settings** (D210 follow-up), so <c>+cl_game_folder</c>
    /// on the command line reaches detection. Source orders it the same way: the shipped
    /// <c>cfg/valve.rc</c> runs <c>exec autoexec.cfg</c> and THEN <c>stuffcmds</c>, which executes the
    /// command line's <c>+</c> commands, so a later reader sees the command line's value over the cfg's.
    /// Without a viewer (the CLI, probes, tests) it falls back to reading the cfg file on every call, so a
    /// folder saved by the picker still reaches the next lookup.
    /// </remarks>
    public static Func<string?> ConfiguredFolder { get; set; } = () => ViewerSettings.Load().GameFolder;

    /// <summary>
    /// TEST SEAM (D210): names the only Steam folder discovery may look in, replacing the registry and
    /// Program Files. Pointed at an empty folder, discovery fails on any machine, so the UI suite can
    /// drive the not-found picker on the owner's machine as on CI. Not a user setting.
    /// </summary>
    public const string SteamRootVariable = "TF2VIEW_STEAM_ROOT";

    /// <summary>The folder that beats detection: the environment's, else the cfg's (D210).</summary>
    /// <param name="environment"><see cref="OverrideVariable"/>'s value — scripts and CI.</param>
    /// <param name="configured">The viewer cfg's <c>cl_game_folder</c> — the user's choice.</param>
    /// <returns>The folder, or null when neither names one.</returns>
    public static string? ChosenFolder(string? environment, string? configured) =>
        string.IsNullOrEmpty(environment) ? configured : environment;

    /// <summary>The <c>tf</c> folder a user picked, or null when it is not one (D210).</summary>
    /// <param name="chosen">The folder picked: <c>tf</c> itself or the <c>Team Fortress 2</c> folder above it.</param>
    /// <param name="fileExists">Whether a file exists; null means the real file system.</param>
    /// <returns>The <c>tf</c> folder holding <see cref="Recogniser"/>, or null.</returns>
    /// <remarks>
    /// The folder above is accepted because it is the one Steam's "Browse local files" opens, so it is
    /// where the obvious click lands.
    /// </remarks>
    public static string? AsGameFolder(string chosen, Func<string, bool>? fileExists = null)
    {
        Func<string, bool> exists = fileExists ?? File.Exists;

        foreach (string candidate in (string[])[chosen, Path.Combine(chosen, "tf")])
        {
            if (exists(Path.Combine(candidate, Recogniser)))
            {
                return candidate;
            }
        }

        return null;
    }

    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    /// <summary>Steam's own folder, or null when nothing names one.</summary>
    /// <remarks>
    /// The first source whose <c>libraryfolders.vdf</c> exists wins, so a stale registry value left
    /// by a moved or uninstalled Steam falls through to the next (D203). When none has one, the
    /// first source named is still the answer, so a "not installed" message names Steam's own record.
    /// </remarks>
    public string? Root
    {
        get
        {
            // SteamPath is written with forward slashes; normalise so paths compare and print the
            // way every other Windows path here does.
            string?[] named =
            [
                readRegistry(UserKey, "SteamPath")?.Replace('/', '\\'),
                readRegistry(MachineKey, "InstallPath")?.Replace('/', '\\'),
                string.IsNullOrEmpty(programFilesX86) ? null : Path.Combine(programFilesX86, "Steam"),
            ];

            string? first = null;

            foreach (string? candidate in named)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (_fileExists(LibraryFileUnder(candidate)))
                {
                    return candidate;
                }

                first ??= candidate;
            }

            return first;
        }
    }

    /// <summary>Steam's <c>libraryfolders.vdf</c>, or null when no Steam folder is named.</summary>
    public string? LibraryFile => Root is { } root ? LibraryFileUnder(root) : null;

    private static string LibraryFileUnder(string root) => Path.Combine(root, "steamapps", "libraryfolders.vdf");

    /// <summary>The game's <c>tf</c> folder, or null when it is not installed.</summary>
    /// <returns>The override when it exists, otherwise the library that lists app 440.</returns>
    public string? GameFolder()
    {
        if (overrideFolder is { Length: > 0 } chosen && Directory.Exists(chosen))
        {
            return chosen;
        }

        return LibraryFile is { } file && Root is { } root
            ? new MapLocator(file, root).FindGameFolder()
            : null;
    }

    private static string? ReadWindowsRegistry(string key, string name) =>
        OperatingSystem.IsWindows() ? Registry.GetValue(key, name, null) as string : null;
}
