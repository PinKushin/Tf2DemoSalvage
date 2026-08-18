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
    public void WeaponsReportTheSlotTheyAreCarriedIn()
    {
        if (Reader() is not { } read)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        string[] classes =
        [
            "CTFScatterGun", "CTFRocketLauncher", "CTFMinigun",
            "CWeaponMedigun", "CTFPistol", "CTFShotgun",
            "CTFBonesaw", "CTFBat", "CTFFireAxe",
            "CTFWeaponPDA_Engineer_Build",
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
        // Asserted as the base script's own answer, which is what this layer can currently see. The
        // translation needs the holder's class and is recorded as the gap it is; without it an
        // engineer's shotgun is right and a soldier's reads primary instead of secondary.
        roles.Suffix("CTFShotgun").ShouldBe("PRIMARY");

        // Melee.
        roles.Suffix("CTFBonesaw").ShouldBe("MELEE");
        roles.Suffix("CTFBat").ShouldBe("MELEE");
        roles.Suffix("CTFFireAxe").ShouldBe("MELEE");
    }

    [Test]
    public void SomethingUnknownFallsBackToPrimary()
    {
        // **Primary is the engine's default too, not a guess made here.** ActivityList's switch
        // gives TF_WPN_TYPE_PRIMARY the same body as `default:`, so a weapon whose script is
        // missing animates exactly as the engine animates one whose type it does not recognise.
        WeaponRoles roles = WeaponRoles.Read(_ => null, ["CTFNotAWeapon"]);

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
