using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_TETFParticleEffect::PostDataUpdate` (`tf_fx_particleeffect.cpp:79`): a `"ParticleEffect"` dispatch (B415).</summary>
public sealed class TfParticleEffectFeedConformanceTests
{
    [Test]
    public void Record_AnEffectOnAnEntity_IsADispatchFromThatEntity()
    {
        TfParticleEffectFeed feed = new();

        feed.Record(
            TfParticleEffectFeed.EventClassName,
            Effect(
                Int("m_iParticleSystemIndex", 42),
                Int("entindex", 7),
                Int("m_iAttachType", 4),
                Int("m_iAttachmentPointIndex", 3),
                Int("m_bResetParticles", 1)),
            9).ShouldBeTrue();

        SceneEffectDispatch dispatch = feed.All.ShouldHaveSingleItem();

        dispatch.Tick.ShouldBe(9);
        dispatch.HitBox.ShouldBe(42, "`data.m_nHitBox = m_iParticleSystemIndex`");
        dispatch.Entity.ShouldBe(7);
        dispatch.Flags.ShouldBe(TfParticleEffectFeed.FromEntity | TfParticleEffectFeed.ResetParticles);
        dispatch.DamageType.ShouldBe(4, "`data.m_nDamageType = m_iAttachType`");
        dispatch.Attachment.ShouldBe(3);
    }

    [TestCase(2047)]
    [TestCase(-1)]
    public void Record_TheInvalidEntity_IsNotFromAnEntity(int invalid)
    {
        // `kInvalidEHandleParticleEffect` is 2047, and old demos wrote −1 (`RecvProxy_ParticleSystemEntIndex`).
        TfParticleEffectFeed feed = new();

        feed.Record(TfParticleEffectFeed.EventClassName, Effect(Int("entindex", invalid)), 1);

        (feed.All[0].Flags & TfParticleEffectFeed.FromEntity).ShouldBe(0);
    }

    [Test]
    public void Record_AnotherClass_IsNotOne()
    {
        new TfParticleEffectFeed().Record("CTEEffectDispatch", Effect(), 1).ShouldBeFalse();
    }

    [Test]
    public void Record_EveryField_IsReadIntoItsDispatchSlot()
    {
        // `DT_TETFParticleEffect` (`tf_fx.cpp`): each field lands where the dispatch it becomes keeps it, and a name the
        // table does not declare is ignored.
        TfParticleEffectFeed feed = new();

        feed.Record(
            TfParticleEffectFeed.EventClassName,
            Effect(
                Float("m_vecOrigin[0]", 1f), Float("m_vecOrigin[1]", 2f), Float("m_vecOrigin[2]", 3f),
                Float("m_vecStart[0]", 4f), Float("m_vecStart[1]", 5f), Float("m_vecStart[2]", 6f),
                Vector("m_vecAngles", (7f, 8f, 9f)),
                Int("m_iParticleSystemIndex", 11),
                Int("m_iAttachType", 2),
                Int("m_iAttachmentPointIndex", 3),
                Int("m_bResetParticles", 1),
                Int("m_bCustomColors", 1),
                Vector("m_CustomColors.m_vecColor1", (1f, 0f, 0f)),
                Vector("m_CustomColors.m_vecColor2", (0f, 1f, 0f)),
                Int("m_bControlPoint1", 1),
                Float("m_ControlPoint1.m_vecOffset[0]", 10f),
                Float("m_ControlPoint1.m_vecOffset[1]", 20f),
                Float("m_ControlPoint1.m_vecOffset[2]", 30f),
                Int("m_nNotAField", 99)),
            5);

        SceneEffectDispatch dispatch = feed.All[0];

        dispatch.Origin.ShouldBe((1f, 2f, 3f));
        dispatch.Start.ShouldBe((4f, 5f, 6f));
        dispatch.Angles.ShouldBe((7f, 8f, 9f));
        dispatch.HitBox.ShouldBe(11, "the particle system index");
        dispatch.DamageType.ShouldBe(2, "the attach type");
        dispatch.Attachment.ShouldBe(3);
        dispatch.Flags.ShouldBe(
            TfParticleEffectFeed.FromEntity | TfParticleEffectFeed.ResetParticles,
            "an unsent entindex is 0, which is neither kInvalidEHandleParticleEffect nor -1");
        dispatch.CustomColours.ShouldBeTrue();
        dispatch.ColourOne.ShouldBe((1f, 0f, 0f));
        dispatch.ColourTwo.ShouldBe((0f, 1f, 0f));
        dispatch.HasControlPoint1.ShouldBeTrue();
        dispatch.ControlPoint1.ShouldBe((10f, 20f, 30f));
    }

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) => new(ClassId: 150, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) => Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Float(string name, float value) =>
        Declared(name, SendPropType.Float, PropertyValue.FromFloat(value));

    private static DecodedProperty Vector(string name, (float X, float Y, float Z) value) =>
        Declared(name, SendPropType.Vector, PropertyValue.FromVector(value.X, value.Y, value.Z));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(
                new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_TETFParticleEffect", null),
            Value: value);
}
