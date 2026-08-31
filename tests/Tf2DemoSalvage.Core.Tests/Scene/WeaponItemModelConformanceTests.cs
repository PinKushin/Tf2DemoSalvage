using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A weapon's model comes from its ITEM, so a weapon that networks no model index still has one.
/// </summary>
/// <remarks>
/// **<c>CEconEntity::SetModel</c> resolves through the item schema, not through the wire.** This
/// project already records the citation, for the viewmodel, at <c>DemoTimeline.cs:1600</c>:
///
/// > <c>pItem-&gt;GetPlayerDisplayModel( iClass, team )</c> (<c>econ_entity.cpp:1167</c>), which is
/// > <c>model_player</c> from <c>items_game.txt</c>. Taking the weapon entity's own
/// > <c>m_nModelIndex</c> was tried on 2026-08-28 and drew no weapon at all: <c>m_hWeapon</c> says
/// > WHICH weapon and the schema says what it looks like. Both hops are needed and they are
/// > different questions.
///
/// <c>WeaponModels.For</c> implements it and is wired to the viewmodel and to the followed player —
/// and NOT to the weapon entities other players carry, which take
/// <c>WorldModelIndex() ?? ModelIndex()</c> and nothing else.
///
/// **Measured on the owner's `cp_fulgur` recording**, every weapon with an owner:
///
/// <code>
///   ENTITY  968 CTFRocketLauncher model  996 worldmodel  426 item   513 attachment 18
///   ENTITY 1100 CTFFlameThrower   model none worldmodel  225 item    40 attachment  5
///   ENTITY  940 CTFMinigun        model none worldmodel  393 item   424 attachment 24
///   ENTITY 1017 CWeaponMedigun    model none worldmodel none item   211 attachment  8
///   ENTITY 1109 CWeaponMedigun    model none worldmodel none item   211 attachment 21
///   ENTITY 1192 CTFMinigun        model none worldmodel none item 15123 attachment 23
/// </code>
///
/// **Item 211 is the stock Medi Gun.** The three that network no model at all still say exactly
/// which weapon they are — so the information was never missing, only unread. Owner's report:
/// *"mediguns still are not drawing on other players too"*, and it is not medigun-specific — a
/// minigun does it too.
///
/// **What went wrong downstream is a routing decision, and that is what these tests pin.**
/// `RecordProp` puts a track with an empty model path into `playerTracks` rather than `props`, on
/// the reasoning that *"they carry poses and no model, so a consumer walking Props to draw models
/// would find one it cannot draw"*. That is right for a player and wrong for a weapon whose model
/// is merely not on the wire yet — and nothing walking `playerTracks` will ever draw it.
///
/// Synthetic, in `Core.Tests`: the resolution itself belongs to `WeaponModels` in the Scene layer,
/// and what is asserted here is only that Core carries the item outward and keeps the track
/// reachable (D38).
/// </remarks>
public sealed class WeaponItemModelConformanceTests
{
    /// <summary>The stock Medi Gun's item definition index.</summary>
    private const int MediGun = 211;

    [Test]
    public void Build_AWeaponWithNoModelIndex_StillReachesTheProps()
    {
        // **The case the owner reported.** Without this the track is in `playerTracks`, where the
        // draw path never looks, and no later resolution can rescue it.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticWeapon.Demo(
            item: MediGun, worldModelIndex: null, ownerEntity: 3));

        timeline.Props
            .Count(track => track.EntityIndex == SyntheticWeapon.WeaponEntityIndex)
            .ShouldBe(1, "a weapon whose model is not on the wire is still a prop to be drawn");
    }

    [Test]
    public void Build_AWeaponWithNoModelIndex_CarriesItsItemOutward()
    {
        // Core cannot read `items_game.txt` and must not try; what it owes the Scene layer is the
        // item number, which is the whole of what the engine needs to name the model.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticWeapon.Demo(
            item: MediGun, worldModelIndex: null, ownerEntity: 3));

        timeline.Props
            .Single(track => track.EntityIndex == SyntheticWeapon.WeaponEntityIndex)
            .ItemDefinitionIndex
            .ShouldBe(MediGun);
    }

    [Test]
    public void Build_AWeaponWithAWorldModel_KeepsUsingIt()
    {
        // **The control, and it is the majority case.** Rocket launchers, flamethrowers and most
        // miniguns DO network a world model, and a fix that preferred the item schema everywhere
        // would re-resolve every one of them — replacing a measured index with a lookup, which is
        // a change nobody asked for and a chance to be wrong about weapons that currently work.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticWeapon.Demo(
            item: MediGun, worldModelIndex: SyntheticWeapon.WorldModel, ownerEntity: 3));

        timeline.Props
            .Single(track => track.EntityIndex == SyntheticWeapon.WeaponEntityIndex)
            .ModelPath
            .ShouldBe(SyntheticWeapon.WorldModelPath, "the wire named a model, so nothing is guessed");
    }

    [Test]
    public void Build_APlayerWithNoModel_StaysOutOfTheProps()
    {
        // **The other control, and it guards the rule this change relaxes.** A player entity has a
        // pose and no model on purpose; letting every model-less track into `props` would put one
        // there that no asset can satisfy, which is the false missing-asset alarm the split exists
        // to prevent. Only a track that says WHICH item it is may cross over.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticWeapon.Demo(
            item: null, worldModelIndex: null, ownerEntity: 3));

        timeline.Props
            .Count(track => track.EntityIndex == SyntheticWeapon.WeaponEntityIndex)
            .ShouldBe(0, "with no item and no model there is nothing a draw path could resolve");
    }
}
