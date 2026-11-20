using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="EntityState"/>'s weapon and viewmodel accessors, and the tables they read (B335).
/// </summary>
/// <remarks>
/// **Each of these is a table name plus a property name, and getting the pair wrong is a SILENT no
/// match** — not a near miss. `docs/memory/a-property-name-needs-its-declaring-table.md` records
/// the case: `m_iTeamNum` is on `DT_BaseEntity` and a fixture that put it on `DT_TFPlayer` produced
/// a player with a null team and no error anywhere.
///
/// **Three tables are in play and they are genuinely different entities**: a weapon in the world
/// (`DT_BaseCombatWeapon`), the viewmodel a first-person view holds (`DT_BaseViewModel`, which is
/// `BEGIN_NETWORK_TABLE_NOBASE` and so inherits nothing), and the base entity everything else
/// derives from. An accessor reading the wrong one answers null for every demo ever recorded, which
/// looks exactly like a field the format does not carry.
/// </remarks>
public sealed class EntityWeaponStateTests
{
    private const string WeaponTable = "DT_BaseCombatWeapon";
    private const string ViewModel = "DT_BaseViewModel";
    private const string BaseEntity = "DT_BaseEntity";

    /// <remarks>
    /// **The three states are a weapon's whole lifecycle**, and only one of them draws in the
    /// world: `WeaponNotCarried` is on the ground, `WeaponCarried` is in a player's inventory but
    /// holstered, and `WeaponActive` is in their hands.
    /// </remarks>
    [Test]
    public void WeaponState_EachOfTheThreeStates_IsReadFromTheWeaponTable()
    {
        Weapon(EntityState.WeaponNotCarried).WeaponState().ShouldBe(EntityState.WeaponNotCarried);
        Weapon(EntityState.WeaponCarried).WeaponState().ShouldBe(EntityState.WeaponCarried);
        Weapon(EntityState.WeaponActive).WeaponState().ShouldBe(EntityState.WeaponActive);
    }

    /// <remarks>
    /// **Zero is a REAL state and absent is not.** `WeaponNotCarried` is 0, so an accessor
    /// returning 0 for "never sent" would report every entity in the demo as a weapon lying on the
    /// ground.
    /// </remarks>
    [Test]
    public void WeaponState_AnEntityThatIsNotAWeapon_IsNullRatherThanNotCarried()
    {
        new EntityState(1, 0, 0, "CTFPlayer").WeaponState().ShouldBeNull();

        EntityState.WeaponNotCarried.ShouldBe(
            0, "which is why null and zero have to stay distinguishable");
    }

    /// <remarks>
    /// **The world model is a SEPARATE index from the entity's own** — a weapon carries the model
    /// it shows in someone's hands as `m_nModelIndex` and the one it shows on the ground as
    /// `m_iWorldModelIndex`, and `docs/memory/a-player-has-two-viewmodels.md` is about how easily
    /// the two are conflated.
    /// </remarks>
    [Test]
    public void WorldModelIndex_AWeaponCarryingBoth_KeepsThemApart()
    {
        EntityState weapon = new(1, 0, 0, "CTFWeaponScattergun");

        weapon.Set($"{BaseEntity}.m_nModelIndex", PropertyValue.FromInt(11));
        weapon.Set($"{WeaponTable}.m_iWorldModelIndex", PropertyValue.FromInt(22));

        weapon.ModelIndex().ShouldBe(11);
        weapon.WorldModelIndex().ShouldBe(22);
    }

    /// <remarks>
    /// The control: an entity carrying only the base index has no world model, rather than
    /// borrowing its own.
    /// </remarks>
    [Test]
    public void WorldModelIndex_AnEntityWithOnlyABaseIndex_IsNull()
    {
        EntityState prop = new(1, 0, 0, "CDynamicProp");

        prop.Set($"{BaseEntity}.m_nModelIndex", PropertyValue.FromInt(11));

        prop.WorldModelIndex().ShouldBeNull();
    }

    /// <remarks>
    /// **A handle is not a slot**, and this is where the two meet: `m_hOwner` carries an entity
    /// index in its low 11 bits and a serial number above them, so reading it whole gives a number
    /// in the millions. The decode is shared with every other handle and is covered by
    /// `DecodeInvariantTests`; what this adds is that the viewmodel's owner goes THROUGH it.
    /// </remarks>
    [Test]
    public void ViewmodelOwner_AHandleCarryingASerial_IsTheSlotAlone()
    {
        EntityState viewmodel = new(1, 0, 0, "CTFViewModel");

        // Slot 3, serial 9 — the serial occupies the bits above the edict, and must not survive.
        viewmodel.Set($"{ViewModel}.m_hOwner", PropertyValue.FromInt(3 | (9 << 11)));

        viewmodel.ViewmodelOwner().ShouldBe(3);
    }

    /// <remarks>
    /// **The invalid handle is all ones and means nobody**, which is a different answer from slot
    /// zero — slot 0 is the worldspawn and a viewmodel owned by it would be a real, wrong claim.
    /// </remarks>
    [Test]
    public void ViewmodelOwner_TheInvalidHandle_IsNullRatherThanSlotZero()
    {
        EntityState viewmodel = new(1, 0, 0, "CTFViewModel");

        viewmodel.Set($"{ViewModel}.m_hOwner", PropertyValue.FromInt(EntityState.NoHandle));

        viewmodel.ViewmodelOwner().ShouldBeNull();
    }

    /// <remarks>
    /// **A wearable a weapon controls names it, slot AND serial** (B770): `m_hWeaponAssociatedWith` is a
    /// `CNetworkHandle` on `DT_TFWearable` (`tf_item_wearable.cpp:37`, `tf_item_wearable.h:84`), set by
    /// `CTFWeaponBase::UpdateExtraWearables` (`tf_weaponbase.cpp:993`, `:1021`). The serial is kept so the scene can
    /// dereference it as `GetWeaponAssociatedWith` does; the invalid handle is no weapon, and an untied wearable sends none.
    /// </remarks>
    [TestCase(3 | (9 << 11), 3, 9, TestName = "WeaponAssociatedWith_ATiedWearable_IsTheWeaponsSlotAndSerial")]
    [TestCase(-1, null, null, TestName = "WeaponAssociatedWith_TheInvalidHandle_IsNoWeapon")]
    [TestCase(null, null, null, TestName = "WeaponAssociatedWith_AWearableSendingNone_IsNoWeapon")]
    public void WeaponAssociatedWith_AWearablesHandle_IsDereferenceable(int? handle, int? slot, int? serial)
    {
        EntityState wearable = new(1, 0, 0, "CTFWearable");

        if (handle is { } sent)
        {
            wearable.Set("DT_TFWearable.m_hWeaponAssociatedWith", PropertyValue.FromInt(sent < 0 ? EntityState.NoHandle : sent));
        }

        wearable.WeaponAssociatedWith().ShouldBe(slot is { } s && serial is { } n ? (s, n) : null);
    }

    /// <remarks>
    /// **A weapon names its extra wearables, slot AND serial** (B69): <c>m_hExtraWearable</c> and
    /// <c>m_hExtraWearableViewModel</c>, <c>CNetworkHandle</c>s on <c>DT_TFWeaponBase</c> (<c>tf_weaponbase.h:757-758</c>),
    /// which <c>UpdateSingleParticleSystem</c> hangs the weapon's effects from (<c>econ_entity.cpp:1611-1624</c>). Measured on
    /// the wire: a Concheror enters with a live <c>m_hExtraWearable</c> and the invalid view-model handle (entity-census,
    /// demostf-cp_steel_f12-1491261).
    /// </remarks>
    [TestCase(3 | (9 << 11), -1, 3, 9, null, null, TestName = "ExtraWearables_ABannerWithAWorldWearable_IsItsSlotAndSerial")]
    [TestCase(3 | (9 << 11), 5 | (2 << 11), 3, 9, 5, 2, TestName = "ExtraWearables_ABotkillerWithBoth_IsEachSlotAndSerial")]
    [TestCase(-1, -1, null, null, null, null, TestName = "ExtraWearables_TheInvalidHandles_AreNone")]
    [TestCase(null, null, null, null, null, null, TestName = "ExtraWearables_AWeaponSendingNone_IsNone")]
    public void ExtraWearables_AWeaponsHandles_AreDereferenceable(
        int? world, int? viewModel, int? worldSlot, int? worldSerial, int? viewModelSlot, int? viewModelSerial)
    {
        EntityState weapon = Weapon(EntityState.WeaponActive);

        if (world is { } sent)
        {
            weapon.Set("DT_TFWeaponBase.m_hExtraWearable", PropertyValue.FromInt(sent < 0 ? EntityState.NoHandle : sent));
        }

        if (viewModel is { } sentVm)
        {
            weapon.Set("DT_TFWeaponBase.m_hExtraWearableViewModel", PropertyValue.FromInt(sentVm < 0 ? EntityState.NoHandle : sentVm));
        }

        weapon.ExtraWearable().ShouldBe(worldSlot is { } s && worldSerial is { } n ? (s, n) : null);
        weapon.ExtraWearableViewModel().ShouldBe(viewModelSlot is { } vs && viewModelSerial is { } vn ? (vs, vn) : null);
    }

    /// <remarks>
    /// **`m_bBeingRepurposedForTaunt`**, a networked bool on `DT_TFWeaponBase` (`tf_weaponbase.cpp:204`), which
    /// `IsBeingRepurposedForTaunt` returns (`tf_weaponbase.h:609-610`). Absent is false, as the constructor sets it.
    /// </remarks>
    [TestCase(1, true, TestName = "RepurposedForTaunt_AWeaponSendingTrue_IsTrue")]
    [TestCase(0, false, TestName = "RepurposedForTaunt_AWeaponSendingFalse_IsFalse")]
    [TestCase(null, false, TestName = "RepurposedForTaunt_AWeaponSendingNone_IsFalse")]
    public void RepurposedForTaunt_TheWeaponsFlag_IsRead(int? sent, bool expected)
    {
        EntityState weapon = Weapon(EntityState.WeaponActive);

        if (sent is { } value)
        {
            weapon.Set("DT_TFWeaponBase.m_bBeingRepurposedForTaunt", PropertyValue.FromInt(value));
        }

        weapon.RepurposedForTaunt().ShouldBe(expected);
    }

    /// <summary>A weapon in the given state.</summary>
    private static EntityState Weapon(int state)
    {
        EntityState weapon = new(1, 0, 0, "CTFWeaponScattergun");

        weapon.Set($"{WeaponTable}.m_iState", PropertyValue.FromInt(state));

        return weapon;
    }
}
