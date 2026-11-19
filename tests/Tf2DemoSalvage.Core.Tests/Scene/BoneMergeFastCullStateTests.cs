using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Reading <c>EF_BONEMERGE_FASTCULL</c> (0x080, <c>const.h:291</c>) off <c>m_fEffects</c> — the wire's half of whether a
/// bone-merged entity stands at its parent's centre (B69). The class half is <c>SchemaClasses.BoneMergesItself</c>.
/// </summary>
public sealed class BoneMergeFastCullStateTests
{
    [Test]
    public void IsBoneMergeFastCull_EffectsCarryingTheBit_IsTrue()
    {
        Entity(effects: 0x001 | 0x080).IsBoneMergeFastCull.ShouldBeTrue();
    }

    /// <summary>The control: <c>EF_BONEMERGE</c> and every other bit without 0x080 is not it.</summary>
    [Test]
    public void IsBoneMergeFastCull_EffectsWithoutTheBit_IsFalse()
    {
        Entity(effects: 0x001 | 0x020 | 0x100).IsBoneMergeFastCull.ShouldBeFalse();
    }

    /// <summary>
    /// **Through the timeline to the prop the scene draws**: the wire's bit reaches <c>SceneProp.BoneMergeFastCull</c>
    /// at the tick it was stated, and a later update without it takes it away.
    /// </summary>
    [Test]
    public void PropsAt_AnEntityStatingFastCull_CarriesItOntoTheProp()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEffects(
            1f / 66.67f, (100, 0x001 | 0x080), (200, 0x001)));

        Prop(timeline, tick: 150).BoneMergeFastCull.ShouldBeTrue();
        Prop(timeline, tick: 250).BoneMergeFastCull.ShouldBeFalse("the control: the bit was taken away");
    }

    /// <summary>
    /// **The class half**: a combat weapon that sends no <c>m_fEffects</c> still fast-culls, because
    /// <c>CBaseCombatWeapon::Spawn</c> adds the bit on the client (<c>basecombatweapon_shared.cpp:229</c>).
    /// </summary>
    [Test]
    public void BoneMergeFastCull_AWeaponSendingNoEffects_IsSetByItsClass()
    {
        const int Owner = 2;
        const int Weapon = 50;

        // `EF_BONEMERGE` alone on the wire, so it hangs off its owner and only the class can say fast-cull.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticEffects.Demo(
            [
                new SyntheticEffects.Placed(Owner, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, team: 2)),
                new SyntheticEffects.Placed(Weapon, SyntheticEffects.WeaponClass, new Dictionary<string, PropertyValue>(System.StringComparer.Ordinal)
                {
                    ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(13),
                    ["m_fEffects"] = PropertyValue.FromInt(0x001),
                    // A handle is slot and serial; the fixture gives each entity its slot as its serial.
                    ["m_hOwnerEntity"] = PropertyValue.FromInt(Owner | (Owner << 11)),
                }),
            ],
            [],
            [],
            []));

        ScenePropTrack weapon = timeline.Props.ShouldHaveSingleItem();
        weapon.EntityIndex.ShouldBe(Weapon, "the control: the weapon is the track");
        weapon.BoneMergeFastCull.ShouldBeTrue();
        weapon.FactsAt(SyntheticEffects.PlayersTick).BoneMergeFastCull.ShouldBeTrue();
    }

    /// <summary>
    /// **A receive of <c>m_fEffects</c>, not a change of it** (B69 h): the server's flags written back restore a fast-cull
    /// bit the client removed, so the same value sent again dates the prop as surely as a new one; an update without it
    /// does not. The creation is the first, as the client's <c>Spawn</c> sets the bit then.
    /// </summary>
    [Test]
    public void EffectsReceived_AnUpdateResendingTheSameEffects_IsDatedAndOneWithoutThemIsNot()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfEffects(
            1f / 66.67f, (100, 0x081), (200, null), (300, 0x081)));

        Prop(timeline, tick: 150).EffectsReceived.ShouldBe(100);
        Prop(timeline, tick: 250).EffectsReceived.ShouldBe(100, "the control: an update without m_fEffects");
        Prop(timeline, tick: 350).EffectsReceived.ShouldBe(300);
    }

    /// <summary>
    /// **An item's every update is dated** (B69: a weapon re-decides its particle systems on each, `tf_weaponbase.cpp:3525`);
    /// anything else's is not, so its facts keep only changes.
    /// </summary>
    [Test]
    public void Received_AnItemAndAPlainProp_DatesOnlyTheItem()
    {
        DemoTimeline plain = DemoTimeline.Build(SyntheticPlayer.DemoOfEffects(1f / 66.67f, (100, 0x081), (200, null)));

        Prop(plain, tick: 250).Received.ShouldBeNull("the control: not an item");

        const int Owner = 2;
        const int Weapon = 50;

        DemoTimeline item = DemoTimeline.Build(SyntheticEffects.Demo(
            [
                new SyntheticEffects.Placed(Owner, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, team: 2)),
                new SyntheticEffects.Placed(Weapon, SyntheticEffects.WeaponClass, new Dictionary<string, PropertyValue>(System.StringComparer.Ordinal)
                {
                    ["m_iItemDefinitionIndex"] = PropertyValue.FromInt(13),
                    ["m_fEffects"] = PropertyValue.FromInt(0x001),
                    ["m_hOwnerEntity"] = PropertyValue.FromInt(Owner | (Owner << 11)),
                }),
            ],
            [],
            [],
            []));

        item.Props.ShouldHaveSingleItem().FactsAt(SyntheticEffects.PlayersTick).Received.ShouldBe(SyntheticEffects.PlayersTick);
    }

    private static SceneProp Prop(DemoTimeline timeline, int tick)
    {
        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        return props.ShouldHaveSingleItem();
    }

    private static EntityState Entity(int effects)
    {
        EntityState state = new(entityIndex: 4, classId: 1, serialNumber: 1, className: "CTFRocketLauncher");

        state.Set("DT_BaseEntity.m_fEffects", PropertyValue.FromInt(effects));

        return state;
    }
}
