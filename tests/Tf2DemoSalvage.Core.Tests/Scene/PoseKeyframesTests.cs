using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A track's keyframes store the moving half inline and share the rest with the keyframe before (D217); a reader must
/// get back exactly the values it stored, and the repeat test must keep the answers it always gave.
/// </summary>
/// <remarks>
/// **Why it exists**: one 336-byte `ScenePose` per keyframe was 856 MB of f12's timeline, while `prop-churn` measured
/// nothing but position and angles changing on more than 1.3% of keyframes.
/// </remarks>
public sealed class PoseKeyframesTests
{
    private static ScenePose Pose(float x, int sequence, params float[] parameters) =>
        new() { X = x, Y = 2f, Z = 3f, Yaw = 90f, Sequence = sequence, Cycle = 0.25f, PoseParameters = parameters };

    /// <remarks>**The sharing**: an equal rest is handed over by reference, lists included, with the position kept.</remarks>
    [Test]
    public void Keyframes_RestEqualByValueOnly_SharesTheEarlierListAndKeepsEachPosition()
    {
        ScenePropTrack track = new(1, "models/a.mdl");

        track.Add(10, Pose(1f, 4, 0.5f, 0.75f));
        track.Add(11, Pose(5f, 4, 0.5f, 0.75f));

        (int _, ScenePose first) = track.Keyframes[0];
        (int tick, ScenePose second) = track.Keyframes[1];

        (tick, first.X, second.X, second.Y, second.Yaw, second.Sequence).ShouldBe((11, 1f, 5f, 2f, 90f, 4));
        second.PoseParameters.ShouldBeSameAs(first.PoseParameters);
        second.PoseParameters.ShouldBe([0.5f, 0.75f]);
    }

    /// <remarks>**No sharing across a change**: a rest that differs anywhere reads back as its own.</remarks>
    [Test]
    public void Keyframes_RestDiffersInOneListValue_ReadsBackItsOwnValues()
    {
        ScenePropTrack track = new(1, "models/a.mdl");

        track.Add(10, Pose(1f, 4, 0.5f));
        track.Add(11, Pose(1f, 4, 0.6f));
        track.Add(12, Pose(1f, 7, 0.6f));

        track.Keyframes.Select(keyframe => (keyframe.Pose.Sequence, keyframe.Pose.PoseParameters[0]))
            .ShouldBe([(4, 0.5f), (4, 0.6f), (7, 0.6f)]);
    }

    /// <remarks>
    /// **The repeat test is unchanged**: it compares the pose last GIVEN, so the same pose twice is one keyframe even
    /// though the stored one now holds a list shared from before it.
    /// </remarks>
    [Test]
    public void Add_SamePoseAfterASharedOne_IsDroppedAsARepeat()
    {
        ScenePropTrack track = new(1, "models/a.mdl");
        ScenePose second = Pose(5f, 4, 0.5f);

        track.Add(10, Pose(1f, 4, 0.5f));
        track.Add(11, second);
        track.Add(12, second);

        track.KeyframeCount.ShouldBe(2);
    }
}
