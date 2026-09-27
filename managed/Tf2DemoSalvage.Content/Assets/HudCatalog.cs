using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One HUD found in the program's own <c>custom/</c> folder.</summary>
/// <param name="Name">A label for the picker — the folder or file name, without its extension.</param>
/// <param name="Path">The folder or <c>.vpk</c> to pass to <see cref="GameArchives.WithHud"/>.</param>
public readonly record struct HudOption(string Name, string? Path);

/// <summary>
/// Lists every HUD in the program's own <c>custom/</c> folder — a folder or <c>.vpk</c> carrying
/// <c>resource/</c> or <c>scripts/</c> at its root (D193).
/// </summary>
/// <remarks>
/// **This is OUR OWN <c>custom/</c>, not <c>tf/custom</c>.** The owner: *"id rather huds players
/// 'import' or copy into our program be in the custom folder, we can sub folder"* — so a HUD a
/// player imports lands beside the program, and this is what the picker enumerates. `tf/custom` is
/// the game's own and is never read for the picker (`GameArchives.WithoutCustom`); a chosen HUD is
/// searched by <see cref="GameArchives.WithHud"/> the same way either kind installs.
///
/// **Subfolders are allowed**, so a player who drops several HUDs each into their own folder, or
/// nests one inside another folder they made, still gets every one listed. The search does not
/// descend into a folder once it has already qualified as a HUD — its own <c>resource/</c> and
/// <c>scripts/</c> are what made it one, not somewhere to keep looking.
/// </remarks>
public static class HudCatalog
{
    private static readonly string[] HudMarkers = ["resource", "scripts"];

    /// <summary>Every HUD found under <paramref name="customRoot"/>.</summary>
    /// <param name="customRoot">The program's own <c>custom/</c> folder, or null.</param>
    /// <returns>Each HUD's folder or <c>.vpk</c> path, in no particular order; empty when there is none.</returns>
    public static IReadOnlyList<HudOption> Find(string? customRoot)
    {
        List<HudOption> found = [];

        if (string.IsNullOrWhiteSpace(customRoot) || !Directory.Exists(customRoot))
        {
            return found;
        }

        FindInto(customRoot, found);

        return found;
    }

    private static void FindInto(string folder, List<HudOption> found)
    {
        if (IsHudFolder(folder))
        {
            found.Add(new HudOption(Path.GetFileName(folder.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), folder));

            // A HUD's own resource/scripts folders are not themselves nested HUDs.
            return;
        }

        IEnumerable<string> entries;

        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder);
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string entry in entries)
        {
            if (Directory.Exists(entry))
            {
                FindInto(entry, found);
            }
            else if (entry.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase) && IsHudVpk(entry))
            {
                found.Add(new HudOption(Path.GetFileNameWithoutExtension(entry), entry));
            }
        }
    }

    private static bool IsHudFolder(string folder) =>
        HudMarkers.Any(marker => Directory.Exists(Path.Combine(folder, marker)));

    private static bool IsHudVpk(string path)
    {
        try
        {
            VpkArchive archive = VpkArchive.Open(path);

            return archive.Paths.Any(entry =>
                HudMarkers.Any(marker => entry.StartsWith(marker + "/", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception failure) when (
            failure is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The picker's full list: TF2's stock HUD first, then every HUD found.</summary>
    /// <param name="customRoot">The program's own <c>custom/</c> folder, or null.</param>
    /// <returns>"TF2 default" (a null path) followed by <see cref="Find"/>'s results.</returns>
    public static IReadOnlyList<HudOption> Options(string? customRoot) =>
        [new HudOption("TF2 default", null), .. Find(customRoot)];
}
