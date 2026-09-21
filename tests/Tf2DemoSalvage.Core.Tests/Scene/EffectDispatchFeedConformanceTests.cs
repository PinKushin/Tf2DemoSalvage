using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTEEffectDispatch` as `C_TEEffectDispatch` receives its `DT_EffectData` (`effect_dispatch_data.cpp:36`) (B415).</summary>
public sealed class EffectDispatchFeedConformanceTests
{
    [Test]
    public void Record_NothingSent_IsCEffectDatasConstructor()
    {
        // `m_flScale = 1.f` and `m_hEntity = INVALID_EHANDLE`; everything else zero.
        EffectDispatchFeed feed = new();

        feed.Record(EffectDispatchFeed.EventClassName, Effect(), 3).ShouldBeTrue();

        SceneEffectDispatch dispatch = feed.All[0];

        dispatch.Scale.ShouldBe(1f);
        dispatch.Entity.ShouldBe(-1);
        dispatch.SurfaceProp.ShouldBe(0);
    }

    [TestCase(1, 0)]
    [TestCase(0, -1)]
    [TestCase(30, 29)]
    public void Record_ASurfaceProp_IsTheSentValueLessOneAsAShort(int sent, int expected)
    {
        // `RecvProxy_ShortSubOne` (`recvproxy.cpp:33`): `*(short *)pOut = m_Int - 1`.
        EffectDispatchFeed feed = new();

        feed.Record(EffectDispatchFeed.EventClassName, Effect(Int("m_nSurfaceProp", sent)), 1);

        feed.All[0].SurfaceProp.ShouldBe(expected);
    }

    [Test]
    public void Record_AnImpact_ReadsItsNameOriginStartAndEntity()
    {
        EffectDispatchFeed feed = new();

        feed.Record(
            EffectDispatchFeed.EventClassName,
            Effect(
                Int("m_iEffectName", 4),
                Float("m_vOrigin[0]", 1f), Float("m_vOrigin[1]", 2f), Float("m_vOrigin[2]", 3f),
                Float("m_vStart[0]", 4f), Float("m_vStart[1]", 5f), Float("m_vStart[2]", 6f),
                Int("entindex", 0)),
            7);

        SceneEffectDispatch dispatch = feed.All[0];

        dispatch.Name.ShouldBe(4);
        dispatch.Origin.ShouldBe((1f, 2f, 3f));
        dispatch.Start.ShouldBe((4f, 5f, 6f));
        dispatch.Entity.ShouldBe(0, "the world");
    }

    [Test]
    public void Record_AnotherClass_IsNotADispatch()
    {
        new EffectDispatchFeed().Record("CTETFBlood", Effect(), 1).ShouldBeFalse();
    }

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) =>
        new(ClassId: 150, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) =>
        Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Float(string name, float value) =>
        Declared(name, SendPropType.Float, PropertyValue.FromFloat(value));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_EffectData", null),
            Value: value);
}
