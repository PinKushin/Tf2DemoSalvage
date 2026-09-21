using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTETFBlood` as `C_TETFBlood` receives it (`tf_fx_blood.cpp:171`) (B415).</summary>
public sealed class BloodFeedConformanceTests
{
    [Test]
    public void Record_ABloodEvent_ReadsItsOriginNormalAndPlayer()
    {
        BloodFeed feed = new();

        feed.Record(
            BloodFeed.EventClassName,
            Effect(Float("m_vecOrigin[0]", 1f), Float("m_vecOrigin[1]", 2f), Float("m_vecOrigin[2]", 3f),
                Vector("m_vecNormal", (0f, 1f, 0f)), Int("entindex", 4)),
            9,
            static index => index == 4)
            .ShouldBeTrue();

        feed.All[0].ShouldBe(new SceneBlood(9, (1f, 2f, 3f), (0f, 1f, 0f), 4, true));
    }

    [Test]
    public void Record_ANegativeEntity_IsNoEntity()
    {
        // `RecvProxy_BloodEntIndex`: `nEntIndex < 0 ? INVALID_EHANDLE : …`.
        BloodFeed feed = new();

        feed.Record(BloodFeed.EventClassName, Effect(Int("entindex", -1)), 1, static _ => true);

        feed.All[0].Entity.ShouldBe(-1);
        feed.All[0].IsPlayer.ShouldBeFalse();
    }

    [Test]
    public void Record_AnotherClass_IsNotBlood()
    {
        new BloodFeed().Record("CTETFExplosion", Effect(), 1, static _ => true).ShouldBeFalse();
    }

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) =>
        new(ClassId: 170, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) =>
        Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Float(string name, float value) =>
        Declared(name, SendPropType.Float, PropertyValue.FromFloat(value));

    private static DecodedProperty Vector(string name, (float X, float Y, float Z) value) =>
        Declared(name, SendPropType.Vector, PropertyValue.FromVector(value.X, value.Y, value.Z));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_TETFBlood", null),
            Value: value);
}
