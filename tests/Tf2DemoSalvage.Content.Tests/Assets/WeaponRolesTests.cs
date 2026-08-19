using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A weapon's activity suffix, read from the game's own encrypted weapon scripts.
/// </summary>
/// <remarks>
/// **The predictions are the slots the game puts these weapons in, and the code was not told them.**
/// The suffix comes from each script's <c>WeaponType</c> key, decrypted with the key Valve publishes
/// in <c>tf_shareddefs.cpp:1616</c>. A medigun reading as a secondary and a bonesaw as a melee is
/// the whole chain working at once: server class to script name, archive lookup, ICE decryption, key
/// scan, and the string mapping in <c>tf_weapon_parse.cpp:134</c>.
///
/// **A wrong answer here is silent**, which is why the negative control matters more than usual: a
/// weapon whose script cannot be found falls back to PRIMARY, and PRIMARY is also the correct answer
/// for most weapons — so "everything is primary" and "everything works" look identical unless
/// something that is NOT primary is checked.
/// </remarks>
public sealed class WeaponRolesTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    public void WeaponRoles_AWeapon_ReportsTheSlotItIsCarriedIn()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        (string Weapon, int? Class)[] classes =
        [
            ("CTFScatterGun", null), ("CTFRocketLauncher", null), ("CTFMinigun", null),
            ("CWeaponMedigun", null), ("CTFPistol", null), ("CTFShotgun", null),
            ("CTFBonesaw", null), ("CTFBat", null), ("CTFFireAxe", null),
            ("CTFWeaponPDA_Engineer_Build", null),
        ];

        WeaponRoles roles = WeaponRoles.Read(read, classes);

        // Primaries.
        roles.Suffix("CTFScatterGun").ShouldBe("PRIMARY");
        roles.Suffix("CTFRocketLauncher").ShouldBe("PRIMARY");
        roles.Suffix("CTFMinigun").ShouldBe("PRIMARY");

        // **The medigun, which is the case this whole chain exists for.** It is a secondary, and
        // every medic in every demo has been animated as though holding a primary.
        roles.Suffix("CWeaponMedigun").ShouldBe("SECONDARY");
        roles.Suffix("CTFPistol").ShouldBe("SECONDARY");

        // **The shotgun is not one weapon, and this expectation was wrong before it was measured.**
        // `pszWpnEntTranslationList` (tf_shareddefs.cpp:1628) translates a base weapon entity into a
        // per-class one: tf_weapon_shotgun becomes _soldier, _hwg or _pyro — all secondaries — and
        // _primary for the engineer, whose shotgun IS his primary. One server class, several
        // scripts, and the role differs between them.
        //
        // Asked without a holder, this is the base script's own answer. The per-class case is
        // below, and it is the one that matters on screen.
        roles.Suffix("CTFShotgun").ShouldBe("PRIMARY");

        // Melee.
        roles.Suffix("CTFBonesaw").ShouldBe("MELEE");
        roles.Suffix("CTFBat").ShouldBe("MELEE");
        roles.Suffix("CTFFireAxe").ShouldBe("MELEE");
    }

    [Test]
    public void WeaponRoles_TheShotgun_IsPrimaryOrSecondaryByHolder()
    {
        // **The case that proves a weapon's role is not a property of the weapon.**
        // pszWpnEntTranslationList (tf_shareddefs.cpp:1628) rewrites tf_weapon_shotgun into
        // _soldier, _hwg or _pyro before its script is read, and those are secondaries — while the
        // engineer's _primary is the one weapon of the four that really is a primary.
        //
        // Both directions are asserted, because a translation that fired for everybody would make
        // the engineer wrong in exactly the way this is meant to fix.
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        const int soldier = 3;
        const int heavy = 6;
        const int pyro = 7;
        const int engineer = 9;

        WeaponRoles roles = WeaponRoles.Read(
            read,
            [
                ("CTFShotgun", soldier), ("CTFShotgun", heavy),
                ("CTFShotgun", pyro), ("CTFShotgun", engineer),
            ]);

        roles.Suffix("CTFShotgun", soldier).ShouldBe("SECONDARY");
        roles.Suffix("CTFShotgun", heavy).ShouldBe("SECONDARY");
        roles.Suffix("CTFShotgun", pyro).ShouldBe("SECONDARY");

        roles.Suffix("CTFShotgun", engineer)
            .ShouldBe("PRIMARY", "the engineer's shotgun IS his primary");
    }

    [Test]
    public void WeaponRoles_AClassWithNoTranslation_KeepsTheBaseScript()
    {
        // The control for the test above: the table has an entry for the shotgun and empty slots
        // inside it, and an empty slot means "no translation" rather than "no weapon". A scout
        // cannot carry this shotgun at all, so the base script is the honest answer.
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        const int scout = 1;

        WeaponRoles roles = WeaponRoles.Read(read, [("CTFScatterGun", scout)]);

        roles.Suffix("CTFScatterGun", scout).ShouldBe("PRIMARY");
    }

    [Test]
    public void WeaponRoles_AnUnknownWeapon_FallsBackToPrimary()
    {
        // **Primary is the engine's default too, not a guess made here.** ActivityList's switch
        // gives TF_WPN_TYPE_PRIMARY the same body as `default:`, so a weapon whose script is
        // missing animates exactly as the engine animates one whose type it does not recognise.
        WeaponRoles roles = WeaponRoles.Read(_ => null, [("CTFNotAWeapon", (int?)null)]);

        roles.Suffix("CTFNotAWeapon").ShouldBe("PRIMARY");
        roles.Suffix(null).ShouldBe("PRIMARY", "an empty hand is the primary animation set");
    }

    /// <summary>Reads a file out of the installed game, or null when it is absent.</summary>
    private static Func<string, byte[]?>? Reader()
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        VpkArchive[] archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        return archives.Length == 0
            ? null
            : path => archives.Select(archive => archive.ReadFile(path)).FirstOrDefault(f => f is not null);
    }
}
