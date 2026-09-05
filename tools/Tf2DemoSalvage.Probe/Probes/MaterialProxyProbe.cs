using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which material proxies TF2's shipped materials actually run, and how often (B80).
/// </summary>
/// <remarks>
/// **`vmt-blocks` cannot answer this and its own control could never say so.** That probe yields a
/// pair only at <c>open.Count == 2</c> — one level inside the shader — and a proxy name is a block
/// at depth THREE, inside <c>Proxies</c>. Its header names `Proxies` as its control, *"expected and
/// reported with the rest rather than filtered out"*, and `Proxies` has no depth-two keys at all:
/// it contains sub-blocks and nothing else. So the control reports absent for a correct scan and
/// for a broken one alike — a control that cannot fire, which is the shape
/// <c>docs/memory/an-empty-search-needs-a-control.md</c> is about, one level up.
///
/// **This walks to any depth and tallies the blocks inside `Proxies`**, which is the question the
/// entity-state proxy work needs a denominator for: a proxy this project leaves unevaluated rests
/// its variable at whatever the VMT declared, so the cost of not implementing one is exactly how
/// many materials run it.
///
/// **Its own control is `resultVar`**, which nearly every proxy declares and which must therefore
/// come back in the thousands. A run reporting few of those has a broken walk rather than a finding.
///
/// <code>
///   vmt-proxy                — every proxy name, by how many materials run it
///   vmt-proxy PlayerProximity — and the variables that one reads and writes
/// </code>
/// </remarks>
public sealed class MaterialProxyProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vmt-proxy";

    /// <inheritdoc/>
    public string Summary =>
        "which material proxies shipped materials run: vmt-proxy [proxy name]";

    /// <summary>How many materials to name for a chosen proxy.</summary>
    private const int Named = 8;

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

        Dictionary<string, int> proxies = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> variables = new(StringComparer.OrdinalIgnoreCase);
        List<string> carrying = [];

        int materials = 0;
        int withProxies = 0;
        int control = 0;

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                continue;
            }

            materials++;

            string text = Encoding.UTF8.GetString(bytes);

            // **Counted once per MATERIAL, not once per declaration.** A material may run the same
            // proxy twice — `Equals` appears three times in some weapon materials — and the number
            // that decides whether implementing one matters is how many materials it changes.
            HashSet<string> here = new(StringComparer.OrdinalIgnoreCase);

            foreach ((string proxy, string key, string value) in Inside(text))
            {
                here.Add(proxy);

                if (key.Equals("resultVar", StringComparison.OrdinalIgnoreCase))
                {
                    control++;
                }

                if (wanted is not null &&
                    proxy.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    variables[$"{key} = {value}"] = variables.GetValueOrDefault($"{key} = {value}") + 1;

                    if (carrying.Count < Named && !carrying.Contains(path))
                    {
                        carrying.Add(path);
                    }
                }
            }

            if (here.Count > 0)
            {
                withProxies++;
            }

            foreach (string proxy in here)
            {
                proxies[proxy] = proxies.GetValueOrDefault(proxy) + 1;
            }
        }

        output.WriteLine($"{materials} materials read, {withProxies} running at least one proxy");
        output.WriteLine($"{control} resultVar declarations — the control");
        output.WriteLine();

        foreach ((string proxy, int count) in proxies.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"  {count,6}  {proxy}");
        }

        if (wanted is null)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"what '{wanted}' declares:");

        foreach ((string setting, int count) in variables
            .OrderByDescending(entry => entry.Value)
            .Take(20))
        {
            output.WriteLine($"  {count,6}  {setting}");
        }

        output.WriteLine();
        output.WriteLine("running it:");

        foreach (string path in carrying)
        {
            output.WriteLine($"  {path}");
        }
    }

    /// <summary>Every key and value inside a proxy block, with the proxy's name.</summary>
    /// <param name="text">The VMT's text.</param>
    /// <returns>Triples of proxy name, key and value.</returns>
    /// <remarks>
    /// **Depth is tracked rather than assumed**, which is the whole difference from `vmt-blocks`:
    /// a proxy block sits inside <c>Proxies</c> inside the shader, and the enclosing names are what
    /// say whether a block is a proxy at all. A `Sine` block directly inside the shader is not one —
    /// eight shipped materials write exactly that, and counting them as proxies would be wrong in
    /// the direction that makes a feature look more used than it is.
    /// </remarks>
    private static IEnumerable<(string Proxy, string Key, string Value)> Inside(string text)
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
                    continue;
                }

                // A proxy is a block at depth three whose parent is `Proxies`. Anything shallower
                // is a shader parameter and anything deeper is a proxy's own nested block.
                if (open.Count == 3 &&
                    open[1].Equals("Proxies", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (open[2], pending, token);
                }

                pending = null;
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
            text[at] is not ('{' or '}'))
        {
            at++;
        }

        return text[from..at];
    }
}
