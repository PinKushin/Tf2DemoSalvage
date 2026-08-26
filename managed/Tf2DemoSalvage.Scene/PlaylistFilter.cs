using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Narrows a playlist to the demos matching what someone typed.
/// </summary>
/// <remarks>
/// **Separate from the form because the form cannot be asked questions cheaply.** A ListView needs
/// a created window handle before its items mean anything, so filtering logic living inside the
/// control would only be testable through a real window. Here it is a pure function over a list.
///
/// The matching is deliberately plain — case-insensitive substrings, all terms required, name and
/// folder both searched. A real archive is 370 files called <c>esea_match_13977649.dem</c>, where
/// the folder is the part a person remembers and the identifier is the part they paste.
/// </remarks>
public static class PlaylistFilter
{
    /// <summary>Puts a library into the order the playlist shows it in.</summary>
    /// <param name="entries">The whole library, in whatever order it was read.</param>
    /// <returns>The entries grouped by folder, and by name within each folder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <remarks>
    /// **This is <see cref="Apply"/>'s precondition, and it lived in another project** until
    /// 2026-08-26 (B188, D90) — three lines inline in `MainForm`, while `Apply` documented its
    /// dependence on them: *"Order is preserved because the playlist groups by folder afterwards,
    /// and reordering would scatter one folder across several groups of the same name."* A contract
    /// stated in one assembly and satisfied in another is a contract nothing enforces.
    ///
    /// **Case-insensitive, because the library is read off a Windows disk** where `Season31` and
    /// `season31` are one folder that may be spelled either way by whoever made it. An ordinal sort
    /// puts every capital before every lowercase, so `ESEA` and `esea` would land at opposite ends
    /// of the list with everything else between them — the exact scattering the grouping prevents.
    /// </remarks>
    public static IReadOnlyList<DemoEntry> Order(IReadOnlyList<DemoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries
            .OrderBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Keeps the entries matching every term in a query.</summary>
    /// <param name="entries">The whole library.</param>
    /// <param name="query">What the user typed; empty keeps everything.</param>
    /// <returns>The matching entries, in their original order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <remarks>
    /// Terms narrow rather than widen, and each may match a different field: "season 13977649" is
    /// a folder and a file, and requiring both to hit the same one would return nothing for the
    /// most natural query there is.
    ///
    /// Order is preserved because the playlist groups by folder afterwards, and reordering would
    /// scatter one folder across several groups of the same name. <see cref="Order"/> is what
    /// establishes that grouping, and it sits above rather than in a window.
    /// </remarks>
    public static IReadOnlyList<DemoEntry> Apply(IReadOnlyList<DemoEntry> entries, string query)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string[] terms = (query ?? string.Empty).Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return entries;
        }

        return [.. entries.Where(entry => Matches(entry, terms))];
    }

    private static bool Matches(DemoEntry entry, string[] terms)
    {
        foreach (string term in terms)
        {
            bool found =
                entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Folder.Contains(term, StringComparison.OrdinalIgnoreCase);

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
