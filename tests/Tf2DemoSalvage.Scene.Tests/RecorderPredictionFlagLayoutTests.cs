using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// B501's wiring: <see cref="RecorderPrediction.PredictFrom"/> reads <c>FL_WATERJUMP</c> through the TIMELINE's flag
/// layout, not the current one.
/// </summary>
/// <remarks>
/// A nine-bit (orangebox) recorder in an empty world, airborne, two commands past the packet with no input. A water
/// jump (<c>1&lt;&lt;2</c> there) is a move this port declines, so prediction answers nothing; <c>FL_ONTRAIN</c>
/// (<c>1&lt;&lt;3</c> there, which is <c>FL_WATERJUMP</c> in the current list) is not, so it answers two ticks of
/// gravity: <c>800 × 0.015 × 2 = 24</c> down.
/// </remarks>
public sealed class RecorderPredictionFlagLayoutTests
{
    private const float Interval = 0.015f;

    [Test]
    public void PredictFrom_WaterJumpingInANineBitDemo_IsDeclined() =>
        Predict(1 << 2).ShouldBeNull();

    [Test]
    public void PredictFrom_OnATrainInANineBitDemo_FallsTwoTicks() =>
        Predict(1 << 3).ShouldBe((0f, 0f, -24f));

    private static (float X, float Y, float Z)? Predict(int flags)
    {
        ScenePlayer recorder = new(1, 0f, 0f, 1000f, 2, 125, 1, Flags: flags, MaxSpeed: 400f);
        DemoTimeline timeline = DemoTimeline.ForRecorder(
            [new TimelineFrame(100, [recorder])],
            PlayerFlagLayout.OrangeBox,
            [Command(101, 6), Command(102, 7)],
            [(100, 5)],
            Interval);
        MapLevel level = EmptyWorld();

        return new RecorderPrediction(timeline, () => level).PredictFrom((100, 5), 102);
    }

    private static RecordedUserCommand Command(int tick, int sequence) =>
        new(tick, sequence, new UserCommand(sequence, tick, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0, 0, 0, 0, 0, 0));

    /// <summary>One node whose every point is in front of its plane, over one empty leaf.</summary>
    private static MapLevel EmptyWorld()
    {
        byte[] planes = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan(8), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(planes.AsSpan(12), -100000f);

        byte[] nodes = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(4), -1);
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(8), -1);

        BspLeafTree tree = BspLeafTree.FromCollisionLumps(nodes, planes, new byte[32], Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>());

        return new MapLevel(
            null, null, null, new Dictionary<int, string>(), tree, null, null, [], [], [], [], null, default);
    }
}
