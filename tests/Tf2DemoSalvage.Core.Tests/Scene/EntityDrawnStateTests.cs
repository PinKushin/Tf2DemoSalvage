using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="EntityState.IsDrawn"/>, <c>EF_NODRAW</c>, and the two tables it reads (B335).
/// </summary>
/// <remarks>
/// **This is how a dead player disappears**, and it is worth having a synthetic test because the
/// mechanism is not the one people assume: death is `EF_NODRAW` on the living entity plus a
/// separate `CTFRagdoll` for the corpse, not an animation. `docs/memory/death-is-ef-nodraw-not-an-
/// animation.md` records the session that went looking for a death animation.
///
/// **`EF_NODRAW` is 0x020**, and `IsDrawn` reads it from whichever of two tables the entity
/// declares it in — a viewmodel is `BEGIN_NETWORK_TABLE_NOBASE` and so carries its own copy rather
/// than inheriting `DT_BaseEntity`'s.
///
/// **What it deliberately does NOT do is remove the entity**, which the type's own remarks record
/// as a trap walked into twice in one hour: every setup gate's grate props are PARENTED to an
/// invisible `func_door`, so an entity dropped for not drawing takes its children's transform with
/// it and the gates vanish.
/// </remarks>
public sealed class EntityDrawnStateTests
{
    private const string BaseEntity = "DT_BaseEntity";
    private const string ViewModel = "DT_BaseViewModel";

    /// <summary>EF_NODRAW.</summary>
    private const int NoDraw = 0x020;

    [Test]
    public void IsDrawn_AnEntitySayingNothingAboutEffects_IsDrawn()
    {
        Entity().IsDrawn.ShouldBeTrue("no effects at all means no EF_NODRAW");
    }

    [Test]
    public void IsDrawn_AnEntityCarryingNoDraw_IsNot()
    {
        EntityState entity = Entity();
        entity.Set($"{BaseEntity}.m_fEffects", PropertyValue.FromInt(NoDraw));

        entity.IsDrawn.ShouldBeFalse();
    }

    /// <remarks>
    /// **A BIT test, not an equality.** `m_fEffects` is a flag word and an entity commonly carries
    /// several at once — `EF_BONEMERGE` (0x001) is on every worn cosmetic. Comparing the whole word
    /// against 0x020 would draw a dead player still wearing a hat, and comparing it against zero
    /// would hide every bone-merged item on the map.
    /// </remarks>
    [Test]
    public void IsDrawn_NoDrawBesideOtherFlags_IsStillNotDrawn()
    {
        EntityState entity = Entity();
        entity.Set($"{BaseEntity}.m_fEffects", PropertyValue.FromInt(NoDraw | 0x001 | 0x008));

        entity.IsDrawn.ShouldBeFalse("the flag is tested by mask, not by equality");
    }

    /// <remarks>
    /// **The control on the mask, and the one that catches an inverted or over-broad test.** Flags
    /// either side of 0x020 — 0x010 and 0x040 — must leave the entity drawn.
    /// </remarks>
    [Test]
    public void IsDrawn_OtherFlagsWithoutNoDraw_IsDrawn()
    {
        EntityState entity = Entity();

        entity.Set($"{BaseEntity}.m_fEffects", PropertyValue.FromInt(0x010 | 0x040));

        entity.IsDrawn.ShouldBeTrue("neighbouring bits are not EF_NODRAW");
    }

    /// <remarks>
    /// **A viewmodel declares its own `m_fEffects`**, because `DT_BaseViewModel` is
    /// `BEGIN_NETWORK_TABLE_NOBASE` and inherits no `DT_BaseEntity`. A reader consulting only the
    /// base table would draw a hidden viewmodel — which is what a dead player's hands are.
    /// </remarks>
    [Test]
    public void IsDrawn_AViewmodelHidingItself_IsReadFromItsOwnTable()
    {
        EntityState weapon = Entity();
        weapon.Set($"{ViewModel}.m_fEffects", PropertyValue.FromInt(NoDraw));

        weapon.IsDrawn.ShouldBeFalse("DT_BaseViewModel carries its own copy");
    }

    /// <remarks>
    /// **The base table WINS when both are present**, which is the `??` chain's order and not an
    /// arbitrary one: an entity declaring both is a base entity that also happens to carry the
    /// viewmodel table, and the base table is the one the engine's `GetEffects` reads.
    /// </remarks>
    [Test]
    public void IsDrawn_BothTablesPresent_TakesTheBaseEntitysValue()
    {
        EntityState entity = Entity();

        entity.Set($"{BaseEntity}.m_fEffects", PropertyValue.FromInt(0));
        entity.Set($"{ViewModel}.m_fEffects", PropertyValue.FromInt(NoDraw));

        entity.IsDrawn.ShouldBeTrue("the base table is consulted first");
    }

    /// <remarks>
    /// **`IsVisible` is the other half of the conjunction**, and it is set by a different mechanism
    /// entirely — the render mode. An entity can be drawable by its effects and still not drawn.
    /// </remarks>
    [Test]
    public void IsDrawn_AnEntityMarkedInvisible_IsNotDrawnWhateverItsEffects()
    {
        EntityState entity = Entity();
        entity.Set($"{BaseEntity}.m_fEffects", PropertyValue.FromInt(0));
        entity.IsVisible = false;

        entity.IsDrawn.ShouldBeFalse();
    }

    /// <remarks>
    /// **A model index of zero is a real answer and absent is not**, which the accessor keeps
    /// apart deliberately: zero means "no model" and null means the property never arrived.
    /// Collapsing them hides a decode that missed a property behind a value that looks chosen.
    /// </remarks>
    [Test]
    public void ModelIndex_ZeroAgainstNeverSent_AreDifferentAnswers()
    {
        Entity().ModelIndex().ShouldBeNull("never sent");

        EntityState entity = Entity();
        entity.Set($"{BaseEntity}.m_nModelIndex", PropertyValue.FromInt(0));

        entity.ModelIndex().ShouldBe(0, "sent, and zero");
    }

    /// <remarks>
    /// A viewmodel's model index comes from its own table, the same split as the effects above —
    /// and the two accessors must not read each other's, or a weapon would draw as its owner.
    /// </remarks>
    [Test]
    public void ViewmodelModelIndex_ReadsTheViewmodelTableAndNotTheBaseOne()
    {
        EntityState entity = Entity();

        entity.Set($"{BaseEntity}.m_nModelIndex", PropertyValue.FromInt(11));
        entity.Set($"{ViewModel}.m_nModelIndex", PropertyValue.FromInt(22));

        entity.ModelIndex().ShouldBe(11);
        entity.ViewmodelModelIndex().ShouldBe(22);
    }

    /// <summary>An entity carrying nothing yet.</summary>
    private static EntityState Entity() => new(1, 0, 0, "CBaseAnimating");
}
