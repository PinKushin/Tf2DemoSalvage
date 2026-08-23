using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Finds the player's own TF2 configs, loose or inside a VPK, in the order the engine execs them.
/// </summary>
/// <remarks>
/// **This is the half of D69 that makes it a feature rather than a capability.** `ConfigConsole` can
/// run a config; without something that goes and gets one, the viewer runs its own defaults for ever
/// and every test of the interpreter is a test of code nothing reaches.
///
/// **VPKs come free, which is the whole reason this goes through <see cref="GameArchives"/>.** The
/// owner asked for configs "in .cfg or vpk form like comfig's configs", and mastercomfig ships
/// exactly that: a `.vpk` under `tf/custom/` containing `cfg/*.cfg`. `GameArchives` already mounts
/// `tf/custom/*` above the game's own files and reads through both with one call, because it was
/// built to resolve materials the same way. Nothing here needs to know a VPK exists.
///
/// **The priority is Valve's, not ours.** `tf/custom/*` outranks the stock files, which is why a
/// mastercomfig pack wins over `config_default.cfg` without this file arranging anything.
/// </remarks>
public static class Tf2ConfigFiles
{
    /// <summary>The configs the engine execs, earliest first.</summary>
    /// <remarks>
    /// **The order is `valve.rc`'s and it matters, because a config is executed rather than
    /// merged.** `config_default.cfg` supplies the stock bindings, `config.cfg` is what the engine
    /// last wrote for this player, and `autoexec.cfg` is what they wrote by hand — so each layer
    /// overrides the one before it, and the hand-written file wins.
    ///
    /// **`autoexec.cfg` last is also where the aliases live**, which is the case that broke the
    /// first implementation: `config.cfg` binds `w` to `+mfwd` and never says what `+mfwd` is.
    /// Reading either file alone finds no movement bindings at all.
    /// </remarks>
    public static IReadOnlyList<string> Order { get; } =
        ["cfg/config_default.cfg", "cfg/config.cfg", "cfg/autoexec.cfg"];

    /// <summary>Reads whichever of the player's configs exist.</summary>
    /// <param name="gameFolder">The <c>tf</c> folder, or null to read nothing.</param>
    /// <param name="log">Where to report what was found, or null.</param>
    /// <returns>The configs' text in exec order; empty when the game is not installed.</returns>
    /// <remarks>
    /// **Missing files are normal and are not errors.** A fresh install has no `config.cfg` until
    /// the game has run once, and most players have never created an `autoexec.cfg` at all. The
    /// viewer must start regardless — its own defaults are a working set of controls.
    /// </remarks>
    public static IReadOnlyList<string> Read(string? gameFolder, Action<string, string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            return [];
        }

        GameArchives archives = GameArchives.Open(gameFolder, log);
        List<string> configs = [];

        foreach (string path in Order)
        {
            if (Read(archives, path) is not { } text)
            {
                continue;
            }

            configs.Add(text);
            log?.Invoke("config", $"{path}: {text.Length} characters");
        }

        return configs;
    }

    /// <summary>One config's text, or null when it is not there.</summary>
    /// <remarks>
    /// **Decoded as UTF-8 with the BOM stripped rather than as ASCII.** A config carries player
    /// names, `say` binds and server addresses, and this project has been bitten before by an ASCII
    /// decoder turning an international name into a plausible different one rather than into an
    /// error — see `docs/memory/international-names-are-required.md`.
    ///
    /// **Invalid bytes are replaced rather than thrown on.** A config edited in a legacy code page
    /// is a file the engine still reads; losing one character from a `say` bind costs nothing, and
    /// refusing the file would cost the player every binding in it.
    /// </remarks>
    private static string? Read(GameArchives archives, string path)
    {
        byte[]? bytes = archives.Read(path);

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> span = bytes;

        // UTF-8 BOM. Notepad writes one, and left in place it becomes part of the first token —
        // which turns a leading `unbindall` into an unknown command that is skipped in silence.
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(span);
    }

    /// <summary>Where a stock Windows Steam install keeps the <c>tf</c> folder.</summary>
    /// <remarks>
    /// **A guess, and it is allowed to be wrong.** Steam libraries move to other drives constantly,
    /// so this is a default for the common case rather than a discovery mechanism — the viewer takes
    /// the folder from its settings when one is configured, and falls back to here. Reporting "no
    /// config found" is a correct outcome, not a failure.
    /// </remarks>
    public static string? DefaultGameFolder
    {
        get
        {
            string? programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);

            if (string.IsNullOrEmpty(programFiles))
            {
                return null;
            }

            string guess = Path.Combine(
                programFiles, "Steam", "steamapps", "common", "Team Fortress 2", "tf");

            return Directory.Exists(guess) ? guess : null;
        }
    }
}
