using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What every animation in <c>c_demo_arms</c> claims for the viewmodel weapon rack.
/// </summary>
/// <remarks>
/// **This separates a decode bug from a wrong-sequence bug, and nothing else can** (B222). In the
/// viewer, `c_demo_arms` bone 17 (`vm_weapon_bone_1`) is displaced up to 139 units from its rest
/// position by animation while bone 16 (`vm_weapon_bone`) never moves more than 20 — two bones with
/// the same parent whose bind positions are 1.51 units apart. The sticky launcher merges onto both,
/// so the model tears.
///
/// Two very different faults produce that, and they need opposite fixes:
///
/// <list type="number">
/// <item>the file really does contain a large displacement, and we are playing the wrong animation
/// — or it is legitimate and something downstream is at fault;</item>
/// <item>the file contains nothing of the sort and <see cref="StudioAnimation"/> is manufacturing
/// it, in which case the decode is wrong.</item>
/// </list>
///
/// **The file is the arbiter and it answers without a viewer, a demo, or anybody watching.** Every
/// animation, every frame, both bones — so a maximum here is the model's own claim rather than a
/// sample of whatever our sequence selection happened to pick.
///
/// **`StudioAnimation`'s own summary says the compressed paths are unverified**: only the
/// Quaternion64 path is covered by a test, and the rest is "unproven code on first contact". The
/// viewer's numbers carry the fingerprint of a saturated read — a raw `int16` near 32767 times a
/// small `posscale` lands on the 65.5 and 32.8 that dominate the log, in a power-of-two
/// relationship.
///
/// Explicit: it reports rather than asserts.
/// </remarks>
[Explicit("Diagnostic: what c_demo_arms' animations claim for the weapon rack.")]
public sealed class ViewmodelArmsAnimationDiagnostic
{
    [TestCase("models/weapons/c_models/c_demo_arms.mdl")]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl")]
    public void ReportWeaponRackTravel(string Model)
    {
        if (Read(Model) is not { } bytes)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        // **The arms' own file carries ONE animation, and that is not what the viewer plays.** A
        // viewmodel's animations live in the models it `$includemodel`s, so sweeping only the base
        // file answers a narrower question than the one being asked — the first version of this
        // diagnostic did exactly that and read "bone 17 never moves" off the wrong file.
        foreach (string included in StudioModelGroups.Read(bytes))
        {
            TestContext.Out.WriteLine($"  includes: {included}");
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);
        int animations = StudioAnimation.Count(bytes);

        TestContext.Out.WriteLine($"{Model}: {bones.Count} bones, {animations} local animations");

        // **Every bone, not just 16 and 17.** A per-bone worst case over the whole file says whether
        // a large displacement is peculiar to the rack or is everywhere — and "everywhere" would
        // mean the decode, not the animation.
        float[] worst = new float[bones.Count];
        int[] worstAnimation = new int[bones.Count];
        int[] worstFrame = new int[bones.Count];

        Array.Fill(worstAnimation, -1);

        for (int animation = 0; animation < animations; animation++)
        {
            int frames = StudioAnimation.Frames(bytes, animation);

            for (int frame = 0; frame < frames; frame++)
            {
                foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
                {
                    if (posed.Bone < 0 || posed.Bone >= bones.Count)
                    {
                        continue;
                    }

                    StudioBone rest = bones[posed.Bone];

                    float dx = posed.Position.X - rest.Position.X;
                    float dy = posed.Position.Y - rest.Position.Y;
                    float dz = posed.Position.Z - rest.Position.Z;

                    float travelled = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (travelled > worst[posed.Bone])
                    {
                        worst[posed.Bone] = travelled;
                        worstAnimation[posed.Bone] = animation;
                        worstFrame[posed.Bone] = frame;
                    }
                }
            }
        }

        int reported = 0;

        for (int bone = 0; bone < bones.Count; bone++)
        {
            if (worstAnimation[bone] < 0 || worst[bone] < 1f)
            {
                continue;
            }

            reported++;

            TestContext.Out.WriteLine(
                $"  [{bone,2}] {bones[bone].Name,-24} worst {worst[bone],9:0.##} units " +
                $"in animation {worstAnimation[bone]} frame {worstFrame[bone]} " +
                $"(posscale {bones[bone].PositionScale.X:0.#####}, " +
                $"{bones[bone].PositionScale.Y:0.#####}, {bones[bone].PositionScale.Z:0.#####})");
        }

        TestContext.Out.WriteLine($"  {reported} bones move more than a unit anywhere in the file");
    }

    /// <summary>Whether a decoded animation is continuous from one frame to the next.</summary>
    /// <remarks>
    /// **This decides whether the decode is wrong WITHOUT a reference implementation, and nothing
    /// else available here does** (B222). Every other question about these numbers — is 128 units
    /// plausible, is `posscale` really 1/256, does `pPosV` land in the right place — needs something
    /// to compare against. Continuity does not: an animation is a person moving, so consecutive
    /// frames differ by a little. A misread stream produces `int16`s drawn from arbitrary bytes,
    /// which jump the full ±32767 range between neighbouring frames.
    ///
    /// So a large frame-to-frame jump is a decode fault by construction, and a smooth curve says the
    /// decode is right and the fault is downstream — the two possibilities that need opposite fixes.
    ///
    /// The saturation arithmetic that prompted it: `posscale` reads exactly 1/256 on every bone and
    /// axis, and 32767 × 1/256 = 128.0, which is `bip_wrist_R`'s worst travel to the decimal.
    /// </remarks>
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 58, 17)]
    [TestCase("models/weapons/c_models/c_demo_animations.mdl", 12, 16)]
    public void ReportFrameContinuity(string model, int animation, int bone)
    {
        if (Read(model) is not { } bytes)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);
        int frames = StudioAnimation.Frames(bytes, animation);

        TestContext.Out.WriteLine(
            $"{model} animation {animation}, bone {bone} ({bones[bone].Name}), {frames} frames");

        (float X, float Y, float Z)? previous = null;

        float biggest = 0f;
        int biggestAt = -1;

        for (int frame = 0; frame < frames; frame++)
        {
            foreach (StudioBonePose posed in StudioAnimation.Pose(bytes, bones, animation, frame))
            {
                if (posed.Bone != bone)
                {
                    continue;
                }

                if (previous is { } was)
                {
                    float dx = posed.Position.X - was.X;
                    float dy = posed.Position.Y - was.Y;
                    float dz = posed.Position.Z - was.Z;

                    float jump = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                    if (jump > biggest)
                    {
                        biggest = jump;
                        biggestAt = frame;
                    }
                }

                // Every frame, no sampling: a jump is a single-frame event and a sample of one in
                // ten is exactly how an instrument misses it.
                TestContext.Out.WriteLine(
                    $"  frame {frame,3}: ({posed.Position.X,9:0.##}, {posed.Position.Y,9:0.##}, " +
                    $"{posed.Position.Z,9:0.##})");

                previous = posed.Position;
            }
        }

        TestContext.Out.WriteLine(
            $"  biggest frame-to-frame jump: {biggest:0.##} units, entering frame {biggestAt}");
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
