using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>Every weapon id the SDK declares names the alias the SDK gives it (B415).</summary>
/// <remarks>
/// **The denominator is the ENUM, not the array, and that is the stronger of the two.** `g_aWeaponNames`
/// (`tf_shareddefs.cpp:599`) is a list of strings whose meaning is entirely positional, and the engine's own
/// `COMPILE_TIME_ASSERT( TF_WEAPON_COUNT == ARRAYSIZE( g_aWeaponNames ) )` is what ties it to `ETFWeaponType`
/// (`tf_shareddefs.h:405`). Reading the enum gets both the names and the numbers Valve assigns them, so a
/// transcription that dropped or duplicated a row fails on the index rather than on the count alone.
///
/// **A wrong index here is silent.** `m_iWeaponID` picks which weapon script an explosion's particle effect comes
/// from, so being one out substitutes a real effect for another real effect — a rocket blast where a sticky went off.
/// There is no exception and nothing to log.
/// </remarks>
public sealed class TfWeaponAliasesConformanceTests
{
    /// <summary>`tf_shareddefs.h`, where `ETFWeaponType` is declared.</summary>
    private const string SharedDefs = "src/game/shared/tf/tf_shareddefs.h";

    /// <remarks>
    /// **Against `g_aWeaponNames` rather than against the enum, and the difference is not academic.**
    /// `WeaponIdToAlias` returns `g_aWeaponNames[iWeapon]` — the STRING — and that string is what
    /// `ReadWeaponDataFromFileForSlot` builds a path out of. The enum's member names are what the engine's own code
    /// says; they are not what it opens.
    /// </remarks>
    [Test]
    public void Of_EveryWeaponIdInTheSdk_NamesTheSdksOwnAlias()
    {
        List<string> aliases = Aliases();

        List<string> wrong = [];

        for (int id = 0; id < aliases.Count; id++)
        {
            if (!string.Equals(TfWeaponAliases.Of(id), aliases[id], StringComparison.Ordinal))
            {
                wrong.Add($"{id}: the SDK says {aliases[id]}, this says {TfWeaponAliases.Of(id) ?? "nothing"}");
            }
        }

        wrong.ShouldBeEmpty(
            "an id that names the wrong weapon reads the wrong script, and substitutes one real " +
            "explosion effect for another: " + string.Join("; ", wrong));
    }

    /// <remarks>
    /// The count as its own assertion, because the loop above cannot see a weapon this table has that the SDK does
    /// not — a transcription that appended a line would pass it.
    /// </remarks>
    [Test]
    public void All_TheTablesLength_IsTheSdksWeaponCount()
    {
        TfWeaponAliases.All.Count.ShouldBe(Declared().Count);
        TfWeaponAliases.All.Count.ShouldBe(Aliases().Count, "Valve's own COMPILE_TIME_ASSERT says these agree");
    }

    /// <remarks>
    /// **Valve's array and Valve's enum disagree at exactly one index, and the array wins.**
    /// `g_aWeaponNames[109]` is `TF_WEPON_FLAME_BALL`, missing the A, while `ETFWeaponType` spells
    /// `TF_WEAPON_FLAME_BALL` correctly — so the Dragon's Fury's script is looked up under the misspelling, and a
    /// table "corrected" to match the enum would find no file for it.
    ///
    /// **Pinned as a pair rather than as one value.** Asserting only the typo would not notice Valve fixing it; this
    /// fails if either side changes, and if they are ever reconciled the failure says which way.
    /// </remarks>
    [Test]
    public void All_WhereTheArrayAndTheEnumDisagree_FollowsTheArray()
    {
        List<string> aliases = Aliases();
        Dictionary<string, int> declared = Declared();

        List<string> apart = [];

        foreach ((string name, int id) in declared.OrderBy(one => one.Value))
        {
            if (id < aliases.Count && !string.Equals(aliases[id], name, StringComparison.Ordinal))
            {
                apart.Add($"{id}: enum {name}, array {aliases[id]}");
            }
        }

        apart.ShouldBe(["109: enum TF_WEAPON_FLAME_BALL, array TF_WEPON_FLAME_BALL"]);
    }

    /// <remarks>
    /// **`TF_WEAPON_COUNT` is the enum's terminator and not a weapon**, so <c>Of</c> must answer null for it.
    /// It is also the id a demo from a newer build would carry, which is the case that decides between "null" and
    /// "some other weapon's script".
    /// </remarks>
    [Test]
    public void Of_AnIdPastTheTable_IsNull()
    {
        TfWeaponAliases.Of(TfWeaponAliases.All.Count).ShouldBeNull();
        TfWeaponAliases.Of(-1).ShouldBeNull();
        TfWeaponAliases.Of(int.MinValue).ShouldBeNull();
    }

    /// <remarks>
    /// **`TFExplosionCallback`'s four remapped ids** (`tf_fx_explosions.cpp:45-59`). A demoman's stickies are the
    /// commonest explosion in a match and `TF_WEAPON_GRENADE_DEMOMAN` has no script of its own, so without the remap
    /// thousands of blasts per match fall to the bare `ExplosionCore_wall` default.
    /// </remarks>
    [Test]
    public void ForExplosion_TheThingsThatExplode_ReadTheirLaunchersScript()
    {
        Dictionary<string, int> declared = Declared();

        TfWeaponAliases.ForExplosion(declared["TF_WEAPON_GRENADE_PIPEBOMB"])
            .ShouldBe("TF_WEAPON_PIPEBOMBLAUNCHER");
        TfWeaponAliases.ForExplosion(declared["TF_WEAPON_GRENADE_DEMOMAN"])
            .ShouldBe("TF_WEAPON_PIPEBOMBLAUNCHER");
        TfWeaponAliases.ForExplosion(declared["TF_WEAPON_PUMPKIN_BOMB"])
            .ShouldBe("TF_WEAPON_PIPEBOMBLAUNCHER");
        TfWeaponAliases.ForExplosion(declared["TF_WEAPON_FLAMETHROWER_ROCKET"])
            .ShouldBe("TF_WEAPON_FLAMETHROWER");
    }

    /// <remarks>
    /// The control for the test above: a remap that fired for everything would pass it. A rocket reads its own
    /// launcher's script, unremapped.
    /// </remarks>
    [Test]
    public void ForExplosion_AWeaponWithItsOwnScript_IsNotRemapped()
    {
        TfWeaponAliases.ForExplosion(Declared()["TF_WEAPON_ROCKETLAUNCHER"])
            .ShouldBe("TF_WEAPON_ROCKETLAUNCHER");
    }

    /// <summary>`ETFWeaponType`'s members with the numbers C gives them, minus its terminator.</summary>
    private static Dictionary<string, int> Declared()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        Dictionary<string, int> declared =
            new(SourceSdk.Enumerators(SharedDefs, "ETFWeaponType"), StringComparer.Ordinal);

        // The control: an enum this could not find would make every assertion above vacuous.
        declared.Count.ShouldBeGreaterThan(
            100, "ETFWeaponType declares over a hundred weapons plus its count");

        declared.Remove("TF_WEAPON_COUNT");

        return declared;
    }

    /// <summary>`g_aWeaponNames`' strings in order — what <c>WeaponIdToAlias</c> actually hands back.</summary>
    private static List<string> Aliases()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }

        string text = SourceSdk.Text("src/game/shared/tf/tf_shareddefs.cpp") ?? string.Empty;

        Match body = Regex.Match(
            text,
            @"const char \*g_aWeaponNames\[\]\s*=\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(10));

        body.Success.ShouldBeTrue("g_aWeaponNames could not be found, so this test is about itself");

        List<string> aliases =
        [
            .. Regex.Matches(body.Groups["body"].Value, "\"(TF_WE[A-Z_0-9]*)\"", RegexOptions.None,
                    TimeSpan.FromSeconds(10))
                .Select(one => one.Groups[1].Value),
        ];

        // The control, for the same reason the enum has one.
        aliases.Count.ShouldBeGreaterThan(100, "the array holds over a hundred weapon names");

        return aliases;
    }
}
