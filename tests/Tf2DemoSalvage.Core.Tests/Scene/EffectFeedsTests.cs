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

    /// <remarks>
    /// **`CL_FireEvents` fires the queue in the order it was filled** (engine.dll FUN_1800905d0 walks the event list,
    /// gated only by the signon state and each event's fire delay) — so a temp entity's place among ALL of them, the ones
    /// no feed takes included, is the order its sounds are emitted in on one frame (B505).
    /// </remarks>
    [Test]
    public void Record_ThreeEffectsAndOneNoFeedTakes_StampsEachTakenWithItsPlaceInTheStream()
    {
        EffectFeeds feeds = new();

        feeds.Record(ExplosionFeed.EventClassName, Effect(), 10, _ => false, _ => null);
        feeds.Record("CTEPlayerAnimEvent", Effect(), 10, _ => false, _ => null);
        feeds.Record(ShotFeed.EventClassName, Effect(), 10, _ => false, _ => null);
        feeds.Record(EffectDispatchFeed.EventClassName, Effect(), 10, _ => false, _ => null);

        (feeds.Explosions.All[0].TempEntity, feeds.Shots.All[0].TempEntity, feeds.Dispatches.All[0].TempEntity).ShouldBe((1, 3, 4));
    }

    /// <remarks>A zero count is one effect sent reliably (`DecodeTempEntities`), which a demo skip still queues (B504).</remarks>
    [Test]
    public void Record_AReliableEffect_IsMarkedReliable()
    {
        EffectFeeds feeds = new();

        feeds.Record(ExplosionFeed.EventClassName, Effect(), 10, _ => false, _ => null, reliable: true);
        feeds.Record(ShotFeed.EventClassName, Effect(), 10, _ => false, _ => null, reliable: false);
        feeds.Record(EffectDispatchFeed.EventClassName, Effect(), 10, _ => false, _ => null, reliable: true);

        (feeds.Explosions.All[0].Reliable, feeds.Shots.All[0].Reliable, feeds.Dispatches.All[0].Reliable).ShouldBe((true, false, true));
    }

    private static int[] Counts(EffectFeeds feeds) =>
    [
        feeds.Explosions.All.Count, feeds.Shots.All.Count, feeds.Decals.All.Count, feeds.Blood.All.Count,
        feeds.Dispatches.All.Count, feeds.TfParticleEffects.All.Count, feeds.Sparks.All.Count,
    ];

    private static DecodedTempEntity Effect() => new(ClassId: 1, DelaySeconds: 0f, Properties: []);
}
