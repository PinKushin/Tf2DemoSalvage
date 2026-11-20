using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The frame's own decisions turned into <see cref="EconParticleItem"/>s (B69 g): whether the item was drawn, its owner's
/// first-person view, life and dormancy, and its systems through the attribute hooks.
/// </summary>
public sealed class EconParticleItemsTests
{
    private const string ItemsGame = """
        "items_game"
        {
            "attributes"
            {
                "134" { "name" "attach particle effect" "attribute_class" "set_attached_particle" }
            }
            "items"
            {
                "300" { "name" "Unusual hat" "static_attrs" { "attach particle effect" "13" } }
                "301" { "name" "Plain hat" }
                "302" { "name" "World Traveler" "static_attrs" { "attach particle effect" "13" } }
                "303" { "name" "Unusual gun" "static_attrs" { "attach particle effect" "13" } }
                "304" { "name" "Unusual c_model gun" "attach_to_hands" "1" "static_attrs" { "attach particle effect" "13" } }
            }
            "attribute_controlled_attached_particles"
            {
                "cosmetic_unusual_effects" { "13" { "system" "superrare_burning1" "attachment" "bip_head" } }
            }
        }
        """;

    private const int Wearer = 10;
    private const int Hat = 40;
    private const int Gun = 41;

    private static readonly ItemSchema Schema = ItemSchema.Read(Encoding.UTF8.GetBytes(ItemsGame));
    private static readonly AttributeHooks Hooks = new(Schema);

    [Test]
    public void Of_AWornUnusualHat_IsAVisibleWearableCarryingItsEffect()
    {
        EconParticleItem item = Items([Prop(Hat, 300)], [Player(Hat, 300)]).ShouldHaveSingleItem();

        (item.Entity, item.Wearable, item.State).ShouldBe((Hat, true, EconParticleState.Visible));
        item.Systems.Select(system => system.System).ShouldBe(["superrare_burning1"]);
        item.Holder.OwnerDrawnAsWorldModel.ShouldBeTrue();
    }

    /// <summary>`m_bHasParticleSystems` false: `UpdateParticleSystems` returns (econ_entity.cpp:1224-1227).</summary>
    [Test]
    public void Of_AHatWithoutSystems_IsLeftOut() =>
        Items([Prop(Hat, 301)], [Player(Hat, 301)]).ShouldBeEmpty();

    /// <summary>In his own eyes a wearable's owner is not drawn (`ShouldDrawThisPlayer`), so its effects are not.</summary>
    [Test]
    public void Of_InTheWearersEyes_IsNotVisible() =>
        Items([Prop(Hat, 300)], [Player(Hat, 300)], inEyes: Wearer).ShouldHaveSingleItem().State
            .ShouldBe(EconParticleState.NotVisible);

    /// <summary>The control for the frame's draw decision: a hat the rules hid is not visible.</summary>
    [Test]
    public void Of_AHatTheFrameDidNotDraw_IsNotVisible() =>
        Items([Prop(Hat, 300)], [Player(Hat, 300)], drawn: _ => false).ShouldHaveSingleItem().State
            .ShouldBe(EconParticleState.NotVisible);

    [Test]
    public void Of_TheWorldTraveler_IsNotVisible() =>
        Items([Prop(Hat, 302)], [Player(Hat, 302)]).ShouldHaveSingleItem().State.ShouldBe(EconParticleState.NotVisible);

    /// <summary>
    /// The recorder's held weapon in his own eyes is not drawn in the world, and so is `VISIBLE_VM` (:1244-1253) — on the
    /// view model, which the holder says it is.
    /// </summary>
    [Test]
    public void Of_TheRecordersHeldWeaponInHisEyes_IsVisibleOnTheViewModel()
    {
        SceneProp gun = Prop(Gun, 303) with { WeaponState = EntityState.WeaponActive };

        EconParticleItem item = Items([gun], [Player(Gun, 303)], inEyes: Wearer, drawn: _ => false).ShouldHaveSingleItem();

        (item.Wearable, item.State, item.Holder.InLocalViewModel).ShouldBe((false, EconParticleState.VisibleViewModel, true));
    }

    /// <summary>
    /// **A hands model exists for the active attach-to-hands weapon of an owner not drawn** — <c>UpdateAttachmentModels</c>
    /// (econ_entity.cpp:1135-1178) with TF's <c>AttachmentModelsShouldBeVisible</c>, <c>m_iState == WEAPON_IS_ACTIVE</c>
    /// (tf_weaponbase.h:588). The controls: a weapon that is its own view model, a holstered one, and one seen from outside.
    /// </summary>
    [TestCase(304, EntityState.WeaponActive, true, true)]
    [TestCase(303, EntityState.WeaponActive, true, false)]
    [TestCase(304, 1, true, false)]
    [TestCase(304, EntityState.WeaponActive, false, false)]
    public void Of_AnAttachToHandsWeapon_HasAHandsModelOnlyActiveInHisEyes(int definition, int state, bool inEyes, bool handsModel)
    {
        SceneProp gun = Prop(Gun, definition) with { WeaponState = state };

        Items([gun], [Player(Gun, definition)], inEyes: inEyes ? Wearer : null, drawn: _ => !inEyes).ShouldHaveSingleItem()
            .AttachmentModel.ShouldBe(handsModel);
    }

    /// <summary>
    /// **A weapon's view-model owner exists whether or not the view is in his eyes** (B69 review): <c>pEffectOwnerVM</c> is
    /// <c>GetPlayerOwner()-&gt;GetViewModel()</c> for any weapon, set before the first-person test (econ_entity.cpp:1606-1620),
    /// and the stops at :1699-1719 run on it whenever it is non-null — "because it may be removed due to first/thirdperson
    /// switch" (:1655). So the recorder's weapon outside his eyes still has one, and its next update stops the effect it made
    /// there (<c>At_TheRecordersWeaponLeavingHisEyes_StopsItsViewModelEffect</c>). The controls: a wearable has none, nor
    /// another player's weapon (his view model is not the one modelled here).
    /// </summary>
    [Test]
    public void Of_TheRecordersWeaponOutsideHisEyes_HasAViewModelOwner()
    {
        SceneProp gun = Prop(Gun, 303) with { WeaponState = EntityState.WeaponActive };

        (Items([gun], [Player(Gun, 303)]).ShouldHaveSingleItem().Holder.HasViewModelOwner,
                Items([Prop(Hat, 300)], [Player(Hat, 300)]).ShouldHaveSingleItem().Holder.HasViewModelOwner,
                Items([gun with { OwnedBy = 11 }], [Player(Gun, 303) with { EntityIndex = 11 }]).ShouldHaveSingleItem().Holder.HasViewModelOwner)
            .ShouldBe((true, false, false));
    }

    /// <summary>
    /// **A live item whose owner is dormant is still an econ entity** (B69 review): a dormant player is not among the
    /// moment's players (<c>SceneDormantPlayer</c>, B725), yet his item can stay in the snapshot (B69 part 5), and the client
    /// keeps it: a weapon holds its effects until its own next update (tf_weaponbase.cpp:3525), which then decides with its
    /// owner dormant — <c>NOT_VISIBLE</c> (econ_entity.cpp:1224-1262). Dropped, it was taken as gone and released at once. So it
    /// is decided from its owner's last record with him dormant (B69 review B): here watched from outside, not visible. The control:
    /// an owner absent and NOT dormant — no player owner at all — is left out.
    /// </summary>
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void Of_AnItemOfADormantOwner_IsCarriedAsLastDecided(bool ownerDormant, bool carried)
    {
        SceneProp gun = Prop(Gun, 303) with { WeaponState = EntityState.WeaponActive, Received = 90 };
        EconParticleOwners owners = new();
        EconParticleItem before = Items([gun], [Player(Gun, 303)], owners: owners).ShouldHaveSingleItem();
        before.State.ShouldBe(EconParticleState.Visible, "the subject: shown while its owner was here");
        List<EconParticleItem> items = Items([gun], [], dormant: entity => ownerDormant && entity == Wearer, owners: owners);

        if (!carried)
        {
            items.ShouldBeEmpty();
            return;
        }

        items.ShouldHaveSingleItem().ShouldBe(before with { State = EconParticleState.NotVisible });

        EconParticleRuns runs = new();
        runs.At(100, landed: true, [before], _ => true, (_, _, _) => 0).ShouldHaveSingleItem();
        runs.At(101, landed: false, items, _ => true, (_, _, _) => 0).ShouldHaveSingleItem().Released
            .ShouldBeFalse("no update of its own: the weapon keeps its effect");
    }

    /// <summary>
    /// **The item's own dormancy, not its wearer's** (B69 part 5): <c>CEconEntity::SetDormant</c> is the item's own
    /// (<c>econ_entity.cpp:994-1001</c>), <c>IsDormant</c> is <c>m_bDormant</c> (<c>c_baseentity.cpp:4031-4039</c>), set when THAT
    /// entity leaves the snapshot. The server checks a carried item's PVS on its own edict (<c>FL_EDICT_PVSCHECK</c>,
    /// <c>basecombatweapon.cpp:132-145</c>; a wearable has no override), and SourceTV always sends an entity with a move parent
    /// (<c>baseentity.cpp:4116-4124</c>) — so a wearer out of view can keep a live hat.
    /// </summary>
    [TestCase(Hat, true, TestName = "Of_ADormantItem_IsDormant")]
    [TestCase(Wearer, false, TestName = "Of_AnItemOnADormantWearer_IsNotDormant")]
    public void Of_Dormancy_IsTheItemsOwn(int asleep, bool dormant) =>
        Items([Prop(Hat, 300)], [Player(Hat, 300)], dormant: entity => entity == asleep).ShouldHaveSingleItem().Dormant
            .ShouldBe(dormant);

    /// <summary>
    /// **A weapon's extra-wearable handles resolve as <c>EHANDLE::Get</c>, slot AND serial** (B69 part 4,
    /// <c>econ_entity.cpp:1611-1624</c>), against the moment's props, each with its own dormancy; a handle naming the slot's
    /// other occupant is none.
    /// </summary>
    [TestCase(3, true, TestName = "Of_AWeaponNamingALiveExtraWearable_CarriesIt")]
    [TestCase(4, false, TestName = "Of_AWeaponNamingAnotherOccupant_CarriesNone")]
    public void Of_AWeaponsExtraWearable_ResolvesBySlotAndSerial(int serial, bool resolves)
    {
        const int Fob = 50;
        SceneProp gun = Prop(Gun, 303) with
        {
            WeaponState = EntityState.WeaponActive,
            ClassName = "CTFBuffItem",
            ExtraWearable = (Fob, serial),
            ExtraWearableViewModel = (Fob, serial),
        };
        SceneProp fob = Prop(Fob, 301) with { SerialNumber = 3 };

        EconParticleItem item = Items([gun, fob], [Player(Gun, 303)], dormant: entity => entity == Fob)
            .Single(each => each.Entity == Gun);

        (item.ExtraWearable, item.ExtraWearableViewModel).ShouldBe(
            resolves ? (new EconExtraWearable(Fob, true), new EconExtraWearable(Fob, true)) : (null, null));
    }

    /// <summary>
    /// **The carry is the SAME entity's** (B69 review B): a dormant owner's item is carried only when the slot holds the item
    /// the last frame decided — its serial and definition (<c>C_BaseEntity::Init( entnum, iSerialNum )</c>,
    /// c_baseentity.cpp:1088-1125, a new serial a new object made <c>NOT_VISIBLE</c>, econ_entity.cpp:1022-1027). Another
    /// item in the slot inherits nothing; nor does anything after the level is forgotten. The control: the same item is carried.
    /// </summary>
    [TestCase(1, 303, false, true)]
    [TestCase(2, 303, false, false)]
    [TestCase(1, 300, false, false)]
    [TestCase(1, 303, true, false)]
    public void Of_ADormantOwnersSlot_CarriesOnlyTheSameItem(int serial, int definition, bool forgotten, bool carried)
    {
        SceneProp gun = Prop(Gun, 303) with { WeaponState = EntityState.WeaponActive, Received = 90, SerialNumber = 1 };
        EconParticleOwners owners = new();
        Items([gun], [Player(Gun, 303)], owners: owners).ShouldHaveSingleItem();

        if (forgotten)
        {
            owners.Clear();
        }

        Items([gun with { SerialNumber = serial, ItemDefinitionIndex = definition }], [], dormant: entity => entity == Wearer, owners: owners)
            .Count.ShouldBe(carried ? 1 : 0);
    }

    /// <summary>
    /// **A dormant owner's item is decided as the engine decides it, not forced hidden** (B69 review B): the
    /// <c>VISIBLE_VM</c> promotion asks only that the local player own it, hold it in his view model, be watched in his eyes
    /// and be alive (econ_entity.cpp:1243-1253) — not his dormancy — so the recorder's active weapon stays on the view model
    /// while the timeline holds him dormant, alive by his dormant record. The control: that record dead, not visible.
    /// </summary>
    [TestCase(true, EconParticleState.VisibleViewModel)]
    [TestCase(false, EconParticleState.NotVisible)]
    public void Of_TheRecordersActiveWeaponWithHimDormant_IsDecidedAsTheEngineDoes(bool alive, EconParticleState state)
    {
        SceneProp gun = Prop(Gun, 303) with { WeaponState = EntityState.WeaponActive, Received = 90 };
        EconParticleOwners owners = new();
        Items([gun], [Player(Gun, 303)], inEyes: Wearer, drawn: _ => false, owners: owners).ShouldHaveSingleItem().State
            .ShouldBe(EconParticleState.VisibleViewModel, "the subject, awake");

        Items(
                [gun], [], inEyes: Wearer, drawn: _ => false, dormant: entity => entity == Wearer, owners: owners,
                asleep: [new SceneDormantPlayer(Wearer, 0f, 0f, 0f, alive)])
            .ShouldHaveSingleItem().State.ShouldBe(state);
    }

    /// <summary>
    /// **And its hands model is this frame's** (B69 review B): <c>UpdateAttachmentModels</c> releases
    /// <c>m_hViewmodelAttachment</c> once the weapon is no longer active (econ_entity.cpp:1135-1146, tf_weaponbase.h:588), so a
    /// weapon holstered while its owner is dormant has none. The control: still active, it keeps it.
    /// </summary>
    [TestCase(EntityState.WeaponActive, true)]
    [TestCase(1, false)]
    public void Of_ADormantOwnersWeapon_HasTheHandsModelOfThisFrame(int state, bool handsModel)
    {
        SceneProp gun = Prop(Gun, 304) with { WeaponState = EntityState.WeaponActive, Received = 90 };
        EconParticleOwners owners = new();
        Items([gun], [Player(Gun, 304)], inEyes: Wearer, drawn: _ => false, owners: owners).ShouldHaveSingleItem().AttachmentModel
            .ShouldBeTrue("the subject, awake");

        Items([gun with { WeaponState = state }], [], inEyes: Wearer, drawn: _ => false, dormant: entity => entity == Wearer, owners: owners)
            .ShouldHaveSingleItem().AttachmentModel.ShouldBe(handsModel);
    }

    private static List<EconParticleItem> Items(
        SceneProp[] props,
        ScenePlayer[] players,
        int? inEyes = null,
        System.Func<SceneProp, bool>? drawn = null,
        System.Func<int, bool>? dormant = null,
        EconParticleOwners? owners = null,
        IReadOnlyList<SceneDormantPlayer>? asleep = null)
    {
        List<EconParticleItem> items = [];

        EconParticleItems.Of(
            props, drawn ?? (_ => true), players, dormant ?? (_ => false), local: Wearer, inEyes, viewModelDraws: true, Schema, Hooks, items,
            owners, asleep);

        return items;
    }

    private static SceneProp Prop(int entity, int definition) =>
        new(entity, "models/item.mdl", SceneModelKind.Studio, new ScenePose(), OwnedBy: Wearer, ItemDefinitionIndex: definition,
            ClassName: "CTFWearable", TeamNumber: 2);

    private static ScenePlayer Player(int entity, int definition) =>
        new(Wearer, 0f, 0f, 0f, 2, 125, 1)
        {
            Items = [new SceneItem(entity, "CTFWearable", definition, new EconAttributeWire([], [], false), entity == Gun)],
        };
}
