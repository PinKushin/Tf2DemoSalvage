using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A canteen's skin: `CTFPowerupBottle::GetSkin` (B523).
/// </summary>
/// <remarks>
/// *Read from published source*, `tf_item_powerup_bottle.cpp:677-685` and `tf_item_powerup_bottle.h:68`:
///
/// <code>
/// bool IsBasePowerUpBottle() const { int iMode = 0; CALL_ATTRIB_HOOK_INT( iMode, set_weapon_mode ); return ( iMode == 1 ); }
///
/// int CTFPowerupBottle::GetSkin()
/// {
///     if ( !IsBasePowerUpBottle() )
///         return ( ( GetNumCharges() > 0 ) ? 1 : 0 );
///     return BaseClass::GetSkin();
/// }
/// </code>
///
/// So a cosmetic canteen's two skins are EMPTY and FILLED, not RED and BLU, and outside Mann vs. Machine it holds no
/// charge: the Battery Canteens on a BLU demoman's hip are skin 0, the grey ones. This viewer drew the entity's own
/// `m_nSkin`, which the server sets to the team — 1 on BLU, the filled yellow-green — as it rightly does for every
/// other wearable.
/// </remarks>
public sealed class PowerupBottleConformanceTests
{
    private const string Bottle = "CTFPowerupBottle";

    [TestCase(1, 0, 0, 0, TestName = "Skin_ACosmeticCanteenWithNoCharge_IsTheEmptySkinWhateverTheTeam")]
    [TestCase(0, 0, 3, 1, TestName = "Skin_ACosmeticCanteenHoldingCharges_IsTheFilledSkin")]
    [TestCase(1, 1, 0, 1, TestName = "Skin_TheBasePowerUpCanteen_KeepsTheEntitysOwnSkin")]
    [TestCase(0, 1, 2, 0, TestName = "Skin_TheBasePowerUpCanteenWithCharges_StillKeepsTheEntitysOwnSkin")]
    public void Skin_ByModeAndCharges_IsGetSkins(int wire, int weaponMode, int charges, int expected)
    {
        PowerupBottle.Skin(wire, weaponMode, charges).ShouldBe(expected);
    }

    /// <remarks>
    /// **Through the draw list, which is the wiring**: the bottle is found by its class, its charges on its wearer's
    /// item, and its mode through the attribute hook. The control is the hat beside it, which keeps its team skin.
    /// </remarks>
    [Test]
    public void Apply_ToADrawList_ReskinsTheCanteenAndLeavesTheHat()
    {
        List<SceneProp> props =
        [
            Worn(535, Bottle, wearer: 3),
            Worn(523, "CTFWearable", wearer: 3),
        ];

        ScenePlayer gummo = new(3, 0f, 0f, 0f, Team: 3, Health: 175, PlayerClass: 4)
        {
            Items = [new SceneItem(535, Bottle, 30015, new EconAttributeWire([], [], false), IsWeapon: false)],
        };

        PowerupBottle.Apply(props, [gummo], (_, _, attribute, value) => attribute == "set_weapon_mode" ? 0f : value);

        props[0].Pose.Skin.ShouldBe(0);
        props[1].Pose.Skin.ShouldBe(1);
    }

    /// <remarks>A bottle whose wearer this moment does not hold is left as the demo stated it: nothing is known about it.</remarks>
    [Test]
    public void Apply_ToACanteenNobodyHereWears_LeavesItsSkin()
    {
        List<SceneProp> props = [Worn(535, Bottle, wearer: 9)];

        PowerupBottle.Apply(props, [], (_, _, _, value) => value);

        props[0].Pose.Skin.ShouldBe(1);
    }

    private static SceneProp Worn(int entity, string className, int wearer) =>
        new(entity, "models/x.mdl", SceneModelKind.Studio, new ScenePose { Skin = 1 })
        {
            ClassName = className,
            AttachedTo = wearer,
        };
}
