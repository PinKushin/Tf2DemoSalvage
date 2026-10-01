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
    /// <summary>A modern match with a full roster in worn items: the f12 parity reference.</summary>
    /// <remarks>The 2026-08-08-2207 SourceTV recording since 2026-09-30, when the 2026-08-07 one was lost.</remarks>
    private const string F12Recording = "demostf-cp_process_f12-2026-08-08-2207";

    [Test]
    public void WearableTracks_Cosmetics_NameTheirWearer()
    {
        // **A modern match, not an era specimen.** The committed demos are the owner's own solo
        // recordings, so they carry no other players and no worn items at all — measured: 11 props
        // and zero wearables in the 2013 badlands POV. A test pointed there would pass while
        // measuring nothing, so this one names the demo it needs and skips when it is absent.
        //
        // **Named in full, because a fragment moved.** "cp_process" found this f12 recording, which
        // every number below was measured on, until 2026-08-18 — when
        // `20150119_2240_cp_process_final_(ovo)_blu` joined lcor and, digits sorting first, silently
        // became the subject instead.
        string path = Corpus.Demo(F12Recording);

        DemoTimeline timeline = TimelineCache.For(path);

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

        // Twelve players wearing two or three items each. 43 attached props are present at this
        // file's midpoint, tick 48315 (measured 2026-10-01 by this test's own WEAR line; the lost
        // 2026-08-07 recording had 37), so twenty is a floor well under the measurement rather than
        // a restatement of it.
        attached.Length.ShouldBeGreaterThan(
            20,
            "cp_process f12 has 43 attached props at its midpoint tick");

        // **The control: an attached prop must not also claim a place in the world.** A cosmetic
        // recorded with a pose of its own would draw at the map origin, in a heap, which is the
        // failure this whole mechanism exists to avoid.
        foreach (SceneProp prop in attached)
        {
            prop.Pose.Hidden.ShouldBeFalse();

            // **A model named on the wire OR by the item, not necessarily on the track** (B263,
            // c21d81c4). A worn item's model is its item's `model_player` — `CEconEntity::SetModel`
            // asking `GetPlayerDisplayModel` — so the wire need not carry one: the track leaves the
            // timeline with an empty path and `WeaponPropModels.Resolve` names it at draw time. This
            // demanded a wire path, which B263 later showed a cosmetic need not have.
            (prop.ModelPath.Length > 0 || prop.ItemDefinitionIndex is not null).ShouldBeTrue(
                $"entity {prop.EntityIndex} is attached and names no model, on the wire or by its item");
        }

        // Every wearer is a player present at the same tick, not a stale handle.
        //
        // **Except a `CTFWearableVM`, whose wearer is a VIEWMODEL** (`IsViewModelWearable`,
        // `tf_item_wearable.cpp:58`). The 2026-08-08-2207 recording showed it: at tick 48315 the
        // demoman on entity 4 wears `fob_h_stickybomb_diamond.mdl` twice — a `CTFWearable` (364) on
        // entity 4 and a `CTFWearableVM` (362) on entity 53, his viewmodel. This said "every" while it
        // ran on the lost 2026-08-07 recording, whose midpoint it passed on.
        int[] players = [.. timeline.PlayersAt(tick).Select(player => player.EntityIndex)];

        attached.Where(prop => prop.ClassName != "CTFWearableVM")
            .Select(prop => prop.AttachedTo!.Value)
            .Distinct()
            .ShouldAllBe(owner => players.Contains(owner));
    }

    [Test]
    public void WearableTracks_OrdinaryProps_NameNoWearer()
    {
        // **The bystander.** A health pack stands at its own origin, and a change that gave every
        // prop an owner would pass the test above while breaking the entire map.
        // **A modern match, not an era specimen.** The committed demos are the owner's own solo
        // recordings, so they carry no other players and no worn items at all — measured: 11 props
        // and zero wearables in the 2013 badlands POV. A test pointed there would pass while
        // measuring nothing, so this one names the demo it needs and skips when it is absent.
        string path = Corpus.Demo(F12Recording);

        DemoTimeline timeline = TimelineCache.For(path);

        int tick = timeline.Frames[timeline.Frames.Count / 2].Tick;

        List<SceneProp> now = [];

        timeline.PropsAt(tick, now);

        now.Count(prop =>
            prop.AttachedTo is null &&
            prop.ModelPath.Contains("medkit", StringComparison.OrdinalIgnoreCase))
            .ShouldBeGreaterThan(0, "medkits stand in the map on their own origins");
    }
}
