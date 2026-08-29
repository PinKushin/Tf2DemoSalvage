using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    /// <summary>The merge targets a weapon actually hangs off.</summary>
    /// <remarks>
    /// **Not every bone in this file is continuous, and that is a fact about the DATA rather than
    /// about the reader.** `c_demo_arms` carries racks of weapon slots — `weapon_bone_0..4` and
    /// `vm_weapon_bone_0..7` — and a given weapon merges onto one or two of them. Nothing merges
    /// onto the rest, so their tracks are arbitrary, and the file says so outright. Animation 41,
    /// `weapon_bone_4`, its twenty raw X values scaled by `posscale` straight out of the bytes:
    ///
    /// <code>
    ///   -18.6 -6.8 5.7 15.9 17 [94.2] 17.6 13 9.8 7.9 7.1 7.3 8.1 9.5 11.1 13 14.7 16.2 17.2 17.6
    /// </code>
    ///
    /// The 94.2 is IN THE FILE, between a 17 and a 17.6, decoded with the same scale as its
    /// neighbours. Asserting continuity there predicts a property the source does not have, and an
    /// assertion the data can violate on its own says nothing about the reader either way.
    ///
    /// **So the subject is the slots a demoman's weapons really use**, which is also what B222 was
    /// about: the sticky launcher merges `vm_weapon_bone` and `vm_weapon_bone_1`. Those stay smooth
    /// across the whole file — 0.87 and 0.89 units at worst — and went from 245 to 0.31 when
    /// sectioning was fixed, so they are both meaningful and sensitive.
    ///
    /// The open question of whether the parked slots are genuinely arbitrary or hiding a second
    /// reader fault is B223, with the measurements. It is not settled by this test and is not
    /// claimed to be.
    /// </remarks>
    private static readonly string[] Merged = ["vm_weapon_bone", "vm_weapon_bone_1"];

    [TestCase("models/weapons/c_models/c_demo_animations.mdl")]
    public void Pose_AcrossEveryAnimationInTheFile_KeepsTheMergedBonesContinuous(string model)
    {
        if (Read(model) is not { } bytes)
        {
            Assert.Ignore($"{model} needs the game installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);

        List<int> subjects =
        [
            .. Enumerable.Range(0, bones.Count)
                .Where(bone => Merged.Contains(bones[bone].Name, StringComparer.OrdinalIgnoreCase))
        ];

        // The control: a renamed or missing bone would make every assertion below vacuous.
        subjects.Count.ShouldBe(
            Merged.Length, $"{model} should carry {string.Join(" and ", Merged)}");

        int animations = StudioAnimation.Count(bytes);

        // **Every animation, not a chosen few.** The three cases this replaced were picked while the
        // fault was still misunderstood, and two of them turned out to be measuring parked slots.
        animations.ShouldBeGreaterThan(1, "the file should carry animations to walk");

        float worst = 0f;
        int worstBone = -1;
        int worstAnimation = -1;
        int worstFrame = -1;
        int spanned = 0;

        for (int animation = 0; animation < animations; animation++)
        {
            int frames = StudioAnimation.Frames(bytes, animation);

            // Sections are thirty frames, so only a longer animation can exercise the defect this
            // exists for. Counted, so "none were long enough" cannot masquerade as a pass.
            if (frames > 30)
            {
                spanned++;
            }

            Dictionary<int, (float X, float Y, float Z)> previous = [];

            for (int frame = 0; frame < frames; frame++)
            {
                foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
                {
                    if (!subjects.Contains(posed.Bone))
                    {
                        continue;
                    }

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
                            worstAnimation = animation;
                            worstFrame = frame;
                        }
                    }

                    previous[posed.Bone] = posed.Position;
                }
            }
        }

        spanned.ShouldBeGreaterThan(
            0, "no animation spans more than one section, so sectioning was never exercised");

        // Named, so a failure says which bone, animation and frame rather than only that one exists.
        string where = worstBone >= 0 && worstBone < bones.Count
            ? $"{bones[worstBone].Name}[{worstBone}] in animation {worstAnimation} " +
                $"entering frame {worstFrame}"
            : "no bone moved";

        worst.ShouldBeLessThan(
            Bound,
            $"a merged bone jumps {worst:0.##} units at {where}; a jump this size on a bone a " +
            $"weapon hangs off is a misread stream, not motion");
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
