using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTESparks`, `CTEMetalSparks` and `CTEArmorRicochet` as their clients read them (B415).</summary>
public sealed class SparkFeedConformanceTests
{
    [Test]
    public void Record_Sparks_CarryTheirMagnitudeTrailAndDirection()
    {
        // `C_TESparks::PostDataUpdate`: `g_pEffects->Sparks( m_vecOrigin, m_nMagnitude, m_nTrailLength, &m_vecDir )`.
        SparkFeed feed = new();

        feed.Record(
            "CTESparks",
            Effect(
                Float("m_vecOrigin[0]", 1f), Float("m_vecOrigin[1]", 2f), Float("m_vecOrigin[2]", 3f),
                Int("m_nMagnitude", 2), Int("m_nTrailLength", 3), Vector("m_vecDir", (0f, 0f, 1f))),
            5).ShouldBeTrue();

        feed.All.ShouldHaveSingleItem().ShouldBe(new SceneSpark(5, SparkKind.Electric, (1f, 2f, 3f), (0f, 0f, 1f), 2, 3));
    }

    [TestCase("CTEMetalSparks", SparkKind.Metal)]
    [TestCase("CTEArmorRicochet", SparkKind.Ricochet)]
    public void Record_MetalSparksOrARicochet_IsItsPositionAndDirection(string className, SparkKind kind)
    {
        // Both are `DT_TEMetalSparks`' `m_vecPos` and `m_vecDir`; `C_TEArmorRicochet` is a `C_TEMetalSparks` that also
        // plays the ricochet sound.
        SparkFeed feed = new();

        feed.Record(className, Effect(Vector("m_vecPos", (4f, 5f, 6f)), Vector("m_vecDir", (1f, 0f, 0f))), 7).ShouldBeTrue();

        feed.All.ShouldHaveSingleItem().ShouldBe(new SceneSpark(7, kind, (4f, 5f, 6f), (1f, 0f, 0f), 0, 0));
    }

    [Test]
    public void Record_AnotherClass_IsNotOne()
    {
        new SparkFeed().Record("CTEDust", Effect(), 1).ShouldBeFalse();
    }

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) => new(ClassId: 150, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) => Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Float(string name, float value) => Declared(name, SendPropType.Float, PropertyValue.FromFloat(value));

    private static DecodedProperty Vector(string name, (float X, float Y, float Z) value) =>
        Declared(name, SendPropType.Vector, PropertyValue.FromVector(value.X, value.Y, value.Z));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_TE", null),
            Value: value);
}
