using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A decoded animation is continuous from one frame to the next.
/// </summary>
/// <remarks>
/// **This is the only test in the suite that can fail when animation sections are ignored** (B222),
/// and without it the fix has no guard. The conformance suite proves
/// <see cref="StudioAnimation.Section"/> computes Valve's arithmetic; nothing there notices if
/// <see cref="StudioAnimation.Pose"/> stops calling it.
///
/// **Continuity is the measurement because it needs no reference implementation.** An animation is a
/// person moving, so consecutive frames differ by a little. A misread stream yields `int16`s taken
/// from arbitrary bytes and jumps the full range between neighbouring frames — so a large jump is a
/// decode fault by construction, whatever the cause.
///
/// Measured before the fix: single-frame excursions past 110 units, values saturating at
/// 32767 × posscale = 128. After: a worst jump of 0.31 units across 150 frames. The bound below is
/// two units — well above the real motion and far below any misread — so it is a decisive
/// prediction rather than a value copied off the current output.
///
/// **Skips without the game rather than failing**, which is right for CI: the machine without TF2
/// installed is the one that proves the no-install path works, and gating that into a failure would
/// destroy the signal.
/// </remarks>
public sealed class StudioAnimationContinuityTests
{
    /// <summary>How far a viewmodel bone may travel between adjacent frames.</summary>
    private const float Bound = 2f;

    // **Every case here was checked against the manipulation, and one was replaced because it
    // survived it.** Animation 76 passed with sectioning disabled — a case that cannot fail is not a
    // weaker test, it is an absent one, so it was swapped for 81. Sensitivity measured by disabling
    // the section lookup and confirming each goes red.
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 12)]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 58)]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 81)]
    public void Pose_AcrossEveryFrameOfALongAnimation_MovesEachBoneLessThanTwoUnits(
        string model, int animation)
    {
        if (Read(model) is not { } bytes)
        {
            Assert.Ignore($"{model} needs the game installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);
        int frames = StudioAnimation.Frames(bytes, animation);

        // **A long animation, or the test cannot see the defect it exists for.** Sections are
        // thirty frames; an animation shorter than that has only section zero and decodes correctly
        // even with the bug present.
        frames.ShouldBeGreaterThan(
            30, $"animation {animation} must span more than one section to exercise sectioning");

        Dictionary<int, (float X, float Y, float Z)> previous = [];

        float worst = 0f;
        int worstBone = -1;
        int worstFrame = -1;

        for (int frame = 0; frame < frames; frame++)
        {
            foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
            {
                if (previous.TryGetValue(posed.Bone, out (float X, float Y, float Z) was))
                {
                    float dx = posed.Position.X - was.X;
                    float dy = posed.Position.Y - was.Y;
                    float dz = posed.Position.Z - was.Z;

                    float jump = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (jump > worst)
                    {
                        worst = jump;
                        worstBone = posed.Bone;
                        worstFrame = frame;
                    }
                }

                previous[posed.Bone] = posed.Position;
            }
        }

        // Named, so a failure says which bone and which frame rather than only that one exists.
        string where = worstBone >= 0 && worstBone < bones.Count
            ? $"{bones[worstBone].Name}[{worstBone}] entering frame {worstFrame}"
            : "no bone moved";

        worst.ShouldBeLessThan(
            Bound,
            $"animation {animation} jumps {worst:0.##} units at {where}; a jump this size is a " +
            $"misread stream, not motion");
    }

    private static byte[]? Read(string path)
    {
        if (!GameInstall.Available)
        {
            return null;
        }

        byte[]? found = null;

        foreach (string archive in new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" })
        {
            string full = Path.Combine(GameInstall.Require(), archive);

            if (File.Exists(full))
            {
                found ??= VpkArchive.Open(full).ReadFile(path);
            }
        }

        return found;
    }
}
