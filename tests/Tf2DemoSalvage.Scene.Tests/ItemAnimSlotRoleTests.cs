using System;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The table the ENGINE animates a named item with, in the class's hands that carry it — off the shipped game (B105).
/// </summary>
/// <remarks>
/// **Each expectation was read by hand out of the installed `items_game.txt` and switched through `ActivityList`**
/// (`tf_weaponbase.cpp:4226-4276`), not produced by the code under test:
///
/// <code>
///   "130"  Scottish Resistance  "anim_slot" "primary"                  -> PRIMARY
///   "1150" Quickiebomb          "anim_slot" "primary"                  -> PRIMARY
///   "20"   stickybomb launcher  "prefab" "weapon_stickybomb_launcher"  -> "anim_slot" "primary"   -> PRIMARY
///   "19"   grenade launcher     "prefab" "weapon_grenade_launcher"     -> "anim_slot" "secondary" -> SECONDARY
///   "226"  Battalion's Backup   "anim_slot" "melee"                    -> MELEE
///   "354"  Concheror            "anim_slot" "melee"                    -> MELEE
///   "142"  Gunslinger           "anim_slot" "item2"                    -> ITEM2
///   "1178" Dragon's Fury        "anim_slot" "primary"                  -> PRIMARY
///   "264"  Frying Pan           "anim_slot" "MELEE_ALLCLASS"           -> MELEEALLCLASS
///   "132"  Eyelander            "prefab" "valve weapon_eyelander"      -> weapon_sword "item1" -> ITEM1
///   "357"  Half-Zatoichi        "anim_slot" "" over weapon_sword       -> -1, then CTFKatana for a demoman
///   "638"  Sharp Dresser        "anim_slot" "ITEM1"                    -> ITEM1
///   "45"   Force-a-Nature       weapon_force_a_nature "item2"          -> ITEM2
///   "441"  Cow Mangler 5000     "anim_slot" "primary2"                 -> PRIMARY2
/// </code>
///
/// **Two controls per row, and they answer different questions.** The stock weapon of the same slot says the test can
/// tell items apart — a Gunslinger's ITEM2 against the wrench's MELEE. The same weapon with NO item says the answer
/// came from the item rather than the script — which matters most for the demoman, whose STOCK launchers are
/// themselves overridden: his stickybomb launcher's script says secondary and the item says primary, so the stock
/// control alone could not see the override at all.
///
/// **The activity is asserted beside the role**, because the role is only a table's name and what a model is asked
/// for is the row: `ACT_MP_RUN_PRIMARY` for a Scottish Resistance, `ACT_MP_RUN_MELEE_ALLCLASS` for a Frying Pan.
/// </remarks>
public sealed class ItemAnimSlotRoleTests
{
    private const int Scout = 1;
    private const int Soldier = 3;
    private const int Demoman = 4;
    private const int Pyro = 7;
    private const int Spy = 8;
    private const int Engineer = 9;

    /// <summary>Every (weapon, holder) any row asks about, read once: each costs an ICE decryption.</summary>
    private static readonly (string Weapon, int? Class)[] Held =
    [
        ("CTFPipebombLauncher", Demoman), ("CTFGrenadeLauncher", Demoman), ("CTFBuffItem", Soldier),
        ("CTFShotgun_Soldier", Soldier), ("CTFRobotArm", Engineer), ("CTFWrench", Engineer),
        ("CTFWeaponFlameBall", Pyro), ("CTFFlameThrower", Pyro), ("CTFShovel", Soldier), ("CTFKatana", Demoman),
        ("CTFKatana", Soldier), ("CTFSword", Demoman), ("CTFBottle", Demoman), ("CTFKnife", Spy),
        ("CTFScatterGun", Scout), ("CTFParticleCannon", Soldier), ("CTFRocketLauncher", Soldier),
    ];

    /// <summary>The installed game's schema and scripts, as the viewer opens them.</summary>
    private static readonly Lazy<GameAppearance> Installed = new(() =>
    {
        GameContent game = GameContent.Open(GameInstall.Root, NullLoggerFactory.Instance);

        return new GameAppearance(
            Classes: null, Roles: WeaponRoles.Read(game.Archives.Read, Held), Items: game.Weapons.Items);
    });

    [TestCase("CTFPipebombLauncher", Demoman, 130, "PRIMARY", "ACT_MP_RUN_PRIMARY")]
    [TestCase("CTFPipebombLauncher", Demoman, 1150, "PRIMARY", "ACT_MP_RUN_PRIMARY")]
    [TestCase("CTFBuffItem", Soldier, 226, "MELEE", "ACT_MP_RUN_MELEE")]
    [TestCase("CTFBuffItem", Soldier, 354, "MELEE", "ACT_MP_RUN_MELEE")]
    [TestCase("CTFRobotArm", Engineer, 142, "ITEM2", "ACT_MP_RUN_ITEM2")]
    [TestCase("CTFWeaponFlameBall", Pyro, 1178, "PRIMARY", "ACT_MP_RUN_PRIMARY")]
    [TestCase("CTFGrenadeLauncher", Demoman, 19, "SECONDARY", "ACT_MP_RUN_SECONDARY")]
    [TestCase("CTFShovel", Soldier, 264, "MELEEALLCLASS", "ACT_MP_RUN_MELEE_ALLCLASS")]
    [TestCase("CTFSword", Demoman, 132, "ITEM1", "ACT_MP_RUN_ITEM1")]
    [TestCase("CTFKatana", Demoman, 357, "ITEM1", "ACT_MP_RUN_ITEM1")]
    [TestCase("CTFKatana", Soldier, 357, "MELEE", "ACT_MP_RUN_MELEE")]
    [TestCase("CTFKnife", Spy, 638, "ITEM1", "ACT_MP_RUN_ITEM1")]
    [TestCase("CTFScatterGun", Scout, 45, "ITEM2", "ACT_MP_RUN_ITEM2")]
    [TestCase("CTFParticleCannon", Soldier, 441, "PRIMARY2", "ACT_MP_RUN_PRIMARY")]
    public void WeaponSuffix_ANamedItemInItsClassesHands_IsTheTableTheEnginePicks(
        string weapon, int holder, int item, string role, string run)
    {
        string chosen = Appearance().WeaponSuffix(weapon, holder, item).ShouldNotBeNull();

        chosen.ShouldBe(role);
        PlayerAnimation.Translate(PlayerActivity.Run, chosen).ShouldBe(run);
    }

    /// <remarks>The stock weapon of the same slot, so a row above cannot pass by answering every item alike.</remarks>
    [TestCase("CTFPipebombLauncher", Demoman, 20, "PRIMARY")]
    [TestCase("CTFShotgun_Soldier", Soldier, 10, "SECONDARY")]
    [TestCase("CTFWrench", Engineer, 7, "MELEE")]
    [TestCase("CTFFlameThrower", Pyro, 21, "PRIMARY")]
    [TestCase("CTFShovel", Soldier, 6, "MELEE")]
    [TestCase("CTFBottle", Demoman, 1, "MELEE")]
    [TestCase("CTFKnife", Spy, 4, "MELEE")]
    [TestCase("CTFScatterGun", Scout, 13, "PRIMARY")]
    [TestCase("CTFRocketLauncher", Soldier, 18, "PRIMARY")]
    public void WeaponSuffix_TheStockWeaponOfTheSameSlot_IsItsOwnTable(string weapon, int holder, int item, string role)
    {
        Appearance().WeaponSuffix(weapon, holder, item).ShouldBe(role);
    }

    /// <remarks>
    /// **No item at all is the script's own answer** — `GetAnimationSlot` returns -1 when the view has no static data
    /// (`econ_item_view.cpp:1095-1096`) — so these are what every row above would read if the item were dropped on the
    /// way. The demoman's two launchers are the rows that show it: each is the other's table without its item.
    /// </remarks>
    [TestCase("CTFPipebombLauncher", Demoman, "SECONDARY")]
    [TestCase("CTFGrenadeLauncher", Demoman, "PRIMARY")]
    [TestCase("CTFShovel", Soldier, "MELEE")]
    [TestCase("CTFKnife", Spy, "MELEE")]
    [TestCase("CTFScatterGun", Scout, "PRIMARY")]
    public void WeaponSuffix_TheSameWeaponWithNoItem_IsItsScriptsTable(string weapon, int holder, string role)
    {
        Appearance().WeaponSuffix(weapon, holder, null).ShouldBe(role);
    }

    /// <summary>The installed game's appearance, or skips when there is no install.</summary>
    private static GameAppearance Appearance()
    {
        _ = GameInstall.Require();

        return Installed.Value;
    }
}
