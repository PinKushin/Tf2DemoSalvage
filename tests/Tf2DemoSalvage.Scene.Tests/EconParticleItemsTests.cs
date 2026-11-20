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
                "520" { "name" "particle effect use head origin" "attribute_class" "particle_effect_use_head_origin" }
                "521" { "name" "particle effect vertical offset" "attribute_class" "particle_effect_vertical_offset" }
            }
            "items"
            {
                "300" { "name" "Unusual hat" "static_attrs" { "attach particle effect" "13" } }
                "301" { "name" "Plain hat" }
                "302" { "name" "World Traveler" "static_attrs" { "attach particle effect" "13" } }
                "303" { "name" "Unusual gun" "static_attrs" { "attach particle effect" "13" } }
                "304" { "name" "Unusual c_model gun" "attach_to_hands" "1" "model_player" "models/c_gun.mdl" "static_attrs" { "attach particle effect" "13" } }
                "306" { "name" "Unusual head-origin hat" "static_attrs" { "attach particle effect" "13" "particle effect use head origin" "1" "particle effect vertical offset" "4.5" } }
                "305" { "name" "Unusual hands wearable" "attach_to_hands" "1" "model_player" "models/c_glove.mdl" "static_attrs" { "attach particle effect" "13" } }
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
    /// **An item's head origin and its height are its own attribute hooks** (B69 review K): <c>UpdateControlPoint</c> asks
    /// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER( item, …, particle_effect_use_head_origin )</c> and, when positive,
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( item, …, particle_effect_vertical_offset )</c> (particle_property.cpp:565-588). The
    /// control: an unusual without the attribute has neither.
    /// </summary>
    [TestCase(306, true, 4.5f)]
    [TestCase(300, false, 0f)]
    public void Of_AnItemWithTheHeadOriginAttribute_CarriesItAndItsOffset(int definition, bool head, float offset)
    {
        EconParticleItem item = Items([Prop(Hat, definition)], [Player(Hat, definition)]).ShouldHaveSingleItem();

        (item.HeadOrigin, item.VerticalOffset).ShouldBe((head, offset));
    }

    /// <summary>
    /// **An attach-to-hands item's hands model is named by its path, and a non-weapon has one too** (B69 review D):
    /// <c>AttachmentModelsShouldBeVisible</c> is <c>true</c> in the base (econ_entity.h:99) and only a TF weapon narrows it to
    /// the active one (tf_weaponbase.h:588), so a wearable with <c>attach_to_hands</c> of an owner in his own eyes gets
    /// <c>m_hViewmodelAttachment</c> (econ_entity.cpp:1135-1178), its model <c>GetPlayerDisplayModel</c> (:1167). Only a
    /// weapon's is the hands model posed here. The controls: seen from outside, none; the active weapon, its own.
    /// </summary>
    [TestCase(305, 0, true, "models/c_glove.mdl", false)]
    [TestCase(305, 0, false, null, false)]
    [TestCase(304, EntityState.WeaponActive, true, "models/c_gun.mdl", true)]
    public void Of_AnAttachToHandsItemInHisEyes_NamesItsHandsModel(int definition, int state, bool inEyes, string? hands, bool placed)
    {
        SceneProp prop = Prop(Hat, definition) with { WeaponState = state == 0 ? null : state };

        EconParticleItem item = Items([prop], [Player(Hat, definition)], inEyes: inEyes ? Wearer : null, drawn: _ => !inEyes)
            .ShouldHaveSingleItem();

        (item.HandsModel, item.AttachmentModel).ShouldBe((hands, placed));
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
            resolves
                ? (new EconExtraWearable(Fob, true, "models/item.mdl"), new EconExtraWearable(Fob, true, "models/item.mdl"))
                : (null, null));
    }

    /// <summary>
    /// **An extra wearable's head hook is its owner's own list alone** (B69 review 3): <c>CTFWeaponBase</c> makes it with a
    /// model and no item (tf_weaponbase.cpp:972-1026), so <c>CALL_ATTRIB_HOOK_INT_ON_OTHER( wearable, … )</c> walks its empty
    /// item and then <c>GetAttributeOwner()</c>'s own list (econ_entity.h:144, attribute_manager.cpp:480-491) — never the
    /// weapon's attributes. Cases: the owner carries it; only the weapon carries it; neither.
    /// </summary>
    [TestCase(true, 303, true, 2.5f)]
    [TestCase(false, 306, false, 0f)]
    [TestCase(false, 303, false, 0f)]
    public void Of_AWeaponsExtraWearable_AsksTheHeadHookOfItsOwnersOwnList(bool ownerCarries, int definition, bool head, float offset)
    {
        const int Fob = 50;
        SceneProp gun = Prop(Gun, definition) with { WeaponState = EntityState.WeaponActive, ExtraWearable = (Fob, 3) };
        SceneProp fob = Prop(Fob, 301) with { SerialNumber = 3 };
        ScenePlayer owner = Player(Gun, definition) with
        {
            OwnAttributes = ownerCarries
                ? [new EconAttributeValue(520, System.BitConverter.SingleToInt32Bits(1f)), new EconAttributeValue(521, System.BitConverter.SingleToInt32Bits(2.5f))]
                : null,
        };

        EconParticleItem item = Items([gun, fob], [owner]).Single(each => each.Entity == Gun);

        (item.ExtraWearableHeadOrigin, item.ExtraWearableVerticalOffset).ShouldBe((head, offset));
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
    /// **His last received record decides only the item it names** (B69 fourth round, item 7): matched by entity and
    /// definition, as the slot's occupant (c_baseentity.cpp:1088-1125). The record naming another definition in the slot —
    /// the slot reused for another item during the spell — decides nothing, and is not overridden by an older frame's
    /// record of the prop's own definition. The control: the record naming the prop's definition decides it.
    /// </summary>
    [TestCase(300, true)]
    [TestCase(303, false)]
    public void Of_ADormantOwnersLastRecord_DecidesOnlyTheSameDefinition(int recorded, bool decided)
    {
        EconParticleOwners owners = new();
        Items([Prop(Hat, 300)], [Player(Hat, 300)], owners: owners).ShouldHaveSingleItem("the subject: a frame's record of 300");
        ScenePlayer before = Player(Hat, recorded);

        Items([Prop(Hat, 300)], [], dormant: entity => entity == Wearer, owners: owners, lastAwake: _ => before)
            .Count.ShouldBe(decided ? 1 : 0);
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

    /// <summary>
    /// **A repeat frame's decision allocates nothing** (B69 review 4; the target is 1000 fps): the head-origin hooks, the hands
    /// model's lookup and the systems are asked of every item every frame, so none may allocate once warm. A head-origin hat
    /// and an attach-to-hands weapon in his eyes, the weapon with an extra wearable whose own head hook is asked too (B69
    /// fourth round, item 5); the least of five frames (the runtime's own allocations land in one,
    /// docs/memory/a-thread-allocation-counter-counts-the-runtime.md).
    /// </summary>
    [Test]
    public void Of_ARepeatFrameWithHeadOriginAndHandsItems_AllocatesNothing()
    {
        const int Fob = 50;
        SceneProp gun = Prop(Gun, 304) with { WeaponState = EntityState.WeaponActive, ExtraWearable = (Fob, 3) };
        SceneProp[] props = [Prop(Hat, 306), gun, Prop(Fob, 301) with { SerialNumber = 3 }];
        ScenePlayer[] players =
        [
            Player(Hat, 306) with
            {
                OwnAttributes =
                [
                    new EconAttributeValue(520, System.BitConverter.SingleToInt32Bits(1f)),
                    new EconAttributeValue(521, System.BitConverter.SingleToInt32Bits(2.5f)),
                ],
                Items =
                [
                    new SceneItem(Hat, "CTFWearable", 306, new EconAttributeWire([], [], false), false),
                    new SceneItem(Gun, "CTFWearable", 304, new EconAttributeWire([], [], false), true),
                ],
            },
        ];
        System.Func<SceneProp, bool> drawn = static _ => false;
        System.Func<int, bool> dormant = static _ => false;
        EconParticleOwners owners = new();
        List<EconParticleItem> items = new(4);

        void Frame()
        {
            items.Clear();
            EconParticleItems.Of(props, drawn, players, dormant, local: Wearer, inEyes: Wearer, viewModelDraws: true, Schema, Hooks, items, owners);
        }

        Frame();
        Frame();
        long allocated = long.MaxValue;

        for (int frame = 0; frame < 5; frame++)
        {
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Frame();
            allocated = System.Math.Min(allocated, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        allocated.ShouldBe(0);
        items.Select(item => (item.Entity, item.HandsModel, item.ExtraWearableHeadOrigin, item.ExtraWearableVerticalOffset))
            .ShouldBe([(Hat, null, false, 0f), (Gun, "models/c_gun.mdl", true, 2.5f)], "the control: both decided");
    }

    /// <summary>
    /// **An item's facts survive new instances of equal values** (B69 fourth round, item 6): the timeline shares a player's
    /// item list only while EVERY element equals the last frame's (<c>DemoTimeline.SameAsBefore</c>, B433), so another item
    /// changing — a weapon's last fire time — hands this hat a new <c>SceneItem</c>; and a rebuild hands a new own-attribute
    /// list. Each frame here gets new instances of the same values; the least of five frames allocates nothing. The control:
    /// the hat is decided with its head origin and offset.
    /// </summary>
    [Test]
    public void Of_FramesWithNewInstancesOfEqualValues_AllocateNothing()
    {
        SceneProp[] props = [Prop(Hat, 306)];
        ScenePlayer[][] frames =
        [
            .. Enumerable.Range(0, 7).Select(_ => new[]
            {
                Player(Hat, 306) with { OwnAttributes = [new EconAttributeValue(2, System.BitConverter.SingleToInt32Bits(1f))] },
            }),
        ];
        System.Func<SceneProp, bool> drawn = static _ => true;
        System.Func<int, bool> dormant = static _ => false;
        EconParticleOwners owners = new();
        List<EconParticleItem> items = new(4);

        void Frame(int at)
        {
            items.Clear();
            EconParticleItems.Of(props, drawn, frames[at], dormant, local: null, inEyes: null, viewModelDraws: true, Schema, Hooks, items, owners);
        }

        Frame(0);
        Frame(1);
        long allocated = long.MaxValue;

        for (int frame = 2; frame < 7; frame++)
        {
            long at = System.GC.GetAllocatedBytesForCurrentThread();
            Frame(frame);
            allocated = System.Math.Min(allocated, System.GC.GetAllocatedBytesForCurrentThread() - at);
        }

        allocated.ShouldBe(0);
        items.ShouldHaveSingleItem().HeadOrigin.ShouldBeTrue("the control");
        items[0].VerticalOffset.ShouldBe(4.5f);
    }

    /// <summary>
    /// **The item view is the entity's own** (B69 fourth round, item 6): <c>CEconEntity</c>'s <c>m_AttributeManager.m_Item</c>
    /// carries its own <c>m_iItemDefinitionIndex</c> (econ_item_view.cpp:182), which is the prop's; the hands model is
    /// <c>GetPlayerDisplayModel</c> on it (econ_entity.cpp:1167). A slot reused for another item while the owner's carried list
    /// still names the old one answers from the prop. The control: the two agreeing.
    /// </summary>
    [TestCase(303)]
    [TestCase(304)]
    public void Of_AReusedSlotWhoseCarriedRecordNamesAnotherItem_TakesTheHandsModelFromTheProp(int recorded)
    {
        SceneProp gun = Prop(Gun, 304) with { WeaponState = EntityState.WeaponActive };

        Items([gun], [Player(Gun, recorded)], inEyes: Wearer, drawn: _ => false).ShouldHaveSingleItem().HandsModel
            .ShouldBe("models/c_gun.mdl");
    }

    /// <summary>
    /// **A frame of a dormant owner's spell allocates nothing** (B69 fourth round, item 1): his items are decided from his
    /// record as last received, every frame of the spell. An item with systems and one without; the least of five frames.
    /// </summary>
    [Test]
    public void Of_ARepeatFrameOfADormantOwner_AllocatesNothing()
    {
        const int Plain = 42;
        SceneProp[] props = [Prop(Hat, 300), Prop(Plain, 301)];
        ScenePlayer before = Player(Hat, 300) with
        {
            Items =
            [
                new SceneItem(Hat, "CTFWearable", 300, new EconAttributeWire([], [], false), false),
                new SceneItem(Plain, "CTFWearable", 301, new EconAttributeWire([], [], false), false),
            ],
        };
        System.Func<SceneProp, bool> drawn = static _ => true;
        System.Func<int, bool> dormant = static entity => entity == Wearer;
        System.Func<int, ScenePlayer?> lastAwake = owner => owner == Wearer ? before : null;
        SceneDormantPlayer[] asleep = [new SceneDormantPlayer(Wearer, 0f, 0f, 0f, true)];
        EconParticleOwners owners = new();
        List<EconParticleItem> items = new(4);

        void Frame()
        {
            items.Clear();
            EconParticleItems.Of(
                props, drawn, [], dormant, local: null, inEyes: null, viewModelDraws: true, Schema, Hooks, items, owners, asleep,
                lastAwake);
        }

        Frame();
        Frame();
        long allocated = long.MaxValue;

        for (int frame = 0; frame < 5; frame++)
        {
            long at = System.GC.GetAllocatedBytesForCurrentThread();
            Frame();
            allocated = System.Math.Min(allocated, System.GC.GetAllocatedBytesForCurrentThread() - at);
        }

        allocated.ShouldBe(0);
        items.Select(item => item.Entity).ShouldBe([Hat], "the control: the hat decided from his record");
    }

    private static List<EconParticleItem> Items(
        SceneProp[] props,
        ScenePlayer[] players,
        int? inEyes = null,
        System.Func<SceneProp, bool>? drawn = null,
        System.Func<int, bool>? dormant = null,
        EconParticleOwners? owners = null,
        IReadOnlyList<SceneDormantPlayer>? asleep = null,
        System.Func<int, ScenePlayer?>? lastAwake = null)
    {
        List<EconParticleItem> items = [];

        EconParticleItems.Of(
            props, drawn ?? (_ => true), players, dormant ?? (_ => false), local: Wearer, inEyes, viewModelDraws: true, Schema, Hooks, items,
            owners, asleep, lastAwake);

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
