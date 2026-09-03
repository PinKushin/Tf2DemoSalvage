using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Our copy of Valve's per-weapon activity tables still matches Valve's.
/// </summary>
/// <remarks>
/// **A transcribed table is a liability unless something re-reads the source**, and this project has
/// said so before: `SdkCoverageTests` generates its denominators from the SDK precisely so they
/// cannot go stale. `WeaponActivityTable` is 415 rows lifted out of `tf_weaponbase.cpp`, and it is
/// exactly the kind of thing that rots silently — a wrong row means an activity resolves to no
/// sequence, which draws no gesture and logs nothing.
///
/// So this parses the same tables out of the SDK and compares them entry for entry. It skips when
/// the SDK is absent, which is the machine without it rather than a pass.
/// </remarks>
public sealed class WeaponActivityConformanceTests
{
    /// <summary>Where the tables live.</summary>
    private const string WeaponBase = "src/game/shared/tf/tf_weaponbase.cpp";

    [Test]
    public void WeaponActivityTable_EveryRole_MatchesTheSdk()
    {
        if (SourceSdk.Text(WeaponBase) is not { } source)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        Dictionary<string, Dictionary<string, string>> sdk = Parse(source);

        // The control: a parse that found nothing would agree with anything.
        sdk.Count.ShouldBeGreaterThan(
            8, "tf_weaponbase.cpp declares a table per weapon role, and there are twelve");

        foreach ((string role, Dictionary<string, string> table) in sdk)
        {
            IReadOnlyDictionary<string, string> ours = WeaponActivityTable.For(role);

            foreach ((string from, string to) in table)
            {
                ours.ContainsKey(from).ShouldBeTrue(
                    $"{role} maps {from} in the SDK and this table does not mention it");

                ours[from].ShouldBe(
                    to, $"{role} maps {from} to {to} in the SDK");
            }
        }
    }

    /// <remarks>
    /// **The other direction, and it is the half that catches an invention.** The test above passes
    /// against a table with extra rows in it — a name typed from memory, a row kept after Valve
    /// removed it — and an extra row is not harmless: it rewrites an activity the engine would have
    /// left alone.
    /// </remarks>
    [Test]
    public void WeaponActivityTable_NoRow_IsAbsentFromTheSdk()
    {
        if (SourceSdk.Text(WeaponBase) is not { } source)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        Dictionary<string, Dictionary<string, string>> sdk = Parse(source);

        foreach (string role in WeaponActivityTable.Roles)
        {
            sdk.ContainsKey(role).ShouldBeTrue($"{role} is not a role tf_weaponbase.cpp declares");

            foreach ((string from, string to) in WeaponActivityTable.For(role))
            {
                sdk[role].ContainsKey(from).ShouldBeTrue(
                    $"{role} maps {from} here and nowhere in the SDK");

                sdk[role][from].ShouldBe(to, $"{role} maps {from} differently in the SDK");
            }
        }
    }

    /// <remarks>
    /// **The behaviour the table exists for**, asserted on the case that made it necessary. A
    /// suffix rule gets the reload right and the attack wrong, so both are named here: the attack is
    /// a RENAME and the reload is an append, and a crouch-deployed is neither.
    /// </remarks>
    [Test]
    public void Override_ForAPrimaryWeapon_RewritesTheEngineNames()
    {
        WeaponActivityTable.Override("PRIMARY", "ACT_MP_RELOAD_STAND")
            .ShouldBe("ACT_MP_RELOAD_STAND_PRIMARY");

        WeaponActivityTable.Override("PRIMARY", "ACT_MP_ATTACK_STAND_PRIMARYFIRE")
            .ShouldBe("ACT_MP_ATTACK_STAND_PRIMARY", "a rename, not a suffix");

        WeaponActivityTable.Override("PRIMARY", "ACT_MP_CROUCH_DEPLOYED")
            .ShouldBe("ACT_MP_CROUCHWALK_DEPLOYED", "neither a rename nor a suffix");

        WeaponActivityTable.Override("SECONDARY", "ACT_MP_RELOAD_STAND")
            .ShouldBe("ACT_MP_RELOAD_STAND_SECONDARY", "the role decides, not the activity");
    }

    /// <remarks>
    /// **A miss returns the activity unchanged**, which is `CBaseCombatWeapon::ActivityOverride`'s
    /// own behaviour: it returns what it was handed when no row matches. A flinch is the live case —
    /// no weapon rewrites one, so it must reach the model under its own name rather than vanishing.
    /// </remarks>
    [Test]
    public void Override_ForAnActivityNoWeaponRewrites_ReturnsItUnchanged()
    {
        WeaponActivityTable.Override("PRIMARY", "ACT_MP_GESTURE_FLINCH_CHEST")
            .ShouldBe("ACT_MP_GESTURE_FLINCH_CHEST");

        WeaponActivityTable.Override("NOT_A_ROLE", "ACT_MP_RELOAD_STAND")
            .ShouldBe("ACT_MP_RELOAD_STAND", "an unknown role rewrites nothing");
    }

    /// <summary>Reads every <c>acttable_t s_acttableX[]</c> out of the SDK source.</summary>
    /// <param name="source">The file's text.</param>
    /// <returns>Role to source activity to mapped activity.</returns>
    /// <remarks>
    /// **The first row for a pair wins**, which is what `ActivityOverride`'s linear walk does — it
    /// returns on its first match.
    /// </remarks>
    private static Dictionary<string, Dictionary<string, string>> Parse(string source)
    {
        Dictionary<string, Dictionary<string, string>> tables = new(StringComparer.Ordinal);

        foreach (Match table in Regex.Matches(
            source,
            @"acttable_t\s+s_acttable(?<role>[A-Za-z0-9]+)\s*\[\]\s*=\s*\{(?<body>.*?)\n\};",
            RegexOptions.Singleline))
        {
            Dictionary<string, string> rows = new(StringComparer.Ordinal);

            foreach (Match row in Regex.Matches(
                table.Groups["body"].Value,
                @"\{\s*(?<from>ACT_[A-Za-z0-9_]+)\s*,\s*(?<to>ACT_[A-Za-z0-9_]+)\s*,"))
            {
                _ = rows.TryAdd(row.Groups["from"].Value, row.Groups["to"].Value);
            }

            if (rows.Count > 0)
            {
                tables[table.Groups["role"].Value.ToUpperInvariant()] = rows;
            }
        }

        return tables;
    }
}
