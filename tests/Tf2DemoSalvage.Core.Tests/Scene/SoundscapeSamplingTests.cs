using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>Whose soundscape the timeline samples, and how it stores it (D217, B544).</summary>
/// <remarks>
/// **The local player's, and nobody else's** (B544): `C_BasePlayer::OnDataChanged` calls `Soundscape_Update(
/// m_Local.m_audio )` only `if ( IsLocalPlayer() )` (`c_baseplayer.cpp:1025-1051`). A SourceTV demo carries `m_audio` on
/// several players (f12: 1,055,037 samples for 96,589 frames, D217), and the sampler used to keep whichever the entity
/// walk reached last. The local player is the recording's own slot plus one — the SourceTV client's own entity on a
/// SourceTV demo ([[sourcetv-has-a-local-player]]); the fixture's `ServerInfo` names slot 0, so entity 1.
/// </remarks>
public sealed class SoundscapeSamplingTests
{
    /// <remarks>
    /// Two players at soundscapes 5 and 7, held for three ticks. Entity 1 is the local player: 5 at every tick, where the
    /// walk's last (entity 2) gave 7 before B544.
    /// </remarks>
    [Test]
    public void SoundscapeAt_TwoPlayersHoldingDifferentSoundscapes_AnswersTheLocalPlayersAtEveryTick()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        new[] { 10, 11, 12 }.Select(tick => timeline.SoundscapeAt(tick)?.Index).ShouldBe([5, 5, 5]);
    }

    /// <remarks>
    /// **The control on the walk order**: the local player holds the HIGHER index here, so neither "first walked" nor
    /// "last walked" can pass both this and the case above.
    /// </remarks>
    [Test]
    public void SoundscapeAt_TheLocalPlayerHoldingTheHigherIndex_AnswersHis()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 9), (2, 3)));

        timeline.SoundscapeAt(11)?.Index.ShouldBe(9);
    }

    /// <remarks>
    /// **Nobody else's ears**: the local player carries no `m_audio` here, only another player does, and the engine
    /// updates the soundscape from nobody — no sample.
    /// </remarks>
    [Test]
    public void SoundscapeAt_OnlyAnotherPlayerCarryingOne_IsNull()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (2, 7)));

        timeline.SoundscapeAt(11).ShouldBeNull();
    }

    /// <remarks>
    /// **The storage itself**: one sample, not one per player per tick. Before D217 this was six — (5, 7) at each of
    /// three ticks, since each differed from the one stored before it.
    /// </remarks>
    [Test]
    public void Soundscapes_TwoPlayersHoldingDifferentSoundscapes_StoreOneSample()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        timeline.Soundscapes.Select(sample => (sample.Tick, sample.Soundscape.Index)).ShouldBe([(10, 5)]);
    }

    /// <remarks>The control: before the first sample's tick there is none, so the lookup is not answering a constant.</remarks>
    [Test]
    public void SoundscapeAt_BeforeTheFirstSample_IsNull()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfSoundscapes([10, 11, 12], (1, 5), (2, 7)));

        timeline.SoundscapeAt(9).ShouldBeNull();
    }
}
