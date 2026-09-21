using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>One file out of the game's content, by the path the engine would ask for.</summary>
/// <remarks>
/// **The game's shipped data is a source, and it is the one nobody thinks of** — `CLAUDE.md` names it as the fifth,
/// and it has already answered two questions filed as needing a decompiler. But every existing probe that reaches
/// it does so for a particular KIND of file: `vmt` prefixes `materials/` and appends `.vmt`, `particles` only walks
/// `.pcf`s, `weapon-script` only looks under `scripts/`. Asking "what does `particles/particles_manifest.txt`
/// contain" had no answer that was not a one-off edit to one of them.
///
/// **Through `GameArchives`, which is the production search path** — `gameinfo.txt` order, `custom/` and loose
/// files ahead of the VPKs — so this answers what the VIEWER would read, not what happens to be in one archive.
///
/// <code>
///   game-file particles/particles_manifest.txt      — print it as text
///   game-file &lt;path&gt; hex [bytes]                    — print it as hex, for a binary
/// </code>
/// </remarks>
public sealed class GameFileProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "game-file";

    /// <inheritdoc/>
    public string Summary =>
        "one file out of the game's content, by the engine's own path: game-file <path> [hex [bytes]]";

    /// <summary>How much of a binary to dump when no length is given.</summary>
    private const int DefaultHexBytes = 256;

    /// <summary>Bytes per hex row.</summary>
    private const int Row = 16;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("Usage: game-file <path> [hex [bytes]]");
            return;
        }

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);

        if (archives.Read(arguments[0]) is not { Length: > 0 } content)
        {
            output.WriteLine($"'{arguments[0]}' is not in the game's content.");

            // **The control an absence needs.** A miss is far more likely to be a mistyped path than a file the
            // game does not ship (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).
            output.WriteLine(
                archives.Read("scripts/items/items_game.txt") is null
                    ? "  and neither is items_game.txt, so the SEARCH PATH is broken, not the path asked for."
                    : "  items_game.txt reads, so the search path works and this file is absent or misspelled.");

            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{arguments[0]}  ({content.Length} bytes)"));
        output.WriteLine();

        if (arguments.Count > 1 && arguments[1].Equals("hex", StringComparison.OrdinalIgnoreCase))
        {
            Hex(output, content, arguments.Count > 2
                ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
                : DefaultHexBytes);

            return;
        }

        output.WriteLine(Encoding.UTF8.GetString(content).TrimEnd('\0'));
    }

    /// <summary>The first bytes, as hex and printable text.</summary>
    private static void Hex(TextWriter output, byte[] content, int bytes)
    {
        int limit = Math.Min(bytes, content.Length);

        for (int at = 0; at < limit; at += Row)
        {
            int run = Math.Min(Row, limit - at);

            StringBuilder hex = new();
            StringBuilder text = new();

            for (int one = 0; one < run; one++)
            {
                byte value = content[at + one];

                hex.Append(value.ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
                text.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  {at:x6}  {hex,-48} {text}"));
        }
    }
}
