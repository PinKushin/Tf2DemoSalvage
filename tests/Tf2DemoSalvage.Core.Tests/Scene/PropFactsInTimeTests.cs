using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A track's identity fields answered at a tick, not at the demo's end (B548).</summary>
public sealed class PropFactsInTimeTests
{
    [Test]
    public void FactsAt_AcrossAModelChange_AnswersTheModelHeldThen()
    {
        ScenePropTrack track = Swapped();

        track.FactsAt(5).ModelPath.ShouldBe("a.mdl");
        track.FactsAt(10).ModelPath.ShouldBe("b.mdl");
        track.ModelPath.ShouldBe("b.mdl", "the scalar stays the builder's latest");
    }

    [Test]
    public void HeldChangesAfter_AModelChangeWithNoNewPose_IsAWake()
    {
        // The pose is restated identically, so no keyframe marks tick 10: only the swap can wake the sampler.
        Swapped().HeldChangesAfter(5).ShouldBe(10d);
    }

    [Test]
    public void ModelPaths_AcrossAModelChange_NamesBoth()
    {
        Swapped().ModelPaths.ShouldBe(["a.mdl", "b.mdl"]);
    }

    /// <summary>A track whose model changes at tick 10 while its pose stays the same object.</summary>
    private static ScenePropTrack Swapped()
    {
        ScenePropTrack track = new(7, "a.mdl");
        ScenePose pose = new();

        track.Add(0, pose);
        track.Stamp(0);
        track.Add(10, pose);
        track.Follow("b.mdl");
        track.Stamp(10);

        return track;
    }
}
