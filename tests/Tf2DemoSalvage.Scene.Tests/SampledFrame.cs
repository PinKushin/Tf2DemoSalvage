using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where the sampler lands a cycle in an <see cref="AnimatedStudioBytes"/> animation: its frame and fraction.</summary>
/// <remarks>
/// **The skeleton carries the cycle and each animation finds its own frame** (B742), so a test that predicted the frame
/// asks this for it: production's <c>StudioSequences.FrameAt</c> over the fixture's own frame count, as the sampler calls it.
/// </remarks>
internal static class SampledFrame
{
    /// <summary>The frame and fraction a cycle lands on.</summary>
    /// <param name="cycle">The cycle the skeleton or layer carries.</param>
    /// <param name="loops">Whether the fixture's sequence loops.</param>
    /// <returns>The frame and the fraction past it.</returns>
    public static (int Frame, float Fraction) Of(float cycle, bool loops = true) =>
        StudioSequences.FrameAt(cycle, AnimatedStudioBytes.Frames, loops);
}
