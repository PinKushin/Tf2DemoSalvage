using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every engine function this project cites, ranked by how many branches Valve gave it.
/// </summary>
/// <remarks>
/// **The owner's standing instruction, made into a denominator.** Every expensive bug of 2026-08-30
/// was one branch of a multi-branch engine function, implemented on one side only:
///
/// <list type="bullet">
/// <item><c>C_TFPlayer::GetSkin</c> chooses a disguise mask and <c>ValidateModelIndex</c> turns the
/// mask MESH on — same condition, two functions, and only the first was done (B236).</item>
/// <item><c>CalcAbsolutePosition</c> has three branches and this project had two, so a parented
/// prop lost its own angles and a gate drew a quarter turn out (B241).</item>
/// <item><c>ShouldDraw</c> tests the render mode before anything else, and that test was absent, so
/// eighteen invisible doors drew over the gates (B240).</item>
/// </list>
///
/// **A count of branches is a SCREEN, not a verdict.** It cannot say whether a branch is
/// implemented; it says where the risk is concentrated, so reading starts at the functions with the
/// most places to go wrong rather than wherever the last bug happened to be. The reading is still
/// the work — this only puts it in order.
///
/// <code>
///   parity                 # every cited function, most branches first
///   parity c_tf_player     # only those in files whose name matches
/// </code>
/// </remarks>
public sealed class ParityAuditProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "parity";

    /// <inheritdoc/>
    public string Summary => "engine functions we cite, ranked by Valve's branch count: parity [filter]";

    /// <summary>Where the published engine source is (D-note: it is on F:).</summary>
    private const string SdkRoot = @"F:\src\source-sdk-2013\src";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;

        if (!Directory.Exists(SdkRoot))
        {
            output.WriteLine($"The SDK is not at {SdkRoot}, so nothing can be audited.");
            return;
        }

        string? managed = Managed();

        if (managed is null)
        {
            output.WriteLine("Could not find the managed source tree above this binary.");
            return;
        }

        // Citation as this project writes one: `c_baseentity.cpp:4387`.
        Regex citation = new(
            @"\b([a-z_0-9]+\.(?:cpp|h)):(\d+)\b", RegexOptions.None, TimeSpan.FromSeconds(5));

        Dictionary<(string File, int Line), int> cited = [];

        foreach (string source in Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories))
        {
            if (source.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (GroupCollection groups in citation
                .Matches(File.ReadAllText(source))
                .Select(match => match.Groups))
            {
                (string, int) key = (
                    groups[1].Value,
                    int.Parse(groups[2].Value, CultureInfo.InvariantCulture));

                cited[key] = cited.TryGetValue(key, out int seen) ? seen + 1 : 1;
            }
        }

        output.WriteLine(
            $"{cited.Count.ToString(CultureInfo.InvariantCulture)} distinct citations in the "
            + "managed tree");

        Dictionary<string, string[]> files = [];
        List<(string Where, string Function, int Branches, int Cites)> found = [];
        List<string> missing = [];

        foreach (((string file, int line), int cites) in cited)
        {
            if (filter.Length > 0 && !file.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!files.TryGetValue(file, out string[]? text))
            {
                string? path = Directory
                    .EnumerateFiles(SdkRoot, file, SearchOption.AllDirectories)
                    .FirstOrDefault();

                text = path is null ? [] : File.ReadAllLines(path);
                files[file] = text;
            }

            if (text.Length == 0)
            {
                missing.Add(file);
                continue;
            }

            if (line < 1 || line > text.Length)
            {
                missing.Add($"{file}:{line} (past its {text.Length.ToString(CultureInfo.InvariantCulture)} lines)");
                continue;
            }

            (string name, int branches) = Enclosing(text, line - 1);

            found.Add(($"{file}:{line.ToString(CultureInfo.InvariantCulture)}", name, branches, cites));
        }

        foreach ((string where, string function, int branches, int cites) in found
            .OrderByDescending(entry => entry.Branches)
            .ThenByDescending(entry => entry.Cites))
        {
            output.WriteLine(
                $"{branches,3} branches  {cites,2} cites  {where,-34}  {function}");
        }

        if (missing.Count > 0)
        {
            output.WriteLine(
                $"NOT FOUND in the SDK ({missing.Count.ToString(CultureInfo.InvariantCulture)}): "
                + string.Join(", ", missing.Distinct().Order(StringComparer.Ordinal).Take(12)));
        }
    }

    /// <summary>The function a line sits in, and how many branch points it has.</summary>
    /// <remarks>
    /// **Crude on purpose.** It walks up to the nearest line that starts at column zero and looks
    /// like a definition, then counts `if`, `else if`, `case` and `?:` until the braces balance.
    /// A C++ parser would be more accurate and would not change what this is for: ranking. Any
    /// function it mis-attributes is still a function worth reading.
    /// </remarks>
    private static (string Name, int Branches) Enclosing(string[] text, int at)
    {
        int start = at;

        while (start > 0 &&
               !(text[start].Length > 0 &&
                 text[start][0] is not (' ' or '\t' or '/' or '#' or '}' or '{') &&
                 text[start].Contains('(', StringComparison.Ordinal)))
        {
            start--;
        }

        string name = text[start].Trim();

        int depth = 0;
        int branches = 0;
        bool opened = false;

        for (int line = start; line < text.Length; line++)
        {
            string body = text[line];

            branches += Count(body, "if (") + Count(body, "if(")
                + Count(body, "case ") + Count(body, "? ");

            foreach (char letter in body)
            {
                if (letter == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (letter == '}')
                {
                    depth--;
                }
            }

            if (opened && depth <= 0)
            {
                break;
            }
        }

        return (name.Length > 90 ? name[..90] : name, branches);
    }

    private static int Count(string line, string token)
    {
        int seen = 0;
        int at = line.IndexOf(token, StringComparison.Ordinal);

        while (at >= 0)
        {
            seen++;
            at = line.IndexOf(token, at + token.Length, StringComparison.Ordinal);
        }

        return seen;
    }

    /// <summary>Walks up from the binary to the repository's managed tree.</summary>
    private static string? Managed()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "managed");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
