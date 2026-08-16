using System;
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

    /// <summary>The full text of one file under the SDK, or null when it is not there.</summary>
    /// <param name="relativePath">Path under the checkout, such as <c>src/public/bspfile.h</c>.</param>
    /// <returns>The file's contents, or null.</returns>
    public static string? Text(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (Root is not { } root)
        {
            return null;
        }

        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path) ? File.ReadAllText(path) : null;
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

        HashSet<string> found = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> captured = Files(relativeFolder, pattern, recursive)
            .SelectMany(file => match.Matches(File.ReadAllText(file)))
            .Select(hit => hit.Groups[1].Value);

        foreach (string name in captured)
        {
            found.Add(name);
        }

        return found;
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
    public static IReadOnlyDictionary<string, int> Constants(string relativePath)
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

        static int Number(string text) =>
            text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(text[2..], 16)
                : int.Parse(text, CultureInfo.InvariantCulture);

        IEnumerable<(string Name, int Value)> declarations = new[] { defined, enumerated }
            .SelectMany(pattern => pattern.Matches(text))
            .Select(hit => (hit.Groups[1].Value, Number(hit.Groups[2].Value)));

        foreach ((string name, int value) in declarations)
        {
            // First declaration wins: a header that redefines a name under an #ifdef is describing
            // a variant build, and the first is the one these readers target.
            values.TryAdd(name, value);
        }

        return values;
    }
}
