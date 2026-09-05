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

        Uncited(output, arguments, managed, cited);
    }

    /// <summary>Methods of a class this project has NEVER cited.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="arguments">The probe's arguments; the second names a class.</param>
    /// <param name="managed">The managed source tree, searched for each method's NAME.</param>
    /// <param name="cited">Every engine line this project points at, by file and line.</param>
    /// <remarks>
    /// **The complement of the ranking above, and it is where the defects were** (B350). The list
    /// this probe prints is what somebody has already compared against the engine; a function with
    /// NO citation is one nobody has looked at, which is a sharper filter than branch count and far
    /// cheaper than reading a subsystem.
    ///
    /// **Two sweeps by hand produced one real defect and eleven dead ends.**
    /// `CMultiPlayerAnimState` had five uncited methods: four dead or unreachable, and
    /// `PlayFlinchGesture`, which turned out to drop more than half of every flinch in a demo.
    /// `CTFPlayerAnimState` had eight and no defects. Both are recorded in `docs/PARITY-AUDIT.md`.
    ///
    /// **A citation is matched by LINE, so this asks whether any cited line falls inside the
    /// method** — a class member cited anywhere counts as looked at, which is the question worth
    /// asking. It is deliberately generous: a false "cited" costs a subject nobody re-reads, while
    /// a false "uncited" costs an hour finding out it was fine.
    /// </remarks>
    private static void Uncited(
        TextWriter output,
        IReadOnlyList<string> arguments,
        string managed,
        Dictionary<(string File, int Line), int> cited)
    {
        if (arguments.Count < 2)
        {
            output.WriteLine(
                "  (pass a class name second — `parity animstate CMultiPlayerAnimState` — to list "
                + "its methods this project has never cited)");
            return;
        }

        string wanted = arguments[1];

        Regex member = new(
            @"^[A-Za-z_][A-Za-z0-9_:<>\* \t]*?\b" + Regex.Escape(wanted) + @"::([A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(10));

        SortedDictionary<string, List<(string File, int Line)>> methods =
            new(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(SdkRoot, "*.cpp", SearchOption.AllDirectories))
        {
            string text;

            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }

            if (!text.Contains(wanted + "::", StringComparison.Ordinal))
            {
                continue;
            }

            string name = Path.GetFileName(path);
            string[] lines = text.Split('\n');

            foreach (Match match in member.Matches(text))
            {
                int line = text.Take(match.Index).Count(character => character == '\n') + 1;

                if (!methods.TryGetValue(match.Groups[1].Value, out List<(string, int)>? at))
                {
                    at = [];
                    methods[match.Groups[1].Value] = at;
                }

                at.Add((name, line));
            }

            _ = lines;
        }

        if (methods.Count == 0)
        {
            output.WriteLine($"  no methods found for '{wanted}' — check the class name.");
            return;
        }

        // **Asked by NAME, not by line, and the difference matters** (B350). Matching a citation's
        // line against the method's range answers "did anybody cite THIS definition" — which
        // reports `HandleJumping` as unstudied because the citation points at TF2's override in
        // `tf_playeranimstate.cpp` instead. That is a fact about which file was quoted, not about
        // whether the mechanism was compared. The question worth asking is whether the NAME appears
        // anywhere in the managed tree, which is what the two hand sweeps asked and what found the
        // flinch: 22 methods by the strict reading against 5 by this one.
        HashSet<string> named = new(StringComparer.Ordinal);

        // **The TEST tree counts too, and leaving it out was the other half of the discrepancy.**
        // A conformance suite is where a mechanism's citation most often lives — the whole point of
        // `docs/CONFORMANCE.md` — so a function named only there has still been compared against
        // the engine.
        string root = Directory.GetParent(managed)?.FullName ?? managed;
        string tests = Path.Combine(root, "tests");

        // **And `docs/`, because an audit's CONCLUSION is where a dead end gets recorded.** Four of
        // the five functions the first hand sweep ran down turned out dead or unreachable; their
        // answer lives in `docs/PARITY-AUDIT.md` and nowhere in the code, since there was no code to
        // write. Leaving docs out would offer them again every time this is run, which is exactly
        // the re-reading the probe exists to prevent.
        string docs = Path.Combine(root, "docs");

        IEnumerable<string> sources = Directory
            .EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.Exists(tests)
                ? Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
                : [])
            .Concat(Directory.Exists(docs)
                ? Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
                : []);

        foreach (string source in sources)
        {
            if (source.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(source);

            foreach (string name in methods.Keys.Where(
                candidate => text.Contains(candidate, StringComparison.Ordinal)))
            {
                named.Add(name);
            }
        }

        // **The UNION of two questions, because neither alone means "looked at"** (B350). A comment
        // may cite `multiplayer_animstate.cpp:1443` without writing `SetupPoseParameters`, and it
        // may name a function without pinning a line. Counting only one reports studied mechanisms
        // as unstudied — the strict line reading gave 28 here against 5 by hand, and every extra
        // was a function this project had genuinely compared.
        List<string> never = methods.Keys
            .Where(name => !named.Contains(name))
            .Where(name => !methods[name].Any(where => cited.Keys.Any(
                key => string.Equals(key.File, where.File, StringComparison.OrdinalIgnoreCase)
                    && key.Line >= where.Line
                    && key.Line < where.Line + MethodWindow)))
            .ToList();

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{wanted}: {methods.Count} methods, {never.Count} NEVER cited by this project"));

        if (never.Count > 0)
        {
            output.WriteLine("  " + string.Join(", ", never));
        }
    }

    /// <summary>How far past a definition a citation still counts as pointing at it.</summary>
    /// <remarks>
    /// **Generous on purpose.** A citation lands on the line that matters, not on the signature, so
    /// a window is needed: too small reports a studied function as unstudied and costs an hour of
    /// re-reading, too large hides one and costs a subject nobody looks at again. Two hundred lines
    /// covers every method in both animstates, the longest being `DoAnimationEvent` at about 120.
    /// </remarks>
    private const int MethodWindow = 200;

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
