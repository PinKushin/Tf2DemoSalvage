using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every sub-block a shipped VMT opens, counted, with the parameters they hide (B326).
/// </summary>
/// <remarks>
/// **The denominator for a gap found on one material.** `gold_player.vmt` declares `$envmap` inside
/// a `">=DX90"` block and this project's VMT reader does not descend into those, so the material
/// arrives with `$envmaptint` and no cubemap. The question that decides what to do about it is not
/// answerable by reading one file: how many materials do this, which block names do they actually
/// use, and which parameters are behind them.
///
/// So this walks every `.vmt` the archives ship and tallies the name of every block opened inside
/// the shader's own — the depth this reader currently drops. `Proxies` is expected and is reported
/// with the rest rather than filtered out, as its own control: a run that reported no `Proxies` at
/// all would be a broken scan rather than a finding
/// (`docs/memory/an-empty-search-needs-a-control.md`).
///
/// <code>
///   vmt-blocks              — every block name, by count
///   vmt-blocks ">=DX90"     — and the parameters that one hides, by count
/// </code>
/// </remarks>
public sealed class MaterialBlockProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vmt-blocks";

    /// <inheritdoc/>
    public string Summary =>
        "sub-blocks shipped VMTs open, and the parameters they hide: vmt-blocks [block name]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        string? wanted = arguments.Count > 0 ? arguments[0] : null;

        Dictionary<string, int> blocks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> hidden = new(StringComparer.OrdinalIgnoreCase);

        // **Keys the block declares that the material does NOT also declare at the top level.**
        // The count above says what a block contains; this says what is LOST by ignoring it, which
        // is a different number and the one that decides whether a gap matters. A block restating
        // a top-level key changes nothing for a reader that skips it.
        Dictionary<string, int> onlyInside = new(StringComparer.OrdinalIgnoreCase);

        // **Names, not just counts.** A census says how much; a name is what lets somebody open the
        // file and read it. Every wrong conclusion in this area so far came from reasoning about
        // the shape of the data instead of looking at one (`docs/memory/print-what-was-added-not-how-many.md`).
        List<string> carrying = [];

        int materials = 0;
        int withBlocks = 0;
        int unreadable = 0;

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                unreadable++;
                continue;
            }

            materials++;

            bool any = false;

            string text = Encoding.UTF8.GetString(bytes);

            // The shader's own keys, so a block's can be compared against them.
            HashSet<string> top = new(TopLevel(text), StringComparer.OrdinalIgnoreCase);

            foreach ((string block, string key) in Nested(text))
            {
                any = true;

                blocks[block] = blocks.GetValueOrDefault(block) + 1;

                if (wanted is null || !block.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hidden[key] = hidden.GetValueOrDefault(key) + 1;

                if (!top.Contains(key))
                {
                    onlyInside[key] = onlyInside.GetValueOrDefault(key) + 1;

                    if (carrying.Count < 10 && !carrying.Contains(path))
                    {
                        carrying.Add(path);
                    }
                }
            }

            if (any)
            {
                withBlocks++;
            }
        }

        output.WriteLine(
            $"{materials} materials read ({unreadable} unreadable), {withBlocks} opening a " +
            "sub-block inside the shader");

        output.WriteLine();
        output.WriteLine("block name                      keys inside");

        foreach ((string block, int count) in blocks.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"  {block,-28} {count}");
        }

        if (wanted is null)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"parameters inside '{wanted}'   (in block / declared ONLY there)");

        foreach ((string key, int count) in hidden.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"  {key,-28} {count,5} {onlyInside.GetValueOrDefault(key),7}");
        }

        if (carrying.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("materials losing a key by ignoring that block:");

        foreach (string path in carrying)
        {
            output.WriteLine($"  {path}");
        }
    }

    /// <summary>Keys the shader's own block declares, one level in from the file's root.</summary>
    /// <remarks>
    /// **The control for the column beside it.** A block that restates a top-level key costs a
    /// reader nothing by being skipped; only a key declared SOLELY inside it is lost. Without this
    /// the parameter counts say what a block contains and not what ignoring it costs, and those two
    /// numbers have already been confused once here (B326's first reading of B328).
    /// </remarks>
    private static IEnumerable<string> TopLevel(string text)
    {
        List<string> open = [];

        string? pending = null;
        int at = 0;

        while (at < text.Length)
        {
            char character = text[at];

            if (char.IsWhiteSpace(character))
            {
                at++;
            }
            else if (character == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                while (at < text.Length && text[at] is not ('\n' or '\r'))
                {
                    at++;
                }
            }
            else if (character == '{')
            {
                open.Add(pending ?? string.Empty);
                pending = null;
                at++;
            }
            else if (character == '}')
            {
                if (open.Count > 0)
                {
                    open.RemoveAt(open.Count - 1);
                }

                pending = null;
                at++;
            }
            else
            {
                string token = Token(text, ref at);

                if (token.Length == 0)
                {
                    break;
                }

                if (pending is null)
                {
                    pending = token;
                }
                else
                {
                    if (open.Count == 1)
                    {
                        yield return pending;
                    }

                    pending = null;
                }
            }
        }
    }

    /// <summary>Each key declared one level inside the shader's own block, with its block's name.</summary>
    /// <remarks>
    /// **Deliberately its own scanner rather than `VmtMaterial.Parse`.** The parser under
    /// examination is the one that DROPS these, so asking it what it dropped would return nothing
    /// every time — an instrument that agrees with the defect it is measuring
    /// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`). This is a brace counter and
    /// nothing more; it does not resolve patches, apply conditions or judge anything.
    /// </remarks>
    private static IEnumerable<(string Block, string Key)> Nested(string text)
    {
        List<string> open = [];

        string? pending = null;
        int at = 0;

        while (at < text.Length)
        {
            char character = text[at];

            if (char.IsWhiteSpace(character))
            {
                at++;
            }
            else if (character == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                while (at < text.Length && text[at] is not ('\n' or '\r'))
                {
                    at++;
                }
            }
            else if (character == '{')
            {
                open.Add(pending ?? string.Empty);
                pending = null;
                at++;
            }
            else if (character == '}')
            {
                if (open.Count > 0)
                {
                    open.RemoveAt(open.Count - 1);
                }

                pending = null;
                at++;
            }
            else
            {
                string token = Token(text, ref at);

                if (token.Length == 0)
                {
                    break;
                }

                if (pending is null)
                {
                    pending = token;
                }
                else
                {
                    // Depth two is one level inside the shader's block — exactly what
                    // `DescribesTheSurface` refuses unless the block is a patch's.
                    if (open.Count == 2)
                    {
                        yield return (open[1], pending);
                    }

                    pending = null;
                }
            }
        }
    }

    /// <summary>One quoted or bare token.</summary>
    private static string Token(string text, ref int at)
    {
        if (text[at] == '"')
        {
            int start = ++at;

            while (at < text.Length && text[at] != '"')
            {
                at++;
            }

            string quoted = text[start..Math.Min(at, text.Length)];

            if (at < text.Length)
            {
                at++;
            }

            return quoted;
        }

        int from = at;

        while (at < text.Length &&
            !char.IsWhiteSpace(text[at]) &&
            text[at] is not ('{' or '}' or '"'))
        {
            at++;
        }

        return text[from..at];
    }
}
