using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A track's identity fields answered at a tick, not at the demo's end (B548).</summary>
public sealed class PropFactsInTimeTests
{
    [Test]
    public void FactsAt_AcrossAModelChange_AnswersTheModelHeldThen()
    {
        ScenePropTrack track = Swapped(track => track.Follow("b.mdl"));

        track.FactsAt(5).ModelPath.ShouldBe("a.mdl");
        track.FactsAt(10).ModelPath.ShouldBe("b.mdl");
        track.ModelPath.ShouldBe("b.mdl", "the scalar stays the builder's latest");
    }

    [Test]
    public void HeldChangesAfter_AModelChangeWithNoNewPose_IsAWake()
    {
        // The pose is restated identically, so no keyframe marks tick 10: only the swap can wake the sampler.
        Swapped(track => track.Follow("b.mdl")).HeldChangesAfter(5).ShouldBe(10d);
    }

    [Test]
    public void ModelPaths_AcrossAModelChange_NamesBoth()
    {
        Swapped(track => track.Follow("b.mdl")).ModelPaths.ShouldBe(["a.mdl", "b.mdl"]);
    }

    /// <summary>Each identity field, changed at tick 10: tick 5 must answer the value before.</summary>
    private static IEnumerable<TestCaseData> Fields()
    {
        EconAttributeWire festive = new([new EconAttributeValue(2053, 1)], [], HasValidItemId: true);

        yield return Case("Team", track => track.TeamNumber = 3, facts => facts.TeamNumber);
        yield return Case("Parent", track => track.AttachedTo = 4, facts => facts.AttachedTo);
        yield return Case("Owner", track => track.OwnedBy = 4, facts => facts.OwnedBy);
        yield return Case("Item", track => track.ItemDefinitionIndex = 211, facts => facts.ItemDefinitionIndex);
        yield return Case("Econ", track => track.Econ = festive, facts => facts.Econ);
        yield return Case("Attachment", track => track.AttachmentPoint = 2, facts => facts.AttachmentPoint);
        yield return Case("BoneMerge", track => track.BoneMerged = true, facts => facts.BoneMerged);
        yield return Case("Disguise", track => track.OfDisguise = true, facts => facts.OfDisguise);
        yield return Case("ClientSideAnimation", track => track.ClientSideAnimated = true, facts => facts.ClientSideAnimated);
    }

    private static TestCaseData Case(string field, Action<ScenePropTrack> change, Func<ScenePropFacts, object?> read) =>
        new TestCaseData(change, read).SetName($"FactsAt_BeforeA{field}Change_AnswersTheEarlierValue");

    [TestCaseSource(nameof(Fields))]
    public void FactsAt_BeforeAFieldChange_AnswersTheEarlierValue(
        Action<ScenePropTrack> change, Func<ScenePropFacts, object?> read)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(read);

        ScenePropTrack track = Swapped(change);

        read(track.FactsAt(10)).ShouldNotBe(read(track.FactsAt(5)), "the control: the field did change");
        read(track.FactsAt(5)).ShouldBe(read(new ScenePropTrack(7, "a.mdl").FactsAt(0)));
    }

    [Test]
    public void FactsAt_OnAPlayerTrackWhoseTeamArrivesLater_AnswersTheTeamHeldThen()
    {
        // A PLAYER's update goes through the same builder path as a prop's (its model is "" not null, so the
        // model-or-item guard passes) and is stamped there. Team stated at 10, changed at 20.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoOfALateTeam(0.015f, (0, null), (10, 2), (20, 3)));

        ScenePropTrack player = timeline.PlayerTracks.ShouldHaveSingleItem();

        player.FactsAt(5).TeamNumber.ShouldBeNull();
        player.FactsAt(15).TeamNumber.ShouldBe(2);
        player.FactsAt(25).TeamNumber.ShouldBe(3);
    }

    [Test]
    public void Stamp_AnEconWireRebuiltWithTheSameValues_AddsNoEntry()
    {
        // `EconAttributeWire` compares by value (B234), and the builder makes a fresh one on every element update.
        ScenePropTrack track = new(7, "a.mdl");

        for (int tick = 0; tick < 3; tick++)
        {
            track.Econ = new EconAttributeWire([new EconAttributeValue(2053, 1)], [], HasValidItemId: true);
            track.Stamp(tick);
        }

        track.FactsHistory.Count().ShouldBe(1);
    }

    /// <summary>A track stamped at tick 0, then changed and stamped at tick 10, its pose the same object throughout.</summary>
    private static ScenePropTrack Swapped(Action<ScenePropTrack> change)
    {
        ScenePropTrack track = new(7, "a.mdl");
        ScenePose pose = new();

        track.Add(0, pose);
        track.Stamp(0);
        track.Add(10, pose);
        change(track);
        track.Stamp(10);

        return track;
    }
}
