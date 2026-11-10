using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary><c>C_Sprite</c>'s client-side scale and brightness ramp (`Sprite.cpp:644-743`).</summary>
public sealed class SpriteRampConformanceTests
{
    /// <remarks><c>DATA_UPDATE_CREATED</c> sets both ends to the first value (`Sprite.cpp:716-720`).</remarks>
    [Test]
    public void Created_AFirstValue_IsBothEnds()
    {
        SpriteRamp.Created(0.5f).ShouldBe(new SpriteRamp(0.5f, 0.5f, 0d));
    }

    /// <remarks>
    /// <c>ClientThink</c> moves the old destination to the start and stamps the time (`Sprite.cpp:730-735`). A second
    /// change mid-ramp starts from the old destination, 2, not from the value drawn then.
    /// </remarks>
    [Test]
    public void Think_TwoChanges_StartFromTheLastDestination()
    {
        SpriteRamp ramp = SpriteRamp.Created(1f).Think(2f, 10d).Think(4f, 10.25d);

        ramp.ShouldBe(new SpriteRamp(2f, 4f, 10.25d));
    }

    /// <remarks>An unchanged value keeps the ramp, its time included.</remarks>
    [Test]
    public void Think_TheSameValue_KeepsTheRamp()
    {
        SpriteRamp ramp = SpriteRamp.Created(1f).Think(2f, 10d);

        ramp.Think(2f, 11d).ShouldBe(ramp);
    }

    /// <remarks>
    /// <c>GetRenderScale</c>: a quarter of the way through a 2-second ramp from 2 to 4 is 2.5 (`Sprite.cpp:651-654`).
    /// </remarks>
    [Test]
    public void At_AQuarterThroughTheRamp_IsAQuarterOfTheWay()
    {
        new SpriteRamp(2f, 4f, 10d).At(4f, 2f, 10.5d).ShouldBe(2.5f);
    }

    /// <remarks>
    /// The two early answers (`Sprite.cpp:647-648`): an untimed ramp, and one whose end has passed, answer the networked
    /// value, 7 here and not the destination.
    /// </remarks>
    [TestCase(0f, 10.5d)]
    [TestCase(2f, 12.001d)]
    public void At_UntimedOrFinished_IsTheNetworkedValue(float rampSeconds, double now)
    {
        new SpriteRamp(2f, 4f, 10d).At(7f, rampSeconds, now).ShouldBe(7f);
    }

    /// <remarks>
    /// **The track keeps both ramps across updates** (`Sprite.cpp:716-743`): created at the first update's 1 and 255, then a
    /// change of scale to 3 at 20 s ramps from 1, and the unchanged brightness keeps its creation. The four <c>DT_Sprite</c>
    /// fields the pose carries are read from their own table (`Sprite.cpp:133-135`, `:152`).
    /// </remarks>
    [Test]
    public void ThinkSprite_AcrossTwoUpdates_CreatesThenRampsFromTheOldValue()
    {
        const string Table = "DT_Sprite";
        EntityState glow = new(12, 0, 0, "CSprite");
        ScenePropTrack track = new(12, "sprites/glow01.vmt");

        glow.Set($"{Table}.m_flSpriteScale", PropertyValue.FromFloat(1f));
        glow.Set($"{Table}.m_nBrightness", PropertyValue.FromInt(255));
        track.ThinkSprite(glow, 10d);

        glow.Set($"{Table}.m_flSpriteScale", PropertyValue.FromFloat(3f));
        glow.Set($"{Table}.m_flScaleTime", PropertyValue.FromFloat(0.5f));
        glow.Set($"{Table}.m_flBrightnessTime", PropertyValue.FromFloat(2f));
        glow.Set($"{Table}.m_hAttachedToEntity", PropertyValue.FromInt(77));
        glow.Set($"{Table}.m_nAttachment", PropertyValue.FromInt(2));
        track.ThinkSprite(glow, 20d);

        track.SpriteScaleRamp.ShouldBe(new SpriteRamp(1f, 3f, 20d));
        track.SpriteBrightnessRamp.ShouldBe(new SpriteRamp(255f, 255f, 0d));
        (glow.SpriteScaleTime(), glow.SpriteBrightnessTime(), glow.SpriteAttachedTo(), glow.SpriteAttachment())
            .ShouldBe((0.5f, 2f, 77, 2));
    }

    /// <remarks>The end is not yet passed at exactly start plus time (<c>&lt;</c>, not <c>&lt;=</c>): the lerp reads 4.</remarks>
    [Test]
    public void At_ExactlyTheEnd_IsStillTheLerp()
    {
        new SpriteRamp(2f, 4f, 10d).At(7f, 2f, 12d).ShouldBe(4f);
    }
}
