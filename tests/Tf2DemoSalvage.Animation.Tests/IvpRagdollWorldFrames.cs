using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>Frames of a fixed step, as the game hands them to <c>Simulate</c>.</summary>
internal static class IvpRagdollWorldFrames
{
    /// <summary>The tick interval the test worlds are built with.</summary>
    internal const float Step = 1f / 66f;

    /// <summary>Simulates one step at a time for about some seconds — <c>Simulate</c> cuts a longer frame to a tenth.</summary>
    internal static void SimulateFrames(this IvpRagdollWorld world, double seconds)
    {
        for (int frame = (int)System.Math.Round(seconds / Step); frame > 0; frame--)
        {
            world.Simulate(Step);
        }
    }
}
