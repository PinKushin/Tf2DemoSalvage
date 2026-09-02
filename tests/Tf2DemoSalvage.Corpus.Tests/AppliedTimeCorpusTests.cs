using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// The applied-time correction fires on real demos, not only on fixtures.
/// </summary>
/// <remarks>
/// **A synthetic test proves the arithmetic; only a real demo proves it is REACHED** (B273). The
/// correction shifts a keyframe by the entity's simulation lag, and if no recording ever carried a
/// non-zero lag the whole mechanism would be a no-op that every unit test still passed —
/// `docs/memory/output-level-assertion-or-it-is-not-done.md`, and the exact shape of B268 and B269
/// earlier the same day.
///
/// **This is a corpus test rather than a synthetic one because the question is about the FILES**:
/// does a real TF2 server stamp an entity's simulation time away from the packet that carried it.
/// A fixture would only restate what its author believed.
/// </remarks>
public sealed class AppliedTimeCorpusTests
{
    [Test]
    public void SimulationLag_OnASourceTvRecording_IsNonZeroForRealEntities()
    {
        DemoTimeline timeline = TimelineCache.For(
            Corpus.Demo("tf2-2013-build1729296-stv-cp_foundry"));

        int lagged = 0;

        for (int bucket = 0; bucket < DemoTimeline.LagBuckets; bucket++)
        {
            if (bucket != DemoTimeline.LagZero)
            {
                lagged += timeline.SimulationLag(bucket);
            }
        }

        lagged.ShouldBeGreaterThan(
            0,
            "if no entity in a real recording ever simulated away from the packet that carried " +
            "it, the applied-time correction would be a no-op that every unit test still passes");

        // **The control for that count.** A histogram whose every update landed in one bucket
        // would satisfy the assertion above only if that bucket were not zero — so this pins the
        // other half: updates DO land on the packet's own tick too, which is what makes the lag a
        // difference between entities rather than a constant offset on all of them.
        timeline.SimulationLag(DemoTimeline.LagZero).ShouldBeGreaterThan(
            0, "some updates simulate on the packet's own tick, or this is a clock offset");
    }

    /// <remarks>
    /// **The one that fails when the STAMPING is severed, which the two above do not.** They assert
    /// on the histogram, which is measured beside the stamping rather than through it — a sabotage
    /// that dropped the lag from `track.Add` left both green. This reads the number the
    /// interpolation actually used, carried out of the track.
    ///
    /// That gap was found by sabotage rather than by reading, which is the point of running one:
    /// two tests that look like they cover a change can both be measuring something adjacent to it.
    /// </remarks>
    [Test]
    public void Keyframes_OnASourceTvRecording_CarryAnAppliedTimeAwayFromTheirArrival()
    {
        DemoTimeline timeline = TimelineCache.For(
            Corpus.Demo("tf2-2013-build1729296-stv-cp_foundry"));

        int corrected = 0;
        int total = 0;

        foreach (ScenePropTrack track in timeline.Props.Concat(timeline.PlayerTracks))
        {
            for (int index = 0; index < track.Keyframes.Count; index++)
            {
                total++;

                if (track.AppliedAt(index) != track.Keyframes[index].Tick)
                {
                    corrected++;
                }
            }
        }

        total.ShouldBeGreaterThan(0, "the demo produced no keyframes at all");

        corrected.ShouldBeGreaterThan(
            0,
            "no keyframe on a real recording was stamped away from the packet that carried it, so " +
            "the applied-time correction is reaching nothing");
    }

    /// <remarks>
    /// **Players are the finding.** Everything else in these recordings that lags is a prop at
    /// rest, whose stale timestamp costs nothing because it is not moving. `CTFPlayer` splits
    /// between two clusters four ticks apart, which is 60 ms of jitter on the fastest things on
    /// screen — and it is per-update rather than per-entity, so it cannot be dismissed as a clock
    /// offset that moves the whole scene together.
    /// </remarks>
    [Test]
    public void SimulationLag_ForPlayers_SplitsBetweenTwoClusters()
    {
        DemoTimeline timeline = TimelineCache.For(
            Corpus.Demo("tf2-2013-build1729296-stv-cp_foundry"));

        timeline.SimulationLagByClass.ShouldContainKey("CTFPlayer");

        int[] counts = timeline.SimulationLagByClass["CTFPlayer"];

        counts.Count(count => count > 0).ShouldBe(
            2,
            "a player's simulation tick sits in exactly two places relative to the packet, and " +
            "one of them would mean a constant offset rather than jitter");
    }
}
