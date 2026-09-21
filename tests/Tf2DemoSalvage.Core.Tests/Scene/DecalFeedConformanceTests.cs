using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The decal temp entities, as `c_te_worlddecal.cpp`, `c_te_decal.cpp` and `c_te_playerdecal.cpp` receive them (B415).</summary>
/// <remarks>
/// `DT_TEWorldDecal` is `m_vecOrigin, m_nIndex`; `DT_TEDecal` adds `m_vecStart, m_nEntity, m_nHitbox`; `DT_TEPlayerDecal`
/// is `m_vecOrigin, m_nEntity, m_nPlayer`. Every constructor zeroes every field, so an unsent one is zero.
/// </remarks>
public sealed class DecalFeedConformanceTests
{
    [Test]
    public void Record_AWorldDecal_ReadsItsOriginAndIndex()
    {
        DecalFeed feed = new();

        feed.Record(DecalFeed.WorldClassName, Effect(Vector("m_vecOrigin", (-2426f, -2053f, 776f)), Int("m_nIndex", 39)), 7)
            .ShouldBeTrue();

        feed.All[0].ShouldBe(new SceneDecal(7, SceneDecalKind.World, (-2426f, -2053f, 776f), (0f, 0f, 0f), 0, 0, 39, 0));
    }

    /// <remarks>f12's first `CTEDecal`: a ray from above onto the world with hitbox 1231 — a static prop, 1230.</remarks>
    [Test]
    public void Record_AnEntityDecal_ReadsTheRayEntityAndHitbox()
    {
        DecalFeed feed = new();

        feed.Record(
            DecalFeed.EntityClassName,
            Effect(
                Vector("m_vecOrigin", (-337.375f, -726.406f, 548.531f)),
                Vector("m_vecStart", (-337.375f, -726.406f, 573.313f)),
                Int("m_nHitbox", 1231),
                Int("m_nIndex", 39)),
            3);

        SceneDecal decal = feed.All[0];

        decal.Kind.ShouldBe(SceneDecalKind.Entity);
        decal.Start.ShouldBe((-337.375f, -726.406f, 573.313f));
        decal.Entity.ShouldBe(0, "unsent, so the world");
        decal.Hitbox.ShouldBe(1231);
    }

    [Test]
    public void Record_ASpray_ReadsItsPlayer()
    {
        DecalFeed feed = new();

        feed.Record(DecalFeed.PlayerClassName, Effect(Vector("m_vecOrigin", (1f, 2f, 3f)), Int("m_nPlayer", 5)), 1);

        feed.All[0].Kind.ShouldBe(SceneDecalKind.Player);
        feed.All[0].Player.ShouldBe(5);
    }

    [Test]
    public void Record_AnotherClass_IsNotADecal()
    {
        DecalFeed feed = new();

        feed.Record("CTETFExplosion", Effect(), 1).ShouldBeFalse();
        feed.All.Count.ShouldBe(0);
    }

    private static DecodedTempEntity Effect(params DecodedProperty[] properties) =>
        new(ClassId: 180, DelaySeconds: 0f, Properties: properties);

    private static DecodedProperty Int(string name, int value) =>
        Declared(name, SendPropType.Int, PropertyValue.FromInt(value));

    private static DecodedProperty Vector(string name, (float X, float Y, float Z) value) =>
        Declared(name, SendPropType.Vector, PropertyValue.FromVector(value.X, value.Y, value.Z));

    private static DecodedProperty Declared(string name, SendPropType type, PropertyValue value) =>
        new(
            Index: 0,
            Definition: new FlatProperty(new SendProperty(type, name, 0, string.Empty, 0f, 0f, 32, 0), "DT_TEDecal", null),
            Value: value);
}
