using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>How the timeline stores the soundscape samples `SoundscapeAt` reads (D217).</summary>
/// <remarks>
/// **Found by measurement, not by a defect anyone saw**: f12, a SourceTV demo, held 1,055,037 soundscape samples for
/// 96,589 frames — 281 MB of a 2,948 MB timeline — because several players carry `m_audio` and the sampler, comparing
/// each to the one before, met a different value at every entity it walked. `SoundscapeAt` returns the last sample at or
/// before a tick, so only a tick's LAST sample can ever be answered; the rest were stored and never read.
/// </remarks>
public sealed class SoundscapeSamplingTests
{
    /// <remarks>
    /// Two players at soundscapes 5 and 7, held for three ticks. Walked in entity order, 7 is the last each tick — the
    /// value `SoundscapeAt` answered before this change — so it must still be what every tick answers.
    /// </remarks>
    [Test]
    public void SoundscapeAt_TwoPlayersHoldingDifferentSoundscapes_AnswersTheLastWalkedAtEveryTick()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        new[] { 10, 11, 12 }.Select(tick => timeline.SoundscapeAt(tick)?.Index).ShouldBe([7, 7, 7]);
    }

    /// <remarks>
    /// **The storage itself**: one sample, not one per player per tick. Before D217 this was six — (5, 7) at each of
    /// three ticks, since each differed from the one stored before it.
    /// </remarks>
    [Test]
    public void Soundscapes_TwoPlayersHoldingDifferentSoundscapes_StoreOneSample()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        timeline.Soundscapes.Select(sample => (sample.Tick, sample.Soundscape.Index)).ShouldBe([(10, 7)]);
    }

    /// <remarks>The control: before the first sample's tick there is none, so the lookup is not answering a constant.</remarks>
    [Test]
    public void SoundscapeAt_BeforeTheFirstSample_IsNull()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        timeline.SoundscapeAt(9).ShouldBeNull();
    }
}
