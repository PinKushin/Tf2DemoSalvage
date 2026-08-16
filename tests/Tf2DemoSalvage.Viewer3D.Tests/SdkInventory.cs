using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
/// </remarks>
internal static class SdkInventory
{
    /// <summary>Where the SDK is checked out, or null when it is not available.</summary>
    /// <remarks>
    /// Environment-driven so the suite runs anywhere: a machine without the SDK skips these rather
    /// than failing, exactly as the corpus tests skip a demo they do not have.
    /// </remarks>
    public static string? Root
    {
        get
        {
            foreach (string? candidate in new[]
            {
                Environment.GetEnvironmentVariable("SOURCE_SDK"),
                @"F:\src\source-sdk-2013",
            })
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    Directory.Exists(Path.Combine(candidate, "src", "public")))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

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
            new Regex(@"SHADER_PARAM\(\s*([A-Z0-9_]+)", RegexOptions.Compiled));

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
            new Regex(@"MATERIAL_VAR_([A-Z0-9_]+)\s*=\s*\(", RegexOptions.Compiled));

    /// <summary>Every lump a BSP can carry.</summary>
    public static IReadOnlyCollection<string> BspLumps() =>
        Names(
            Path.Combine("src", "public"),
            "bspfile.h",
            new Regex(@"^\s*(LUMP_[A-Z0-9_]+)\s*=", RegexOptions.Compiled | RegexOptions.Multiline));

    /// <summary>Every structure a studio model file can carry.</summary>
    public static IReadOnlyCollection<string> StudioStructures() =>
        Names(
            Path.Combine("src", "public"),
            "studio.h",
            new Regex(@"^struct (mstudio[a-z_]+_t)", RegexOptions.Compiled | RegexOptions.Multiline));

    /// <summary>Every network message the engine hands to a client.</summary>
    public static IReadOnlyCollection<string> NetMessages() =>
        Names(
            Path.Combine("src", "public"),
            "inetmsghandler.h",
            new Regex(
                @"PROCESS_(?:SVC|NET)_MESSAGE\(\s*([A-Za-z0-9_]+)\s*\)\s*=\s*0",
                RegexOptions.Compiled));

    /// <summary>Every one-shot effect the engine sends as a temp entity.</summary>
    /// <remarks>
    /// Taken from the class declarations in <c>game/client/c_te_*.cpp</c>, which is one file per
    /// effect family and the closest thing to a list the SDK has.
    /// </remarks>
    public static IReadOnlyCollection<string> TempEntities() =>
        Names(
            Path.Combine("src", "game", "client"),
            "c_te_*.cpp",
            new Regex(@"class (C_TE[A-Za-z0-9_]+)\s*:", RegexOptions.Compiled));

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

        Regex defined = new(
            @"^\s*#define\s+([A-Z][A-Z0-9_]*)\s+\(?\s*(0x[0-9A-Fa-f]+|\d+)\s*\)?\s*(?://.*)?$",
            RegexOptions.Multiline);

        Regex enumerated = new(
            @"^\s*([A-Z][A-Z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,",
            RegexOptions.Multiline);

        foreach (Regex pattern in new[] { defined, enumerated })
        {
            foreach (Match hit in pattern.Matches(File.ReadAllText(path)))
            {
                string text = hit.Groups[2].Value;

                int value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(text[2..], 16)
                    : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

                // First declaration wins: a header that redefines a name under an #ifdef is
                // describing a variant build, and the first is the one these readers target.
                values.TryAdd(hit.Groups[1].Value, value);
            }
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
