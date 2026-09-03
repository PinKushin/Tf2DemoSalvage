using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Studio bytes carrying one animation with a real frame count and rate.</summary>
/// <remarks>
/// **<see cref="SyntheticSkinnedModel"/> builds <c>Models: [[]]</c>**, which is enough for the
/// sequence-selection tests it was written for and not enough for anything about TIME. Both
/// <c>SkinnedModel.Frames</c> and <c>SkinnedModel.CyclesPerSecond</c> read the studio bytes, so a
/// model built that way reports one frame at zero cycles a second — and a cycle that cannot advance
/// makes several questions unanswerable while looking answered.
///
/// **Two tests were written against that fixture and one of them could not fail.** A gesture whose
/// sequence has no rate never reaches cycle one, so it never auto-kills, so an assertion that an
/// expired gesture is dropped passed only because the gesture never expired. That is the
/// wrong-condition trap: correct and broken predict the same observation, and strengthening the
/// assertion would not have helped.
/// </remarks>
internal static class AnimatedStudioBytes
{
    /// <summary>Frames the animation carries.</summary>
    /// <remarks>
    /// Thirty-one frames at thirty a second is one cycle a second exactly, since
    /// <c>Studio_CPS</c> divides the rate by <c>numframes - 1</c>. A round number keeps the
    /// arithmetic in a test visible rather than hidden behind the fixture.
    /// </remarks>
    public const int Frames = 31;

    /// <summary>Frames a second the animation plays at.</summary>
    public const float Rate = 30f;

    /// <summary>The bytes.</summary>
    /// <param name="animations">
    /// How many animation descriptions to write, all identical. **Must cover every animation index
    /// the sequences reference**, and getting this wrong is silent: an index past the count reads
    /// as zero frames, which becomes one frame at zero cycles a second — a sequence that cannot
    /// advance, exactly like the empty-bytes case this file exists to replace.
    /// <c>SyntheticSkinnedModel.With</c> numbers a model's animations from zero in the order the
    /// labels are given, so this wants the label count.
    /// </param>
    /// <returns>A <c>.mdl</c> body carrying that many animation descriptions.</returns>
    /// <remarks>
    /// Only the four fields the readers touch: the animation count and index in the header, then
    /// each animation's frames-a-second and frame count. Written by hand rather than copied from a
    /// shipped model, so the values under test are the ones this file put there.
    /// </remarks>
    public static byte[] OneSecondLoop(int animations = 1)
    {
        const int header = 256;
        const int stride = 100;

        byte[] file = new byte[header + (Math.Max(1, animations) * stride)];

        BitConverter.TryWriteBytes(file.AsSpan(180), animations);
        BitConverter.TryWriteBytes(file.AsSpan(184), header);

        for (int animation = 0; animation < animations; animation++)
        {
            int at = header + (animation * stride);

            BitConverter.TryWriteBytes(file.AsSpan(at + 8), Rate);
            BitConverter.TryWriteBytes(file.AsSpan(at + 16), Frames);
        }

        return file;
    }
}
