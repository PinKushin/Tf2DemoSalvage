using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// The engine's own surface, enumerated from <c>source-sdk-2013</c> rather than from memory.
/// </summary>
/// <remarks>
/// **A hand-written checklist cannot cover Source and goes stale the moment it is written.** The
/// published SDK declares what exists — 489 distinct <c>SHADER_PARAM</c> names, 66 BSP lumps, 54
/// <c>mstudio*_t</c> structures, 41 temp entity classes — so the list is extracted from the headers
/// and diffed against what this project implements. The SDK is the source of truth; our sets are the
/// claim being checked.
///
/// **Reading published source is not decompilation** and none of it is copied into this repository:
/// what comes back is a set of NAMES, used to count and to report. The decompiler rule in CLAUDE.md
/// is about output landing in the tree, and nothing here writes any.
///
/// **Regex rather than a C++ parser, deliberately.** These are declaration lists in a fixed shape,
/// and the failure mode of a loose pattern is a name that should not be counted — which shows up as
/// a suspicious total rather than as a silent omission. A parser would be a project of its own for
/// no more certainty.
///
/// **Moved here from <c>Viewer3D.Tests</c> on 2026-08-24 (B184).** It reads text out of the SDK and
/// touches no platform surface at all, and it was sitting in the one test project pinned to
/// <c>net10.0-windows</c> — so nothing on a plain <c>net10.0</c> assembly could use it. The pose
/// pipeline's denominator needs it from <c>Tf2DemoSalvage.Animation.Tests</c>, which is the case
/// that made the pin bite rather than merely being untidy.
/// </remarks>
public static class SdkInventory
{
    /// <summary>Where the SDK is checked out, or null when it is not available.</summary>
    /// <remarks>
    /// Environment-driven so the suite runs anywhere: a machine without the SDK skips these rather
    /// than failing, exactly as the corpus tests skip a demo they do not have.
    /// </remarks>
    public static string? Root =>
        new[]
        {
            Environment.GetEnvironmentVariable("SOURCE_SDK"),
            @"F:\src\source-sdk-2013",
        }
        .FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            Directory.Exists(Path.Combine(candidate, "src", "public")));

    /// <summary>How long one match may run before it is abandoned.</summary>
    /// <remarks>
    /// **Every pattern here is built through <see cref="Pattern"/> so this cannot be forgotten at a
    /// call site.** S6444 wants it, and the rule has a point that applies squarely: these run over
    /// whatever text the SDK checkout happens to contain, and one of them uses a variable-length
    /// lookbehind, which is the construct that backtracks catastrophically. A hang in an extraction
    /// reads as a stuck test run with no output rather than as a failure.
    ///
    /// **This became a build error only when the file moved out of a test project (B184).** Sonar
    /// relaxes several rules inside test assemblies, so the same code compiled clean in
    /// <c>Viewer3D.Tests</c> for weeks. Worth knowing before moving anything else: an extraction
    /// leaving a test project gets held to a stricter standard, and that is the correct direction.
    /// </remarks>
    private static readonly TimeSpan MatchLimit = TimeSpan.FromSeconds(5);

    /// <summary>Builds a compiled pattern with the shared match limit applied.</summary>
    private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.Compiled, MatchLimit);

    /// <summary>Every material parameter Source's own shaders declare.</summary>
    /// <remarks>
    /// <c>SHADER_PARAM( BASETEXTURE, SHADER_PARAM_TYPE_TEXTURE, …)</c> — the first argument is the
    /// name as a material writes it, minus its <c>$</c>. Counted across every shader in
    /// <c>stdshaders</c>, so this is the union of what any material could ask for rather than what
    /// one shader uses.
    /// </remarks>
    public static IReadOnlyCollection<string> ShaderParameters() =>
        Names(
            Path.Combine("src", "materialsystem", "stdshaders"),
            "*.cpp",
            Pattern(@"SHADER_PARAM\(\s*([A-Z0-9_]+)"));

    /// <summary>Every material FLAG the engine defines, which are declared apart from parameters.</summary>
    /// <remarks>
    /// **A second axis, and its absence made the first one lie.** <c>$translucent</c>,
    /// <c>$alphatest</c>, <c>$additive</c>, <c>$selfillum</c>, <c>$nocull</c>, <c>$decal</c> and
    /// <c>$halflambert</c> are not <c>SHADER_PARAM</c> declarations at all — they are
    /// <c>MATERIAL_VAR_*</c> bits in <c>imaterial.h:355</c>, set on the material rather than passed
    /// to one shader. Counting only parameters reported this project as claiming eight things the
    /// engine "does not declare", when it was the inventory that had the wrong model of the engine.
    ///
    /// The flag names carry an underscore where a material writes nothing — MATERIAL_VAR_ALPHATEST
    /// against <c>$alphatest</c> — so they are matched with separators removed.
    /// </remarks>
    public static IReadOnlyCollection<string> MaterialFlags() =>
        Names(
            Path.Combine("src", "public", "materialsystem"),
            "imaterial.h",
            Pattern(@"MATERIAL_VAR_([A-Z0-9_]+)\s*=\s*\("));

    /// <summary>Every STANDARD material var, which belongs to no shader and to all of them.</summary>
    /// <remarks>
    /// **A third axis, and its absence made the other two lie the same way the flags did.**
    /// <c>$color</c>, <c>$alpha</c> and <c>$color2</c> are not <c>SHADER_PARAM</c> declarations and
    /// not <c>MATERIAL_VAR_*</c> bits. They are members of <c>ShaderMaterialVars_t</c> in
    /// <c>public/shaderlib/BaseShader.h:32</c> — registered once by the material system for every
    /// shader, which is exactly why no shader declares them.
    ///
    /// The header says where the names themselves live, and it is somewhere this project cannot
    /// read:
    ///
    /// <code>
    /// // Note: if you add to these, add to s_StandardParams in CBaseShader.cpp
    /// </code>
    ///
    /// <c>CBaseShader.cpp</c> is in the closed shaderlib. So the enum is **read from published
    /// source** and the enum-name-to-parameter-name mapping is **interpolated** — lowercase with a
    /// <c>$</c> — from the four instances the shipped game code confirms by string:
    /// <c>FindVar( "$alpha" )</c> (alphamaterialproxy.cpp:42), <c>FindVar( "$color" )</c>
    /// (thermalmaterialproxy.cpp:50), <c>"$color2"</c> (item_import.cpp:1328), and
    /// <c>$basetexture</c> everywhere. Four of nine is enough to fix the convention and is not a
    /// reading of the table itself; flagged rather than presented as measured.
    /// </remarks>
    public static IReadOnlyCollection<string> StandardMaterialVars() =>
        Names(
            Path.Combine("src", "public", "shaderlib"),
            "BaseShader.h",

            // **Scraped from the enum body rather than listed**, which is the whole point of
            // generating a denominator from the SDK: a hardcoded member list is a second copy that
            // goes stale the moment Valve adds one, and it would go stale silently.
            //
            // The variable-length lookbehind is what makes that possible — it anchors each match
            // inside this one enum without consuming the members, so every member is its own hit.
            // `[^}]*` cannot escape the block, so nothing after the closing brace can match.
            Pattern(
                @"(?<=enum\s+ShaderMaterialVars_t\s*\{[^}]*?)^\s*([A-Z][A-Z0-9_]*)\s*(?:=[^,\r\n]*)?,",
                RegexOptions.Multiline));

    /// <summary>Every lump a BSP can carry.</summary>
    public static IReadOnlyCollection<string> BspLumps() =>
        Names(
            Path.Combine("src", "public"),
            "bspfile.h",
            Pattern(@"^\s*(LUMP_[A-Z0-9_]+)\s*=", RegexOptions.Multiline));

    /// <summary>Every structure a studio model file can carry.</summary>
    public static IReadOnlyCollection<string> StudioStructures() =>
        Names(
            Path.Combine("src", "public"),
            "studio.h",
            Pattern(@"^struct (mstudio[a-z_]+_t)", RegexOptions.Multiline));

    /// <summary>Every network message the engine hands to a client.</summary>
    public static IReadOnlyCollection<string> NetMessages() =>
        Names(
            Path.Combine("src", "public"),
            "inetmsghandler.h",
            Pattern(@"PROCESS_(?:SVC|NET)_MESSAGE\(\s*([A-Za-z0-9_]+)\s*\)\s*=\s*0"));

    /// <summary>Every one-shot effect the engine sends as a temp entity.</summary>
    /// <remarks>
    /// Taken from the class declarations in <c>game/client/c_te_*.cpp</c>, which is one file per
    /// effect family and the closest thing to a list the SDK has.
    /// </remarks>
    public static IReadOnlyCollection<string> TempEntities() =>
        Names(
            Path.Combine("src", "game", "client"),
            "c_te_*.cpp",
            Pattern(@"class (C_TE[A-Za-z0-9_]+)\s*:"));

    /// <summary>Every named integer a header declares, by <c>#define</c> or as an enumerator.</summary>
    /// <param name="header">Path under the SDK, such as <c>src/public/bspfile.h</c>.</param>
    /// <returns>Name to value, for the ones that are plain integers.</returns>
    /// <remarks>
    /// **This is the highest-value axis of the whole inventory.** A format reader is mostly magic
    /// numbers — a lump index, a version bound, a vertex size, a bit width — and every one of them
    /// fails the same way when wrong: it lands on real data and decodes something plausible. The
    /// engine declares them all, so they can be checked rather than trusted.
    ///
    /// Only plain integers and hexadecimal are returned. Expressions like
    /// <c>('V'&lt;&lt;24)+('S'&lt;&lt;16)+…</c> and anything built from other constants are skipped
    /// rather than half-evaluated: a wrong value here would be worse than a missing one, because it
    /// would fail a test that is supposed to be the reference.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Constants(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (Root is not { } root)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        string path = Path.Combine(root, header.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        Dictionary<string, int> values = new(StringComparer.Ordinal);

        Regex defined = Pattern(
            @"^\s*#define\s+([A-Z][A-Z0-9_]*)\s+\(?\s*(0x[0-9A-Fa-f]+|\d+)\s*\)?\s*(?://.*)?$",
            RegexOptions.Multiline);

        Regex enumerated = Pattern(
            @"^\s*([A-Z][A-Z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,",
            RegexOptions.Multiline);

        // Read once. It was read per pattern, so a two-pattern scan opened the header twice for an
        // answer that cannot differ between them.
        string text = File.ReadAllText(path);

        IEnumerable<GroupCollection> declarations = new[] { defined, enumerated }
            .SelectMany(pattern => pattern.Matches(text))
            .Select(hit => hit.Groups);

        foreach (GroupCollection groups in declarations)
        {
            string literal = groups[2].Value;

            int value = literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(literal[2..], 16)
                : int.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

            // First declaration wins: a header that redefines a name under an #ifdef is
            // describing a variant build, and the first is the one these readers target.
            values.TryAdd(groups[1].Value, value);
        }

        return values;
    }

    /// <summary>Matches a pattern across files and returns every distinct capture.</summary>
    private static HashSet<string> Names(string folder, string pattern, Regex match)
    {
        if (Root is not { } root)
        {
            return [];
        }

        string directory = Path.Combine(root, folder);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(directory, pattern))
        {
            foreach (Match hit in match.Matches(File.ReadAllText(file)))
            {
                found.Add(hit.Groups[1].Value);
            }
        }

        return found;
    }

    /// <summary>The body of one function, brace-matched from its signature.</summary>
    /// <param name="file">Path under the SDK, such as <c>src/game/client/c_baseanimating.cpp</c>.</param>
    /// <param name="signature">
    /// Text that starts the definition, such as <c>void C_BaseAnimating::StandardBlendingRules</c>.
    /// Matched literally, so it must be the definition rather than the declaration.
    /// </param>
    /// <returns>Everything between the outermost braces, or empty when it is not found.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **This is what turns "the pipeline has these stages" from a typed list into a read one.**
    /// B182 exists because the pose path's stage list was written by hand, and it was wrong by two
    /// stages within a day: <c>GetPoseParameters</c> and <c>ChildLayerBlend</c> were both missing.
    /// A list extracted from the function body cannot omit a stage without the extraction failing
    /// loudly.
    ///
    /// **Braces inside string literals and line comments are skipped**, because C++ carries both
    /// and a naive count walks off the end of the function. It does not attempt block comments or
    /// preprocessor conditionals — an unbalanced brace inside <c>#if 0</c> would defeat it, and the
    /// callers assert on what comes back rather than trusting it.
    /// </remarks>
    public static string FunctionBody(string file, string signature)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(signature);

        if (Root is not { } root)
        {
            return string.Empty;
        }

        string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        string source = File.ReadAllText(path);
        int at = source.IndexOf(signature, StringComparison.Ordinal);

        if (at < 0)
        {
            return string.Empty;
        }

        int open = source.IndexOf('{', at);

        if (open < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        bool inString = false;
        bool inComment = false;

        // A while loop rather than a for, because the escape case advances by two and S127 rightly
        // objects to a for loop that edits its own counter — the reader cannot then trust the header.
        int index = open - 1;

        while (++index < source.Length)
        {
            char current = source[index];

            if (inComment)
            {
                inComment = current is not ('\n' or '\r');
                continue;
            }

            if (inString)
            {
                // A backslash escapes the next character, so a trailing quote in "\"" does not
                // close the literal.
                if (current == '\\')
                {
                    index++;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inString = true;
                    break;

                case '/' when index + 1 < source.Length && source[index + 1] == '/':
                    inComment = true;
                    break;

                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;

                    if (depth == 0)
                    {
                        return source[(open + 1)..index];
                    }

                    break;

                default:
                    break;
            }
        }

        return string.Empty;
    }

    /// <summary>The same text with comments and string literals blanked out.</summary>
    /// <param name="source">C++ source, typically one function body.</param>
    /// <returns>The text with anything inside a comment or a literal replaced by spaces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <remarks>
    /// **Written because the instrument was wrong on its first run, in the direction that costs
    /// most.** Scanning <c>StandardBlendingRules</c> for calls returned <c>AddTextOverlay</c>,
    /// <c>GetAbsOrigin</c> and <c>Vector</c>, all three from a single COMMENTED-OUT line:
    ///
    /// <code>
    /// // debugoverlay->AddTextOverlay( GetAbsOrigin() + Vector( 0, 0, 64 ), 0, 0, …
    /// </code>
    ///
    /// A denominator that reports deleted code as an engine stage does not merely overcount — it
    /// asks somebody to implement something Valve removed, and the resulting work looks like parity
    /// while being the opposite. Caught only by reading the list the tool printed, which is the
    /// argument for printing it.
    ///
    /// **Replaced by spaces rather than deleted**, so offsets are preserved and a caller can still
    /// map a match back onto the original text.
    ///
    /// Blanks literals too: a string containing <c>foo(</c> would otherwise read as a call.
    /// </remarks>
    public static string Live(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        char[] text = source.ToCharArray();
        int index = -1;

        // **Comments are tested BEFORE quotes and the order is load-bearing.** An apostrophe is a
        // character literal in code and an ordinary letter in prose, so `// don't` would otherwise
        // open a literal that runs to the next apostrophe several lines later, blanking real calls
        // in between. Inside a comment nothing is a literal.
        while (++index < text.Length)
        {
            bool pair = index + 1 < text.Length;

            if (text[index] == '/' && pair && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] is not ('\n' or '\r'))
                {
                    text[index++] = ' ';
                }

                index--;
            }
            else if (text[index] == '/' && pair && text[index + 1] == '*')
            {
                // Find the terminator first and blank the whole span in one pass, rather than
                // walking and then patching up the closing pair — that shape assigns the same
                // index twice when the comment is empty, which S4143 correctly objects to.
                int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                int last = close < 0 ? text.Length - 1 : close + 1;

                while (index <= last)
                {
                    text[index++] = ' ';
                }

                index--;
            }
            else if (text[index] is '"' or '\'')
            {
                index = Blank(text, index, text[index]);
            }
        }

        return new string(text);
    }

    /// <summary>Blanks a quoted literal in place and returns the index of its closing quote.</summary>
    private static int Blank(char[] text, int start, char quote)
    {
        int index = start;

        text[index++] = ' ';

        while (index < text.Length && text[index] != quote)
        {
            // A backslash escapes the next character, so "\"" does not end here.
            bool escaped = text[index] == '\\';

            text[index++] = ' ';

            if (escaped && index < text.Length)
            {
                text[index++] = ' ';
            }
        }

        if (index < text.Length)
        {
            text[index] = ' ';
        }

        return index;
    }

    /// <summary>Every identifier called as a function in a block, in the order they appear.</summary>
    /// <param name="body">A function body, from <see cref="FunctionBody"/>.</param>
    /// <returns>Call names, first appearance order, no duplicates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Order matters and is preserved**, because a pipeline is a sequence: knowing that
    /// <c>CalcBoneAdj</c> runs after <c>AccumulateLayers</c> and before <c>UnragdollBlend</c> is
    /// half of what the denominator is for. A set would answer "is it there" and lose "when".
    ///
    /// **A method call and a plain function call look the same here on purpose.**
    /// <c>boneSetup.InitPose(...)</c> yields <c>InitPose</c>; the receiver is not part of the stage
    /// name and including it would make the list churn if Valve renamed a local.
    ///
    /// Control-flow keywords are dropped, since <c>if (</c> is not a call. Everything else is
    /// returned and the caller must classify it — an unrecognised name is the signal that the
    /// engine grew a stage.
    /// </remarks>
    public static IReadOnlyList<string> CallsIn(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        HashSet<string> keywords = new(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "return", "sizeof", "catch", "throw",
        };

        List<string> calls = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (Match hit in Pattern(@"([A-Za-z_][A-Za-z0-9_]*)\s*\(").Matches(Live(body)))
        {
            string name = hit.Groups[1].Value;

            if (!keywords.Contains(name) && seen.Add(name))
            {
                calls.Add(name);
            }
        }

        return calls;
    }

    /// <summary>Writes a report of what is covered and what is not.</summary>
    /// <param name="axis">What is being counted, such as "material parameters".</param>
    /// <param name="engine">Every name the engine declares.</param>
    /// <param name="ours">The names this project handles.</param>
    /// <returns>A block of Markdown.</returns>
    /// <remarks>
    /// **Names the uncovered ones rather than only counting them.** A percentage is a score and a
    /// list is a worklist, and this project has been bitten repeatedly by reports that summarised
    /// instead of naming — the material census stayed silent for a session because it counted
    /// failures rather than stating what was asked for.
    /// </remarks>
    public static string Report(
        string axis, IReadOnlyCollection<string> engine, IReadOnlyCollection<string> ours)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(ours);

        // **Both sides normalised, because the two spell the same thing differently.** A shader
        // declares SHADER_PARAM( BASETEXTURE ) and a material writes "$basetexture"; comparing them
        // raw reported 0 of 489 handled, which is the most alarming possible way to be wrong about
        // your own coverage — and it was the report that was wrong, not the coverage.
        // Separators go too, so MATERIAL_VAR_ALPHATEST and "$alphatest" are one name.
        static string Key(string name) => name.TrimStart('$', '%').Replace("_", string.Empty, StringComparison.Ordinal);

        HashSet<string> handled = new(ours.Select(Key), StringComparer.OrdinalIgnoreCase);

        string[] missing =
        [
            .. engine
                .Where(name => !handled.Contains(Key(name)))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
        ];

        int covered = engine.Count - missing.Length;

        return $"## {axis}\n\n" +
            $"**{covered} of {engine.Count}** declared by the engine are handled here.\n\n" +
            (missing.Length == 0
                ? "Nothing outstanding.\n"
                : "Not handled:\n\n```\n" + string.Join(", ", missing) + "\n```\n");
    }
}
