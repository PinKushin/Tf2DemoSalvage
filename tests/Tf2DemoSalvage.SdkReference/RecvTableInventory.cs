using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>One networked property, as the engine's client RecvTable declares it.</summary>
/// <param name="Table">The <c>DT_</c> table declaring it, which is half of a lookup key.</param>
/// <param name="Property">The wire name, e.g. <c>m_nSequence</c>.</param>
/// <param name="File">Where it was found, so a gap can be read in context.</param>
public readonly record struct NetworkedProperty(string Table, string Property, string File);

/// <summary>
/// Every property Source's client actually receives, extracted from its own RecvTables.
/// </summary>
/// <remarks>
/// **This is the denominator for "what does the demo tell us that we ignore".** Every entity bug
/// found on 2026-08-30 had one of two shapes — a field decoded and never read, or a field never
/// decoded because it was looked up under the wrong table. Both are invisible to a search that
/// starts from what this project already knows about, which is why the audit has to start from the
/// ENGINE's list. It is the argument <see cref="SdkInventory"/> already makes for shader parameters
/// and BSP lumps, applied to the wire.
///
/// **Two fields cost a whole session between them and neither could have been found from our side:**
///
/// - <c>m_flAnimTime</c>, cited in seven comments here and decoded nowhere. It is declared in
///   <c>DT_AnimTimeMustBeFirst</c> — Valve gives it a table of its own to force it first on the
///   wire — so asking under <c>DT_BaseEntity</c> silently matched nothing.
/// - <c>m_nDisguiseTeam</c> and <c>m_nDisguiseClass</c>, networked at
///   <c>tf_player_shared.cpp:400</c>. The string "Disguise" appeared ZERO times in the whole managed
///   tree, and the symptom reached the owner as *"a spy looked like a blue spy and a red demo at the
///   same time"*.
///
/// **The table is captured with the property, not just the name.** Our lookups are keyed
/// <c>"DT_Table.m_property"</c> (see <c>docs/memory/a-property-name-needs-its-declaring-table.md</c>),
/// so a bare name cannot say whether we are asking in the right place — which is exactly how
/// <c>m_flAnimTime</c> hid.
///
/// **What it cannot do**: say whether a gap MATTERS. A viewer needs `m_nSequence` and does not need
/// `m_flPoseParameter[13]` equally. That judgement belongs in `docs/CONFORMANCE.md`; this says how
/// many there are and names them.
/// </remarks>
public static class RecvTableInventory
{
    /// <summary>Guards a pathological pattern against a file that is not what we think.</summary>
    private static readonly TimeSpan MatchLimit = TimeSpan.FromSeconds(5);

    /// <summary>Opens a table and names it. Three spellings, all in use in the SDK.</summary>
    /// <remarks>
    /// <c>IMPLEMENT_CLIENTCLASS_DT( C_TFPlayer, DT_TFPlayer, CTFPlayer )</c> for a networked class,
    /// and <c>BEGIN_RECV_TABLE</c> / <c>BEGIN_RECV_TABLE_NOBASE</c> for the embedded tables a class
    /// composes — which is where <c>DT_TFPlayerShared</c> and <c>DT_AnimTimeMustBeFirst</c> live.
    /// Matching only the first would miss most of what a player sends.
    /// </remarks>
    private static readonly Regex Opens = new(
        @"(?:IMPLEMENT_CLIENTCLASS_DT|BEGIN_RECV_TABLE(?:_NOBASE)?)\s*\(\s*[A-Za-z0-9_]+\s*,\s*(DT_[A-Za-z0-9_]+)",
        RegexOptions.Compiled,
        MatchLimit);

    /// <summary>Closes one.</summary>
    private static readonly Regex Closes = new(
        @"END_RECV_TABLE\s*\(",
        RegexOptions.Compiled,
        MatchLimit);

    /// <summary>
    /// Names a received property. <c>RECVINFO</c>, its array forms, and the ALIASED form.
    /// </summary>
    /// <remarks>
    /// **<c>RECVINFO_NAME( m_local, m_wire )</c> takes the WIRE name second**, which is the trap
    /// <c>docs/memory/wire-names-are-strings.md</c> records for the sending side. Capturing the
    /// first argument would inventory names that never travel and miss the ones that do, so the
    /// alias form is matched separately and its second argument taken.
    /// </remarks>
    private static readonly Regex Received = new(
        @"RECVINFO(?:_ARRAY|_ARRAY3|_VECTOR|_DT|_STRUCTARRAYELEM)?\s*\(\s*([A-Za-z_][A-Za-z0-9_\[\]\.]*)\s*\)",
        RegexOptions.Compiled,
        MatchLimit);

    /// <summary>The aliased form, whose SECOND argument is what travels.</summary>
    private static readonly Regex Aliased = new(
        @"RECVINFO_NAME\s*\(\s*[A-Za-z_][A-Za-z0-9_\[\]\.]*\s*,\s*([A-Za-z_][A-Za-z0-9_\[\]\.]*)\s*\)",
        RegexOptions.Compiled,
        MatchLimit);

    /// <summary>Every networked property the client's RecvTables declare, with its table.</summary>
    /// <returns>One entry per table/property pair, deduplicated.</returns>
    /// <remarks>
    /// Swept over <c>src/game/client</c> recursively, which is where every <c>C_</c> class and the
    /// TF-specific ones under <c>tf/</c> live. Returns empty when the SDK is not available, so a
    /// caller must check <see cref="SdkInventory.Root"/> and skip rather than read a confident zero.
    /// </remarks>
    public static IReadOnlyList<NetworkedProperty> All()
    {
        if (SdkInventory.Root is not { } root)
        {
            return [];
        }

        // **BOTH client and shared, and the control is what proved it.** A first version swept
        // only `src/game/client` and missed `m_nDisguiseClass` entirely — TF's player state is
        // declared in `src/game/shared/tf/tf_player_shared.cpp`, compiled into both sides, and its
        // RecvTable sits there rather than under `client`. The extraction control asserting that
        // very field is what caught it, which is the whole reason an inventory needs one.
        string[] folders =
        [
            Path.Combine(root, "src", "game", "client"),
            Path.Combine(root, "src", "game", "shared"),
        ];

        HashSet<NetworkedProperty> found = [];

        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string file in
                Directory.EnumerateFiles(folder, "*.cpp", SearchOption.AllDirectories))
            {
                Collect(File.ReadAllText(file), Path.GetFileName(file), found);
            }
        }

        return [.. found.OrderBy(entry => entry.Table, StringComparer.Ordinal)
            .ThenBy(entry => entry.Property, StringComparer.Ordinal)];
    }

    /// <summary>Walks one file, attributing each property to the table it sits inside.</summary>
    /// <remarks>
    /// **By position rather than by parsing C++.** A table's properties are every `RECVINFO` between
    /// its opening macro and the next `END_RECV_TABLE`, and that is true without understanding a
    /// single declaration — which is the only reason this is a regex and not a compiler.
    ///
    /// A `RECVINFO` outside any table is dropped rather than guessed at. There are a few, in macros
    /// and comments, and attributing them to whichever table happened to precede them would be
    /// worse than losing them: a wrong table is what this whole inventory exists to catch.
    /// </remarks>
    private static void Collect(string text, string file, HashSet<NetworkedProperty> into)
    {
        foreach (Match open in Opens.Matches(text))
        {
            string table = open.Groups[1].Value;

            Match close = Closes.Match(text, open.Index);

            int end = close.Success ? close.Index : text.Length;

            if (end <= open.Index)
            {
                continue;
            }

            string body = text[open.Index..end];

            foreach (Match hit in Received.Matches(body))
            {
                into.Add(new NetworkedProperty(table, Normalise(hit.Groups[1].Value), file));
            }

            foreach (Match hit in Aliased.Matches(body))
            {
                into.Add(new NetworkedProperty(table, Normalise(hit.Groups[1].Value), file));
            }
        }
    }

    /// <summary>Strips an array subscript, so <c>m_hMyWeapons[0]</c> is one property.</summary>
    /// <remarks>
    /// The wire flattens an array into a sub-table whose members are <c>000</c>, <c>001</c> and so
    /// on — see `SyntheticPlayer.SchemaWithResource`'s remarks — so the SUBSCRIPT in a RECVINFO is
    /// C++ addressing the first element, not part of any name that travels.
    /// </remarks>
    private static string Normalise(string name)
    {
        int bracket = name.IndexOf('[', StringComparison.Ordinal);

        return bracket < 0 ? name : name[..bracket];
    }
}
