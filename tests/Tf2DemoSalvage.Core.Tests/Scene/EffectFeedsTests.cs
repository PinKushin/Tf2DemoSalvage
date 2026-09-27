using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`EffectFeeds.Record`: each temp entity class reaches exactly its own feed, and one no feed knows reaches none.</summary>
public sealed class EffectFeedsTests
{
    [TestCase(ExplosionFeed.EventClassName, 0)]
    [TestCase(ShotFeed.EventClassName, 1)]
    [TestCase(DecalFeed.WorldClassName, 2)]
    [TestCase(BloodFeed.EventClassName, 3)]
    [TestCase(EffectDispatchFeed.EventClassName, 4)]
    [TestCase(TfParticleEffectFeed.EventClassName, 5)]
    [TestCase("CTEMetalSparks", 6)]
    public void Record_EachFeedsClass_LandsInThatFeedAlone(string className, int feed)
    {
        EffectFeeds feeds = new();

        feeds.Record(className, Effect(), 10, _ => false, _ => null).ShouldBeTrue();

        int[] expected = new int[7];
        expected[feed] = 1;
        Counts(feeds).ShouldBe(expected);
    }

    [Test]
    public void Record_AClassNoFeedKnows_IsNotTaken()
    {
        EffectFeeds feeds = new();

        feeds.Record("CTEPlayerAnimEvent", Effect(), 10, _ => false, _ => null).ShouldBeFalse();

        Counts(feeds).ShouldBe(new int[7]);
    }

    private static int[] Counts(EffectFeeds feeds) =>
    [
        feeds.Explosions.All.Count, feeds.Shots.All.Count, feeds.Decals.All.Count, feeds.Blood.All.Count,
        feeds.Dispatches.All.Count, feeds.TfParticleEffects.All.Count, feeds.Sparks.All.Count,
    ];

    private static DecodedTempEntity Effect() => new(ClassId: 1, DelaySeconds: 0f, Properties: []);
}
