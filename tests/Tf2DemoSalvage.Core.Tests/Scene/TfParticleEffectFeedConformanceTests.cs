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

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) => new(ClassId: 150, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(
                new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_TETFParticleEffect", null),
            Value: PropertyValue.FromInt(value));
}
