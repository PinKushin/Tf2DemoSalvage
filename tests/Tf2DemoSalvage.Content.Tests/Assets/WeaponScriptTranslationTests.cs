using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>WeaponScriptName.Translate</c> and <c>Candidates</c> — the per-class half.
/// </summary>
/// <remarks>
/// **<c>WeaponScriptNameTests</c> checks the MAPPING against the SDK; this checks the LOGIC around
/// it, and nothing did before.** 269 lines carried two tests, both of them about whether the
/// class-to-script table agrees with <c>LINK_ENTITY_TO_CLASS</c>. The selection rules — per-class
/// translation, candidate ordering, the fallbacks — had no test at all, and they are pure functions
/// needing no game install, so they belong in the synthetic set (`docs/MEASUREMENT-PLAN.md`).
///
/// **Why it matters that this is tested at all: a wrong answer here is invisible.** A script name
/// that resolves to no file leaves the weapon with no role, and the animation keeps the primary
/// suffix it would have had anyway. Correct and broken look identical for the majority of weapons,
/// which are primaries — the same reason the mapping itself is enumerated rather than guessed.
///
/// Class indices are <c>TF_CLASS_*</c>: 0 is undefined, 1 scout, 2 sniper, 3 soldier, 4 demoman,
/// 5 medic, 6 heavy, 7 pyro, 8 spy, 9 engineer.
/// </remarks>
public sealed class WeaponScriptTranslationTests
{
    private const int Scout = 1;
    private const int Soldier = 3;
    private const int Demoman = 4;
    private const int Heavy = 6;
    private const int Engineer = 9;

    [Test]
    public void AWeaponWithNoTranslationForThisClassKeepsItsOwnName()
    {
        // The shotgun table has an empty entry for the scout, and an empty entry means "this class
        // has no translation", not "this class has no weapon". Returning the empty string would
        // resolve to no file and silently cost the weapon its role.
        WeaponScriptName.Translate("tf_weapon_shotgun", Scout).ShouldBe("tf_weapon_shotgun");
    }

    [Test]
    public void AWeaponTranslatesPerClass()
    {
        // The same entity name becomes three different scripts in three different hands. This is
        // the whole reason the table exists, so it is asserted across classes rather than once.
        WeaponScriptName.Translate("tf_weapon_shotgun", Soldier).ShouldBe("tf_weapon_shotgun_soldier");
        WeaponScriptName.Translate("tf_weapon_shotgun", Heavy).ShouldBe("tf_weapon_shotgun_hwg");
        WeaponScriptName.Translate("tf_weapon_shotgun", Engineer).ShouldBe("tf_weapon_shotgun_primary");
    }

    [Test]
    public void TheSameWeaponInTwoHandsCanSwapIdentityEntirely()
    {
        // A soldier's bottle is a shovel and a demoman's shovel is a bottle - the table maps both
        // names in both directions. A translation that merely appended a suffix would pass every
        // other case here and fail this one.
        WeaponScriptName.Translate("tf_weapon_bottle", Soldier).ShouldBe("tf_weapon_shovel");
        WeaponScriptName.Translate("tf_weapon_shovel", Demoman).ShouldBe("tf_weapon_bottle");
    }

    [Test]
    public void AnUnknownPlayerClassLeavesTheNameAlone()
    {
        // The demo does not always say who is holding a weapon, and null is that case rather than
        // an error. Defaulting to class 0 instead would index a real slot in the table.
        WeaponScriptName.Translate("tf_weapon_shotgun", playerClass: null)
            .ShouldBe("tf_weapon_shotgun");
    }

    [Test]
    public void AnOutOfRangeClassLeavesTheNameAloneRatherThanThrowing()
    {
        // Both ends, because a bounds check is where an off-by-one lives and a decoder should not
        // throw on a value a malformed demo can carry.
        WeaponScriptName.Translate("tf_weapon_shotgun", -1).ShouldBe("tf_weapon_shotgun");
        WeaponScriptName.Translate("tf_weapon_shotgun", 99).ShouldBe("tf_weapon_shotgun");
    }

    [Test]
    public void AWeaponWithNoTableAtAllIsUntouched()
    {
        WeaponScriptName.Translate("tf_weapon_rocketlauncher", Soldier)
            .ShouldBe("tf_weapon_rocketlauncher");
    }

    [Test]
    public void TheClassTranslationIsOfferedBeforeTheBaseName()
    {
        // "The translation goes in FRONT rather than replacing the list" - a translation naming a
        // script the install does not ship still leaves the base one to answer. Order is the
        // assertion: both being present says nothing about which is tried first.
        IReadOnlyList<string> candidates = WeaponScriptName.Candidates("CTFShotgun", Soldier);

        candidates[0].ShouldBe("tf_weapon_shotgun_soldier");
        candidates.ShouldContain("tf_weapon_shotgun");
    }

    [Test]
    public void AnIrregularClassResolvesToTheNameValveChose()
    {
        // No naming rule produces these, which is why they are enumerated. A rule-only
        // implementation would return tf_weapon_syringe_gun and find nothing.
        WeaponScriptName.Candidates("CTFSyringeGun")[0].ShouldBe("tf_weapon_syringegun_medic");
        WeaponScriptName.Candidates("CTFSniperRifleClassic")[0].ShouldBe("tf_weapon_sniperrifle_classic");
    }

    [Test]
    public void CandidatesNeverRepeatThemselves()
    {
        // When a class's translation IS the base name, the two paths produce the same string. A
        // duplicate costs a wasted archive lookup and, more to the point, means the de-duplication
        // that is written is not running.
        IReadOnlyList<string> candidates = WeaponScriptName.Candidates("CTFRevolver", Engineer);

        candidates.ShouldBeUnique();
    }

    [Test]
    public void ARegularClassNameIsBrokenAtItsCapitals()
    {
        // The rule half, which covers most weapons: strip the prefix, lowercase, and split at each
        // capital. A two-word name is the case that distinguishes it from plain lowercasing.
        WeaponScriptName.Candidates("CTFBat").ShouldContain("tf_weapon_bat");
        WeaponScriptName.Candidates("CTFBreakableSign").ShouldContain("tf_weapon_breakable_sign");
    }
}
