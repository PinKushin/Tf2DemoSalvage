using System.Linq;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The properties that say which model an entity is, and how it is posed.
/// </summary>
/// <remarks>
/// **The names are measured, not guessed.** They were taken from a trace of
/// <c>tf2-2013-build1729296-stv-cp_foundry.dem</c>: <c>DT_BaseEntity.m_nModelIndex</c> on 119
/// entities, <c>DT_BaseEntity.m_angRotation</c> on 76, <c>DT_BaseAnimating.m_nSequence</c> on 16.
/// Guessing a property name produces an entity that decodes perfectly and renders as nothing,
/// which looks like a renderer fault rather than a lookup one.
///
/// Which tables they live in matters: the model index and the rotation are on
/// <c>DT_BaseEntity</c>, so everything with a position has them, while the animation properties
/// are on <c>DT_BaseAnimating</c> and only things that animate carry them.
/// </remarks>
public sealed class EntityModelTests
{
    [Test]
    public void ModelIndex_WhenTheEntitySentOne_IsRead()
    {
        EntityState entity = State(Property("DT_BaseEntity", "m_nModelIndex", PropertyValue.FromInt(42)));

        entity.ModelIndex().ShouldBe(42);
    }

    [Test]
    public void ModelIndex_WhenTheEntitySentNone_IsNothing()
    {
        // Null rather than zero: zero is a real index meaning "no model", and an entity that never
        // sent the property at all is a different thing from one that sent zero. Collapsing them
        // hides a decode that missed the property.
        EntityState entity = State(Property("DT_BaseEntity", "m_iTeamNum", PropertyValue.FromInt(2)));

        entity.ModelIndex().ShouldBeNull();
    }

    [Test]
    public void Angles_AreReadAsPitchYawRoll()
    {
        // A QAngle is (pitch, yaw, roll) - Valve's own order, and the order PropTransform already
        // expects. Reading it as (x, y, z) and hoping puts every prop in the map facing wrongly,
        // which is a picture nobody can check without knowing the map.
        EntityState entity = State(
            Property("DT_BaseEntity", "m_angRotation", PropertyValue.FromVector(10f, 90f, 0f)));

        entity.Angles().ShouldBe((10f, 90f, 0f));
    }

    [Test]
    public void Angles_WhenNotSent_AreNothing()
    {
        // Distinct from all-zero, which is a real orientation. A prop that never sent a rotation
        // is not a prop facing along positive X - though it is drawn that way, so the difference
        // only shows in what the code can say about it.
        EntityState entity = State(Property("DT_BaseEntity", "m_nModelIndex", PropertyValue.FromInt(3)));

        entity.Angles().ShouldBeNull();
    }

    [Test]
    public void Animation_ReadsSequenceCycleAndRate()
    {
        // The three that make a model move: which animation, how far through it, and how fast.
        // c_baseanimating.cpp networks all three (lines 173, 152, 186).
        EntityState entity = State(
            Property("DT_BaseAnimating", "m_nSequence", PropertyValue.FromInt(7)),
            Property("DT_ServerAnimationData", "m_flCycle", PropertyValue.FromFloat(0.25f)),
            Property("DT_BaseAnimating", "m_flPlaybackRate", PropertyValue.FromFloat(1.5f)));

        entity.AnimationSequence().ShouldBe(7);
        entity.Cycle().ShouldBe(0.25f);
        entity.PlaybackRate().ShouldBe(1.5f);
    }

    [Test]
    public void Animation_OnSomethingThatDoesNotAnimate_IsNothing()
    {
        // The control: DT_BaseAnimating is a different table from DT_BaseEntity, and a pickup that
        // sends a model but no animation must not report sequence zero - which is a real animation,
        // usually the idle one.
        EntityState entity = State(Property("DT_BaseEntity", "m_nModelIndex", PropertyValue.FromInt(3)));

        entity.AnimationSequence().ShouldBeNull();
        entity.Cycle().ShouldBeNull();
    }

    [Test]
    public void ModelScale_WhenNotSent_IsNothing()
    {
        // Left to the caller to default to 1, because zero is the value the property would decode
        // to if it were missing and read as a number - and a prop at scale zero is invisible,
        // which reads as a rendering fault.
        EntityState entity = State(Property("DT_BaseEntity", "m_nModelIndex", PropertyValue.FromInt(3)));

        entity.ModelScale().ShouldBeNull();
    }

    [Test]
    public void AnEntityWithNoDraw_IsHidden()
    {
        // **How a taken health pack disappears, and it is not by being deleted.** A pickup
        // respawns, so the server hides it: CTFPowerup::SetDisabled calls AddEffects(EF_NODRAW),
        // and EF_NODRAW is 0x020 in const.h. The entity keeps its position and keeps updating.
        //
        // A viewer that ignores this leaves a marker on the floor for the rest of the match at
        // every pickup anyone ever took - which is what the owner saw.
        EntityState entity = State(
            Property("DT_BaseEntity", "m_fEffects", PropertyValue.FromInt(0x020)));

        entity.IsDrawn.ShouldBeFalse();
    }

    [Test]
    public void AnEntityWithOtherEffects_IsStillDrawn()
    {
        // The control. m_fEffects is a bit field carrying a dozen unrelated flags - EF_BONEMERGE,
        // EF_DIMLIGHT, EF_NOSHADOW - and testing it for non-zero rather than for the one bit would
        // hide entities for reasons that have nothing to do with visibility.
        EntityState entity = State(
            Property("DT_BaseEntity", "m_fEffects", PropertyValue.FromInt(0x001 | 0x004)));

        entity.IsDrawn.ShouldBeTrue();
    }

    [Test]
    public void AnEntityThatSentNoEffects_IsDrawn()
    {
        // Absence is not concealment. Most entities never send the property at all.
        EntityState entity = State(Property("DT_BaseEntity", "m_nModelIndex", PropertyValue.FromInt(3)));

        entity.IsDrawn.ShouldBeTrue();
    }

    private static EntityState State(params DecodedProperty[] properties)
    {
        EntityStateTable table = new(EntityBaselines.None);

        table.Apply(
            new DecodedEntity(1, ClassId: 212, SerialNumber: 1, EntityUpdateType.Enter, properties));

        return table.All.First();
    }

    private static DecodedProperty Property(string table, string name, PropertyValue value) =>
        new(0, new FlatProperty(
                new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0),
                table,
                null),
            value);
}
