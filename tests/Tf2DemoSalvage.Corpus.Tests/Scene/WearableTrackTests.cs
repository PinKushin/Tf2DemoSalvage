using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Cosmetics reach the timeline even though they carry no position.
/// </summary>
/// <remarks>
/// A <c>CTFWearable</c> — a hat, a badge, a medal — sends a model index, an owner, a skin and a
/// team, and no origin at all. That is not an omission: <c>FollowEntity</c> sets
/// <c>EF_BONEMERGE</c> and zeroes local origin and angles, because a merged entity takes its
/// parent's bone matrices by name rather than transforming from a position of its own
/// (<c>shared/baseentity_shared.cpp:2360</c>, <c>public/const.h:284</c>).
///
/// So the timeline's "no origin means nothing to draw" rule drops every cosmetic in every demo.
/// Full account in <c>docs/findings/22-bone-merged-attachments.md</c>.
/// </remarks>
public sealed class WearableTrackTests
{
    [Test]
    public void CosmeticsAreRecordedAndNameTheirWearer()
    {
        string path = Corpus.FilesWithSchema()
            .First(file => Path.GetFileName(file).Contains("cp_process", StringComparison.Ordinal));

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // **The demo's own midpoint, never a round number.** This file's first packet is already
        // past tick 20000, so a hardcoded tick walks nothing and reports an empty world with no
        // error — measured, and it drove a wrong conclusion for a round of work.
        int tick = timeline.Frames[timeline.Frames.Count / 2].Tick;

        List<SceneProp> now = [];

        timeline.PropsAt(tick, now);

        SceneProp[] attached = [.. now.Where(prop => prop.AttachedTo is not null)];

        // **Tracks against props, because the two failures look identical from the count alone.**
        // "Few tracks were ever attached" is a recording problem and "many tracks exist but few
        // answer at this tick" is a presence problem, and a bare 3-of-37 cannot tell them apart.
        TestContext.Out.WriteLine(
            $"WEAR {timeline.Props.Count(track => track.AttachedTo is not null)} attached tracks " +
            $"in the whole demo, {attached.Length} of them present at tick {tick}, " +
            $"out of {now.Count} props");

        ScenePropTrack[] attachedTracks = [.. timeline.Props.Where(track => track.AttachedTo is not null)];

        TestContext.Out.WriteLine(
            $"WEAR of those tracks: {attachedTracks.Count(track => track.At(tick) is not null)} " +
            $"answer at the tick, {attachedTracks.Count(track => track.At(tick) is { Hidden: true })} " +
            $"answer hidden, {attachedTracks.Count(track => track.FirstTick <= tick)} started by then");

        TestContext.Out.WriteLine(
            "WEAR attached models: " + string.Join(
                ", ",
                attachedTracks
                    .GroupBy(track => Path.GetFileName(track.ModelPath))
                    .OrderByDescending(group => group.Count())
                    .Take(10)
                    .Select(group => $"{group.Count()}x {group.Key}")));

        // Twelve players wearing two or three items each. The probe counted 37 live CTFWearable
        // entities at this file's midpoint, so twenty is a floor well under the measurement rather
        // than a restatement of it.
        attached.Length.ShouldBeGreaterThan(
            20,
            "cp_process has 37 live wearables at its midpoint tick");

        // **The control: an attached prop must not also claim a place in the world.** A cosmetic
        // recorded with a pose of its own would draw at the map origin, in a heap, which is the
        // failure this whole mechanism exists to avoid.
        foreach (SceneProp prop in attached)
        {
            prop.Pose.Hidden.ShouldBeFalse();
            prop.ModelPath.ShouldNotBe(string.Empty);
        }

        // Every wearer is a player present at the same tick, not a stale handle.
        int[] players = [.. timeline.PlayersAt(tick).Select(player => player.EntityIndex)];

        attached.Select(prop => prop.AttachedTo!.Value)
            .Distinct()
            .ShouldAllBe(owner => players.Contains(owner));
    }

    [Test]
    public void OrdinaryPropsNameNoWearer()
    {
        // **The bystander.** A health pack stands at its own origin, and a change that gave every
        // prop an owner would pass the test above while breaking the entire map.
        string path = Corpus.FilesWithSchema()
            .First(file => Path.GetFileName(file).Contains("cp_process", StringComparison.Ordinal));

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        int tick = timeline.Frames[timeline.Frames.Count / 2].Tick;

        List<SceneProp> now = [];

        timeline.PropsAt(tick, now);

        now.Count(prop =>
            prop.AttachedTo is null &&
            prop.ModelPath.Contains("medkit", StringComparison.OrdinalIgnoreCase))
            .ShouldBeGreaterThan(0, "medkits stand in the map on their own origins");
    }
}
