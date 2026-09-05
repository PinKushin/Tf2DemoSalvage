using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Every property is looked for in the table the engine actually declares it in.
/// </summary>
/// <remarks>
/// **The gap this closes let a real defect through the whole suite.**
/// <see cref="SendPropConformanceTests"/> checks that each name appears in SOME send table anywhere
/// in the SDK, and deliberately so — this project decodes generically, so a name is legitimate if
/// any class sends it. But the key a demo is searched with is <c>Table.Property</c>, and a name that
/// is real in the wrong table finds nothing at all.
///
/// That is exactly what happened. <c>m_fFlags</c> was looked for in <c>DT_LocalPlayerExclusive</c>,
/// which passed the existing test because <c>m_fFlags</c> is a real property — while
/// <c>player.cpp:8183</c> declares it in <c>DT_BasePlayer</c>, with no exclusivity and
/// <c>SPROP_CHANGES_OFTEN</c>. The lookup never matched for any player in any demo, the activity
/// state machine took its "nothing said" branch forever, and no player ever crouched or jumped in
/// the viewer. 978 unit tests passed either way.
///
/// **A send table is a block, and that is what makes the pair checkable.** The engine opens one
/// with <c>IMPLEMENT_SERVERCLASS_ST( CBasePlayer, DT_BasePlayer )</c> or
/// <c>BEGIN_SEND_TABLE_NOBASE( CTFPlayer, DT_TFLocalPlayerExclusive )</c> and closes it with
/// <c>END_SEND_TABLE()</c>, so every <c>SENDINFO</c> between the two belongs to that table by
/// construction.
///
/// **Inheritance is deliberately not followed**, and the exclusions below say why each table needs
/// none: every pair asserted here is declared in its own table's block.
/// </remarks>
public sealed class SendTableConformanceTests
{
    /// <summary>Pairs a build older than this SDK sent, proved by a demo rather than by source.</summary>
    /// <remarks>
    /// **The SDK is one build's snapshot and this project reads thirteen years of demos**, so a
    /// name TF2 has since renamed cannot appear in it. The engine keeps the evidence on the receive
    /// side — <c>RecvPropFloat(RECVINFO_NAME(m_flModelScale, m_flModelWidthScale))</c>,
    /// <c>c_baseanimating.cpp:181</c>, with Valve's comment "for demo compatibility only" — but a
    /// receive table has no block structure to tie a name to a TABLE, which is the pair this test
    /// exists to check.
    ///
    /// **So the pair is taken from a demo, which outranks both.**
    /// `schema tf2-2007-build3258-pov-cp_granary m_flModelWidthScale` answers
    /// <c>PROP DT_BaseAnimating.m_flModelWidthScale</c> — the 2007 client's own schema, saying which
    /// table declared it. That is the strongest evidence available for a build whose source nobody
    /// has, and it is the premise of this whole project: a demo carries the schema it was recorded
    /// against.
    ///
    /// **Every entry needs a demo that declares it**, not an argument that it probably existed. One
    /// entry so far (B271).
    /// </remarks>
    private static readonly HashSet<string> Retired =
        ["DT_BaseAnimating.m_flModelWidthScale"];

    [Test]
    public void SendTables_EveryProperty_IsDeclaredInTheTableItIsLookedForIn()
    {
        Dictionary<string, HashSet<string>> tables = SendTables();

        List<string> wrong = [];

        foreach ((string table, IReadOnlyList<string> properties) in EntityState.NetworkedProperties)
        {
            if (!tables.TryGetValue(table, out HashSet<string>? declared))
            {
                wrong.Add($"{table} (no such send table in the SDK)");
                continue;
            }

            foreach (string property in properties)
            {
                // An indexed name is a component of its array, as in the sibling test.
                string sent = property.Contains('[', StringComparison.Ordinal)
                    ? property[..property.IndexOf('[', StringComparison.Ordinal)]
                    : property;

                if (!declared.Contains(sent) && !Retired.Contains($"{table}.{sent}"))
                {
                    wrong.Add($"{table}.{property}");
                }
            }
        }

        wrong.ShouldBeEmpty(
            "these are looked for in a table that does not declare them, so the qualified key can " +
            "never match and the value silently takes its default: " + string.Join(", ", wrong));
    }

    [Test]
    public void SendTables_TheScan_FindsTheTablesThisTestDependsOn()
    {
        // **The control, and this test is worthless without it.** A regex that matched nothing would
        // make the assertion above vacuous for every table at once — it would report "no such send
        // table" for all of them, which is a failure, but a subtler breakage that found the blocks
        // and none of their contents would pass silently.
        Dictionary<string, HashSet<string>> tables = SendTables();

        tables.Count.ShouldBeGreaterThan(100, "the SDK declares hundreds of send tables");

        tables.ShouldContainKey("DT_BasePlayer");
        tables["DT_BasePlayer"].ShouldContain("m_fFlags");
        tables["DT_BasePlayer"].ShouldContain("m_lifeState");

        // And the negative that this whole test exists for: the table it USED to be looked for in
        // does not declare it.
        if (tables.TryGetValue("DT_LocalPlayerExclusive", out HashSet<string>? exclusive))
        {
            exclusive.ShouldNotContain(
                "m_fFlags",
                "if this ever becomes true the bug was not what it was recorded as");
        }
    }

    /// <summary>Every send table the SDK declares, and the properties inside each one.</summary>
    /// <remarks>
    /// Blocks run from the macro that names the table to <c>END_SEND_TABLE()</c>. THREE spellings
    /// are collected: <c>IMPLEMENT_SERVERCLASS_ST</c> for a class's own table,
    /// <c>BEGIN_SEND_TABLE</c> for the standalone ones, and <c>BEGIN_NETWORK_TABLE</c> for the
    /// shared client/server form — each with a <c>_NOBASE</c> variant.
    ///
    /// **The third was missing and it hid every TF2 weapon** (B347). `tf_weapon_minigun.cpp:44`
    /// declares `BEGIN_NETWORK_TABLE( CTFMinigun, DT_WeaponMinigun )`, and that is the spelling all
    /// of `game/shared/tf` uses — so this instrument reported "no such send table in the SDK" for a
    /// table a real demo sends 462 times. The absence was a fact about the PATTERN, which is the
    /// same failure its own `SENDINFO` comment above records one line down.
    /// </remarks>
    private static Dictionary<string, HashSet<string>> SendTables()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        Regex opens = new(
            @"(?:IMPLEMENT_SERVERCLASS_ST(?:_NOBASE)?|BEGIN_SEND_TABLE(?:_NOBASE)?"
            + @"|BEGIN_NETWORK_TABLE(?:_NOBASE)?)\s*\(\s*"
            + @"[A-Za-z_][A-Za-z0-9_]*\s*,\s*(DT_[A-Za-z0-9_]+)\s*\)",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10));

        Regex sends = new(
            // **The dot in the first group is load-bearing.** SENDINFO_STRUCTELEM( m_fog.start )
            // sends under the expression it was handed, so the wire name carries a member access.
            // Matching only identifier characters captured `m_fog` and reported every fog property
            // as declared-nowhere — a fact about the pattern rather than about Valve's tables.
            @"SENDINFO(?:_[A-Z]+)?\(\s*([A-Za-z_][A-Za-z0-9_.]*)\s*(?:,\s*([A-Za-z_][A-Za-z0-9_]*))?",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10));

        Dictionary<string, HashSet<string>> tables = new(StringComparer.Ordinal);

        // **Recursive, and read by absolute path.** Files defaults to the top folder only and
        // src/game holds no .cpp there at all, so the non-recursive call found nothing; and it
        // returns absolute paths while Text takes one relative to the checkout, so every read
        // returned null on top of that. Two independent reasons for the same empty result, which
        // is exactly what the control below exists to catch.
        foreach (string file in SourceSdk.Files("src/game", "*.cpp", recursive: true))
        {
            string text = File.ReadAllText(file);

            foreach (Match open in opens.Matches(text))
            {
                string table = open.Groups[1].Value;

                int from = open.Index + open.Length;
                int to = text.IndexOf("END_SEND_TABLE", from, StringComparison.Ordinal);

                string body = to < 0 ? text[from..] : text[from..to];

                if (!tables.TryGetValue(table, out HashSet<string>? properties))
                {
                    properties = new HashSet<string>(StringComparer.Ordinal);
                    tables[table] = properties;
                }

                foreach (Match send in sends.Matches(body))
                {
                    // SENDINFO_NAME sends under its SECOND argument. Both are added: the member
                    // name is what most tables use, and the alias is what the wire carries for the
                    // few that rename — moveparent being the one this project depends on.
                    properties.Add(send.Groups[1].Value);

                    if (send.Groups[2].Success)
                    {
                        properties.Add(send.Groups[2].Value);
                    }
                }
            }
        }

        return tables;
    }
}
