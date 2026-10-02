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

    /// <summary>This machine: its registry, its Program Files, its <see cref="OverrideVariable"/>.</summary>
    public static SteamInstall Machine => new(
        ReadWindowsRegistry,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetEnvironmentVariable(OverrideVariable));

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
