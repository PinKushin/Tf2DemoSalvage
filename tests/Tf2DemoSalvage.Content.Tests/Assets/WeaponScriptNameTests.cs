using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every weapon the SDK declares resolves to the script name the SDK gives it.
/// </summary>
/// <remarks>
/// **The demo names a server class and the weapon script is named for the entity class.** The SDK
/// pairs them with <c>LINK_ENTITY_TO_CLASS( tf_weapon_bat, CTFBat )</c>. That is source rather than
/// shipped data, so the mapping has to be reproduced in this project — and reproduced mappings go
/// stale silently, which is what this exists to stop.
///
/// **A miss costs nothing visible, which is exactly why it needs enumerating.** A script name that
/// resolves to no file leaves the weapon with no role, and the activity keeps the primary suffix it
/// would have had without any of this work. Correct and broken produce the same picture for the
/// majority of weapons, which are primaries.
///
/// So this asserts across the whole set rather than sampling: every pair in the SDK, with the count
/// checked so a scan that finds nothing cannot pass.
/// </remarks>
public sealed class WeaponScriptNameTests
{
    [Test]
    public void WeaponScriptNames_EveryWeaponInTheSdk_ResolvesToItsOwnScriptName()
    {
        Dictionary<string, string> pairs = Pairs();

        // The control: a regex that matched nothing would make every assertion below vacuous.
        pairs.Count.ShouldBeGreaterThan(
            50,
            "the SDK declares around a hundred tf_weapon classes");

        List<string> unresolved = [];

        foreach ((string serverClass, string entityClass) in pairs)
        {
            if (!WeaponScriptName.Candidates(serverClass).Contains(entityClass, StringComparer.Ordinal))
            {
                unresolved.Add(
                    $"{serverClass}: wanted {entityClass}, offered " +
                    string.Join("/", WeaponScriptName.Candidates(serverClass)));
            }
        }

        unresolved.ShouldBeEmpty(
            "these server classes do not offer the script name the SDK pairs them with, so their " +
            "weapon role cannot be read and the body keeps the primary suffix: " +
            string.Join("; ", unresolved));
    }

    [Test]
    public void WeaponScriptNames_ARegularName_NeedsNoException()
    {
        // **The half the exception list must not swallow.** If every pair were listed explicitly the
        // test above would pass while the rule did nothing, so this pins a few the rule has to get
        // on its own — one plain, one broken at a capital, one acronym.
        WeaponScriptName.Candidates("CTFBat").ShouldContain("tf_weapon_bat");
        WeaponScriptName.Candidates("CTFBreakableSign").ShouldContain("tf_weapon_breakable_sign");
        WeaponScriptName.Candidates("CTFDRGPomson").ShouldContain("tf_weapon_drg_pomson");
        WeaponScriptName.Candidates("CTFWeaponInvis").ShouldContain("tf_weapon_invis");
    }

    /// <summary>Every <c>LINK_ENTITY_TO_CLASS</c> pair naming a weapon.</summary>
    private static Dictionary<string, string> Pairs()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        Regex link = new(
            @"LINK_ENTITY_TO_CLASS\(\s*(tf_weapon[A-Za-z0-9_]*)\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*\)",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10));

        Dictionary<string, string> pairs = new(StringComparer.Ordinal);

        foreach (string file in SourceSdk.Files("src/game", "*.cpp", recursive: true))
        {
            foreach (Match match in link.Matches(File.ReadAllText(file)))
            {
                pairs[match.Groups[2].Value] = match.Groups[1].Value;
            }
        }

        return pairs;
    }
}
