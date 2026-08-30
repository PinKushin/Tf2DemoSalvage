using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which properties the demo carries that this project never reads, counted from the engine.
/// </summary>
/// <remarks>
/// **Built because finding these one at a time was costing a session each.** Every entity bug on
/// 2026-08-30 had the same shape: a field the wire carries and nothing here consumes. The owner,
/// after the sixth: *"really we just need to be finding what we wer doing wrong, where we are
/// diverging. this shouldnt be this difficult"*. It should not, and it is not — the list is
/// extractable.
///
/// **The denominator comes from the ENGINE, so it cannot flatter us.** An audit that starts from
/// this project's own accessors can only find fields we already decode; the two most expensive gaps
/// of that day were both invisible to it:
///
/// - <c>m_flAnimTime</c>, cited in seven comments and decoded nowhere, because it lives in
///   <c>DT_AnimTimeMustBeFirst</c> rather than <c>DT_BaseEntity</c>.
/// - <c>m_nDisguiseTeam</c> / <c>m_nDisguiseClass</c>, networked at
///   <c>tf_player_shared.cpp:400</c>, with the string "Disguise" appearing zero times in the whole
///   managed tree.
///
/// **The instrument is checked before its output means anything.** A regex that matched nothing
/// would report perfect coverage of an empty set — the most flattering possible way to be wrong,
/// and a shape this project has hit before. So the extraction is floored, and a property this
/// project demonstrably DOES read is asserted present as a positive control.
///
/// **It says how many, not what they cost.** A viewer needs <c>m_nSequence</c> and does not need
/// every pose parameter equally; `docs/CONFORMANCE.md` carries that judgement. This names the
/// candidates so nobody has to discover them from a screenshot again.
/// </remarks>
public sealed class NetworkedPropertyCoverageTests
{
    /// <summary>Where the generated report is written.</summary>
    /// <remarks>
    /// Into the repository beside `SDK-COVERAGE.md` and for the same reason: a number that exists
    /// only in a test run cannot show that a change moved it.
    /// </remarks>
    private static string ReportPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "docs", "WIRE-COVERAGE.md"));

    /// <summary>
    /// Tables worth reporting in detail: the ones an entity this viewer DRAWS actually composes.
    /// </summary>
    /// <remarks>
    /// **A shortlist for the detail, not for the count.** The engine declares hundreds of tables and
    /// most belong to entities no demo viewer draws — vehicles, the HUD, Portal's paint. Reporting
    /// every one buries the ones that matter, and reporting only these would let a gap hide by
    /// living somewhere nobody listed. So the COUNT covers everything and the DETAIL covers these.
    /// </remarks>
    private static readonly string[] Detailed =
    [
        "DT_BaseEntity",
        "DT_AnimTimeMustBeFirst",
        "DT_BaseAnimating",
        "DT_BaseAnimatingOverlay",
        "DT_ServerAnimationData",
        "DT_BaseCombatWeapon",
        "DT_BaseCombatCharacter",
        "DT_BasePlayer",
        "DT_TFPlayer",
        "DT_TFPlayerShared",
        "DT_TFLocalPlayerExclusive",
        "DT_TFNonLocalPlayerExclusive",
        "DT_TFWeaponBase",
        "DT_ScriptCreatedItem",
        "DT_EconEntity",
        "DT_TFWearable",
        "DT_DynamicProp",
        "DT_BaseViewModel",
    ];

    [Test]
    public void WireCoverage_EveryNetworkedProperty_IsCountedAndReported()
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
            return;
        }

        IReadOnlyList<NetworkedProperty> declared = RecvTableInventory.All();

        // **The extraction is checked first.** A pattern that matched nothing reports flawless
        // coverage of an empty set, and every number after it would be a lie in the flattering
        // direction.
        declared.Count.ShouldBeGreaterThan(
            600, "RECVINFO extraction found too few properties to be real");

        declared.Select(entry => entry.Table).Distinct().Count().ShouldBeGreaterThan(
            80, "RecvTable extraction found too few tables to be real");

        // The two gaps that motivated this, asserted as extraction controls rather than as
        // findings: if the sweep cannot see them it cannot see anything of their kind.
        Declares(declared, "DT_AnimTimeMustBeFirst", "m_flAnimTime").ShouldBeTrue(
            "m_flAnimTime is declared in its own table, which is why asking DT_BaseEntity missed it");

        Declares(declared, "DT_TFPlayerShared", "m_nDisguiseClass").ShouldBeTrue(
            "the spy disguise fields are networked and this project reads neither");

        // **The positive control on the OTHER half.** `AnyProductionAssemblyMentions` searches the
        // built assemblies beside this test; a test project that does not reference the assembly
        // holding a name gets a false "absent". Without this, a broken search reports every property
        // as unread and the report becomes noise that nobody trusts twice.
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            $"the search cannot find '{SchemaGap.KnownPresent}', which this project demonstrably reads");

        Dictionary<string, bool> read = new(StringComparer.Ordinal);

        foreach (NetworkedProperty entry in declared)
        {
            if (!read.ContainsKey(entry.Property))
            {
                read[entry.Property] = SchemaGap.AnyProductionAssemblyMentions(entry.Property);
            }
        }

        int covered = declared.Count(entry => read[entry.Property]);

        StringBuilder report = new();

        report.Append(
            "# Coverage of what the wire carries\n\n" +
            "**Generated by `NetworkedPropertyCoverageTests`. Do not edit.** Extracted from\n" +
            "`source-sdk-2013`'s client RecvTables and diffed against the strings this project's\n" +
            "shipped assemblies contain, so it cannot drift from the engine without the engine\n" +
            "changing.\n\n" +
            "**A name here is something the DEMO can tell us, not something this viewer needs\n" +
            "equally.** `docs/CONFORMANCE.md` carries what a gap costs; this carries how many there\n" +
            "are and where they live.\n\n" +
            "**\"Read\" means a shipped assembly contains the string.** That is a lower bound on\n" +
            "ignorance rather than proof of use — a name can be mentioned and still not honoured,\n" +
            "which is its own recurring bug here. A property this report calls read may still be\n" +
            "decoded and dropped.\n\n");

        report.Append(
            CultureInfo.InvariantCulture,
            $"**{covered} of {declared.Count}** declared table/property pairs are mentioned at all, ");

        report.Append(
            CultureInfo.InvariantCulture,
            $"across {declared.Select(entry => entry.Table).Distinct().Count()} tables.\n\n");

        report.Append("## Tables an entity this viewer draws composes\n\n");

        foreach (string table in Detailed)
        {
            NetworkedProperty[] inTable =
                [.. declared.Where(entry => string.Equals(entry.Table, table, StringComparison.Ordinal))];

            if (inTable.Length == 0)
            {
                report.Append(CultureInfo.InvariantCulture, $"### {table}\n\nNot declared in this SDK.\n\n");
                continue;
            }

            string[] missing =
            [
                .. inTable.Where(entry => !read[entry.Property])
                    .Select(entry => entry.Property)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal),
            ];

            report.Append(
                CultureInfo.InvariantCulture,
                $"### {table}\n\n**{inTable.Length - missing.Length} of {inTable.Length}** mentioned.\n\n");

            report.Append(
                missing.Length == 0
                    ? "Nothing unread.\n\n"
                    : $"Not mentioned anywhere in a shipped assembly:\n\n```\n{string.Join(", ", missing)}\n```\n\n");
        }

        // **Every other table, ranked by how much of it is unread.** The shortlist above is a
        // judgement about what a viewer draws and judgements go stale; this is the backstop that
        // surfaces a table nobody thought to list.
        report.Append("## Every other table, worst first\n\n");

        var others = declared
            .Where(entry => !Detailed.Contains(entry.Table, StringComparer.Ordinal))
            .GroupBy(entry => entry.Table, StringComparer.Ordinal)
            .Select(group => new
            {
                Table = group.Key,
                Total = group.Count(),
                Unread = group.Count(entry => !read[entry.Property]),
            })
            .Where(row => row.Unread > 0)
            .OrderByDescending(row => row.Unread)
            .ThenBy(row => row.Table, StringComparer.Ordinal)
            .Take(40)
            .ToList();

        report.Append("| table | unread | declared |\n|---|---|---|\n");

        foreach (var row in others)
        {
            report.Append(
                CultureInfo.InvariantCulture,
                $"| `{row.Table}` | {row.Unread} | {row.Total} |\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, report.ToString());

        TestContext.Out.WriteLine(
            $"{covered} of {declared.Count} wire properties mentioned; report at {ReportPath}");
    }

    private static bool Declares(
        IReadOnlyList<NetworkedProperty> declared, string table, string property) =>
        declared.Any(entry =>
            string.Equals(entry.Table, table, StringComparison.Ordinal)
            && string.Equals(entry.Property, property, StringComparison.Ordinal));
}
