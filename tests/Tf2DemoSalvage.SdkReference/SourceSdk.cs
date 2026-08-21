using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// Where <c>source-sdk-2013</c> is, and how a conformance test reads it.
/// </summary>
/// <remarks>
/// **One place that knows where the SDK lives, because three had it.** The lump test, the send-prop
/// test and the coverage inventory each carried their own copy of the same environment lookup and
/// the same hardcoded fallback path. Three copies of a path is three chances for a machine to run
/// two of the suites and silently skip the third — and a skipped conformance test looks exactly like
/// a passing one in a summary line.
///
/// **Every accessor returns empty rather than throwing when the SDK is absent**, so a machine
/// without a checkout skips these tests instead of failing them, exactly as the corpus tests skip a
/// demo they do not have. The caller decides: <see cref="Available"/> gates an
/// <c>Assert.Ignore</c>, and every extractor carries a floor assertion so an extraction that found
/// nothing cannot pass by vacuum.
///
/// **Reading published source is not decompilation** and nothing here copies any of it into the
/// repository: what comes back is names, numbers and field offsets, used to check this project's own
/// constants against the engine's.
/// </remarks>
public static class SourceSdk
{
    /// <summary>Where the SDK is checked out, or null when it is not available.</summary>
    /// <remarks>
    /// <c>SOURCE_SDK</c> overrides, so the suite runs on a machine that keeps it elsewhere. The
    /// fallback is the owner's checkout; a machine with neither gets null and skips.
    /// </remarks>
    public static string? Root =>
        new[] { Environment.GetEnvironmentVariable("SOURCE_SDK"), @"F:\src\source-sdk-2013" }
            .FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate) &&
                Directory.Exists(Path.Combine(candidate, "src", "public")));

    /// <summary>How long a pattern may run before it is treated as a defect in the pattern.</summary>
    /// <remarks>
    /// These read headers of a few hundred kilobytes with patterns that do not backtrack, so the
    /// bound is unreachable in practice and exists to make a future pattern that does backtrack fail
    /// rather than hang a test run.
    /// </remarks>
    private static readonly TimeSpan PatternLimit = TimeSpan.FromSeconds(10);

    /// <summary>Whether a conformance test can run at all.</summary>
    public static bool Available => Root is not null;

    /// <summary>The reason to give when skipping.</summary>
    public const string Missing =
        "source-sdk-2013 is not available; set SOURCE_SDK to a checkout to run this.";

    /// <summary>What has already been read, so a suite reads the SDK once rather than per test.</summary>
    /// <remarks>
    /// **The reads are repetitive in a way that adds up.** <c>BspStructTests</c> asks for
    /// <c>bspfile.h</c> once per structure — a dozen times across the class — and the send-prop test
    /// walks every <c>.cpp</c> under <c>src/game</c> recursively, reading each in full, once per test
    /// method. The SDK is a fixed checkout for the length of a run, so the second read of a file can
    /// never differ from the first.
    ///
    /// Concurrent because NUnit may run fixtures in parallel, and keyed by the path as given so two
    /// spellings of the same file cost two reads rather than returning the wrong one.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, string?> Read = new(StringComparer.Ordinal);

    /// <summary>Extractions already performed, keyed by file and what was asked of it.</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, int>> Parsed =
        new(StringComparer.Ordinal);

    /// <summary>The full text of one file under the SDK, or null when it is not there.</summary>
    /// <param name="relativePath">Path under the checkout, such as <c>src/public/bspfile.h</c>.</param>
    /// <returns>The file's contents, or null.</returns>
    public static string? Text(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return Read.GetOrAdd(relativePath, static path =>
        {
            if (Root is not { } root)
            {
                return null;
            }

            string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

            return File.Exists(full) ? File.ReadAllText(full) : null;
        });
    }

    /// <summary>Every file matching a pattern under one folder of the SDK.</summary>
    /// <param name="relativeFolder">Folder under the checkout, such as <c>src/game</c>.</param>
    /// <param name="pattern">A file glob, such as <c>*.cpp</c>.</param>
    /// <param name="recursive">Whether to descend into subfolders.</param>
    /// <returns>Absolute paths, empty when the SDK or the folder is absent.</returns>
    public static IEnumerable<string> Files(
        string relativeFolder, string pattern, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(relativeFolder);

        if (Root is not { } root)
        {
            return [];
        }

        string directory = Path.Combine(
            root, relativeFolder.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(directory))
        {
            return [];
        }

        SearchOption depth = recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        return Directory.EnumerateFiles(directory, pattern, depth);
    }

    /// <summary>Every distinct capture of a pattern across a set of files.</summary>
    /// <param name="relativeFolder">Folder under the checkout.</param>
    /// <param name="pattern">A file glob.</param>
    /// <param name="match">A regex whose first group is the name to collect.</param>
    /// <param name="recursive">Whether to descend into subfolders.</param>
    /// <returns>The names found, compared case-insensitively.</returns>
    public static HashSet<string> Names(
        string relativeFolder, string pattern, Regex match, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(match);

        // **Measured, and neither the crawl nor the matching turned out to dominate.** The sweep
        // over src/game reads thousands of files and runs a pattern across roughly twenty megabytes,
        // three times, which looks expensive and is not: caching the file list, then the contents,
        // then the result changed a three-test run from 553 ms to 532–648 ms. That is noise. The
        // cost is the test host starting up, and none of this touches it.
        //
        // Kept anyway, because it bounds the cost as more suites read the SDK and the checkout
        // cannot change mid-run — but recorded as unmeasured benefit rather than a win, since the
        // numbers do not support calling it one.
        //
        // Keyed by the pattern's own text as well as the folder, because two callers sweeping the
        // same directory for different things must not share an answer. That would be a wrong
        // result rather than a slow one, which is the only outcome here worth avoiding.
        // **A COPY, because a caller that mutates what it is handed poisons the cache for everyone
        // else.** `SendPropConformanceTests` calls `UnionWith` on this result to fold in the aliased
        // `SENDINFO_NAME` sweep, which wrote those names into the entry cached for the FIRST pattern
        // — precisely the shared answer the comment above says must not happen, arriving by mutation
        // rather than by a key collision.
        //
        // It also failed the way an unsynchronised collection does. NUnit runs these in parallel, so
        // one test's `UnionWith` overlapped another's `Contains` on the same `HashSet`, and a
        // `ShouldContain("moveparent")` failed while its own failure message listed `moveparent`
        // among the actual values. Intermittent, and it passed on a re-run — which is the shape this
        // project treats as a defect rather than as noise.
        //
        // Copying on the way out is affordable: the remarks above already record that the caching's
        // benefit was unmeasurable, so correctness costs nothing here worth counting.
        return new HashSet<string>(Cached(relativeFolder, pattern, match, recursive), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The shared sweep result, which callers must never be handed directly.</summary>
    private static HashSet<string> Cached(
        string relativeFolder, string pattern, Regex match, bool recursive)
    {
        return Matched.GetOrAdd(
            $"{relativeFolder}|{pattern}|{recursive}|{match}",
            _ =>
            {
                HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

                IEnumerable<string> captured = Sweep(relativeFolder, pattern, recursive)
                    .Select(Contents)
                    .SelectMany(text => match.Matches(text))
                    .Select(hit => hit.Groups[1].Value);

                foreach (string name in captured)
                {
                    found.Add(name);
                }

                return found;
            });
    }

    /// <summary>Sweeps already performed, keyed by where, what files, and which pattern.</summary>
    private static readonly ConcurrentDictionary<string, HashSet<string>> Matched =
        new(StringComparer.Ordinal);

    /// <summary>File lists already walked, keyed by folder, pattern and depth.</summary>
    private static readonly ConcurrentDictionary<string, string[]> Walked =
        new(StringComparer.Ordinal);

    /// <summary>Absolute file paths in a folder, walked once.</summary>
    private static string[] Sweep(string relativeFolder, string pattern, bool recursive) =>
        Walked.GetOrAdd(
            $"{relativeFolder}|{pattern}|{recursive}",
            _ => [.. Files(relativeFolder, pattern, recursive)]);

    /// <summary>One absolute path's contents, read once.</summary>
    private static string Contents(string absolutePath) =>
        Read.GetOrAdd("abs:" + absolutePath, _ => File.ReadAllText(absolutePath)) ?? string.Empty;

    /// <summary>One enum's members with the values C gives them, counting implicit ones.</summary>
    /// <param name="relativePath">Path under the checkout, such as
    /// <c>src/public/bitmap/imageformat.h</c>.</param>
    /// <param name="name">The enum's name, such as <c>ImageFormat</c>.</param>
    /// <returns>Member to value, empty when the enum or the file is absent.</returns>
    /// <remarks>
    /// **Most Source enums number themselves implicitly, and <see cref="Constants"/> cannot see
    /// those.** <c>ImageFormat</c> assigns a value to exactly two of its forty members — −1 and 0 —
    /// and every other format is defined only by its POSITION in the list. A pattern that reads
    /// <c>NAME = value</c> comes back with two entries and looks like it worked.
    ///
    /// That is the more dangerous half of the format: a texture's format byte selects how its pixels
    /// are decoded, and the wrong selection produces an image rather than an error. So the counter
    /// is modelled the way C does it — start at zero, an explicit assignment resets it, every
    /// member takes the next value.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Enumerators(string relativePath, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Parsed.GetOrAdd($"enum:{relativePath}:{name}", _ => ReadEnumerators(relativePath, name));
    }

    /// <summary>Does the work behind <see cref="Enumerators"/>, once per enum.</summary>
    private static Dictionary<string, int> ReadEnumerators(string relativePath, string name)
    {
        Dictionary<string, int> values = new(StringComparer.Ordinal);

        if (Text(relativePath) is not { } text)
        {
            return values;
        }

        Match declaration = Regex.Match(
            text,
            @"enum\s+" + Regex.Escape(name) + @"\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline,
            PatternLimit);

        if (!declaration.Success)
        {
            return values;
        }

        string body = Regex.Replace(
            declaration.Groups["body"].Value, @"//[^\n]*", " ", RegexOptions.None, PatternLimit);

        int next = 0;

        foreach (string member in body.Split(','))
        {
            Match parsed = Regex.Match(
                member.Trim(),
                @"^([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*(-?\d+))?$",
                RegexOptions.None,
                PatternLimit);

            if (!parsed.Success)
            {
                continue;
            }

            if (parsed.Groups[2].Success)
            {
                next = int.Parse(parsed.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            values[parsed.Groups[1].Value] = next;
            next++;
        }

        return values;
    }

    /// <summary>Every named integer a header declares, by <c>#define</c> or as an enumerator.</summary>
    /// <param name="relativePath">Path under the checkout, such as <c>src/public/bspfile.h</c>.</param>
    /// <returns>Name to value, for the ones that are plain integers.</returns>
    /// <remarks>
    /// **This is the highest-value axis of the whole reference.** A format reader is mostly magic
    /// numbers — a lump index, a version bound, a vertex size, a bit width — and every one of them
    /// fails the same way when wrong: it lands on real data and decodes something plausible. The
    /// engine declares them all, so they can be checked rather than trusted. It caught
    /// <c>LUMP_FACES_HDR</c> written as 54 within minutes of the constant being typed; the real
    /// value is 58.
    ///
    /// Only plain integers and hexadecimal are returned. Expressions like
    /// <c>('V'&lt;&lt;24)+('S'&lt;&lt;16)+…</c> and anything built from other constants are skipped
    /// rather than half-evaluated: a wrong value here would be worse than a missing one, because it
    /// would fail a test that is supposed to be the reference.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Constants(string relativePath) =>
        Parsed.GetOrAdd($"const:{relativePath}", _ => ReadConstants(relativePath));

    /// <summary>Does the work behind <see cref="Constants"/>, once per file.</summary>
    private static Dictionary<string, int> ReadConstants(string relativePath)
    {
        Dictionary<string, int> values = new(StringComparer.Ordinal);

        if (Text(relativePath) is not { } text)
        {
            return values;
        }

        // **Mixed case is allowed after the first character**, because Valve's own names are not
        // all uppercase: `TCOMBINE_RGB_EQUALS_BASE_x_DETAILx2` in common_ps_fxc.h has two lowercase
        // letters, and an uppercase-only pattern silently omits it. A missing constant here does
        // not fail — it makes whatever asked for it look unchecked, which is the failure mode this
        // whole reference is built against.
        Regex defined = new(
            @"^\s*#define\s+([A-Za-z_][A-Za-z0-9_]*)\s+\(?\s*(0x[0-9A-Fa-f]+|\d+)\s*\)?\s*(?://.*)?$",
            RegexOptions.Multiline,
            PatternLimit);

        Regex enumerated = new(
            @"^\s*([A-Z][A-Z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,",
            RegexOptions.Multiline,
            PatternLimit);

        // **`(1 << n)` is how Valve writes most flag sets**, and skipping it left whole headers
        // looking empty. soundflags.h declares every SND_* that way, so an extraction that only
        // understood literals reported no sound flags at all — which reads as "the header moved"
        // rather than "the pattern is too narrow". Only this one shape is evaluated; anything built
        // from other constants is still skipped rather than half-computed.
        Regex shifted = new(
            @"(?:^\s*#define\s+|^\s*)([A-Za-z_][A-Za-z0-9_]*)\s*=?\s*\(\s*1\s*<<\s*(\d+)\s*\)",
            RegexOptions.Multiline,
            PatternLimit);

        // **The same shape with a NAME where the number goes**, which is how a header states that one
        // constant is defined in terms of another: `#define MAX_BLOCK_SIZE (1<<MAX_BLOCK_BITS )`.
        //
        // Worth resolving rather than skipping, because the relationship is exactly what a
        // conformance test wants to check. Reading only the bit count and computing the size in the
        // test would be transcribing Valve's arithmetic into our code — the same mistake as
        // transcribing a number, one level up.
        //
        // Resolved in a second pass against what the literal patterns already found, and only one
        // level deep. A macro built from a macro built from a macro is not evaluated: half-computing
        // an expression is worse than declining it, because the wrong answer still looks like a
        // number.
        Regex shiftedByName = new(
            @"(?:^\s*#define\s+|^\s*)([A-Za-z_][A-Za-z0-9_]*)\s*=?\s*\(\s*1\s*<<\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)",
            RegexOptions.Multiline,
            PatternLimit);

        static int Number(string text) =>
            text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(text[2..], 16)
                : int.Parse(text, CultureInfo.InvariantCulture);

        IEnumerable<(string Name, int Value)> declarations = new[] { defined, enumerated }
            .SelectMany(pattern => pattern.Matches(text))
            .Select(hit => (hit.Groups[1].Value, Number(hit.Groups[2].Value)))
            .Concat(shifted
                .Matches(text)
                .Select(hit => (
                    hit.Groups[1].Value,
                    1 << int.Parse(hit.Groups[2].Value, CultureInfo.InvariantCulture))));

        foreach ((string name, int value) in declarations)
        {
            // First declaration wins: a header that redefines a name under an #ifdef is describing
            // a variant build, and the first is the one these readers target.
            //
            // **Load-bearing far beyond one constant.** bspfile.h declares roughly thirty capacities
            // twice, and every second declaration is the literal 2 — the BSP_USE_LESS_MEMORY build
            // keeps the types and throws away the limits. Reversing this one call silently turns
            // MAX_MAP_TEXDATA from 2048 into 2, which does not throw: it makes a guard reject every
            // real map, and that reads as a corrupt file. SdkConstantResolutionTests holds the line.
            values.TryAdd(name, value);
        }

        // Second pass, after the literals are in hand, so a name-shift can be resolved against them.
        // Ordered rather than folded into the loop above because the operand may be declared later in
        // the file than the macro that uses it.
        // **A plain alias: `#define A B`, where B is another constant.** Valve uses it to give one
        // bit two meanings deliberately — `#define SPROP_VARINT SPROP_NORMAL`, with the comment
        // "reuse existing flag so we don't break demo". Skipping aliases makes a name that is very
        // much declared look absent, which reads as "the header moved".
        //
        // Same one-level rule as the shift above, and the same reason.
        Regex aliased = new(
            @"^\s*#define\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?://.*)?$",
            RegexOptions.Multiline,
            PatternLimit);

        IEnumerable<(string Name, int Value)> byAlias = aliased
            .Matches(text)
            .Select(hit => (
                Name: hit.Groups[1].Value,
                Value: values.TryGetValue(hit.Groups[2].Value, out int target) ? target : int.MinValue))
            .Where(pair => pair.Value != int.MinValue);

        foreach ((string name, int value) in byAlias)
        {
            values.TryAdd(name, value);
        }

        IEnumerable<(string Name, int Bits)> byName = shiftedByName
            .Matches(text)
            .Select(hit => (
                Name: hit.Groups[1].Value,
                Bits: values.TryGetValue(hit.Groups[2].Value, out int bits) ? bits : -1))
            .Where(pair => pair.Bits is >= 0 and < 31);

        foreach ((string name, int bits) in byName)
        {
            values.TryAdd(name, 1 << bits);
        }

        return values;
    }
}
