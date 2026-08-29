using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Where <c>c_demo_arms</c> actually puts its weapon bones, read from the file.
/// </summary>
/// <remarks>
/// **Written to settle a question that eight running-program instruments could not** (B222). A
/// demoman's sticky launcher is drawn with its visible vertices spanning up to 108 units on a model
/// 28 units long, and the bone dragging them is `vm_weapon_bone_1`, merged from `c_demo_arms` bone
/// 17. Measured in the viewer, arms bones 16 and 17 sit about 92 units apart.
///
/// **Ninety-two units is impossible on a viewmodel** — the whole arms model measures 28 to 55 units
/// posed. So either the model really does carry a bone out there and the weapon's own bind pose is
/// supposed to compensate, or our pose is displacing it. The file answers that directly, and it
/// answers it without a viewer, a demo, or anybody watching a screen.
///
/// **The rest pose is the right thing to read.** It is what the bones are before any animation, so
/// a large separation here is the model's own geometry and a small one means the separation seen in
/// the viewer was introduced by us.
///
/// Explicit: it reports rather than asserts.
/// </remarks>
[Explicit("Diagnostic: where c_demo_arms puts its weapon bones.")]
public sealed class ViewmodelArmsBoneDiagnostic
{
    [TestCase("models/weapons/c_models/c_demo_arms.mdl")]
    [TestCase("models/weapons/c_models/c_stickybomb_launcher/c_stickybomb_launcher.mdl")]

    // **The control, and it is the same demoman wearing the same arms.** The sticky launcher tears
    // and these do not, so if the bind separation explains the tear these must agree with the arms'
    // 1.51-unit rack spacing where the sticky launcher's 10.54 does not. If they ALSO disagree, the
    // mismatch is normal and this whole line of reasoning is dead.
    [TestCase("models/weapons/c_models/c_grenadelauncher/c_grenadelauncher.mdl")]
    [TestCase("models/weapons/c_models/c_bottle/c_bottle.mdl")]
    [TestCase("models/weapons/c_models/c_scattergun/c_scattergun.mdl")]
    public void ReportWeaponBones(string model)
    {
        if (Read(model) is not { } bytes)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(bytes);

        bones.ShouldNotBeEmpty($"{model} should declare bones");

        TestContext.Out.WriteLine($"{model}: {bones.Count} bones");

        // **`BoneToWorld`, NOT `Matrices`, and the first version of this diagnostic got it wrong.**
        // `Matrices` is the SKINNING matrix — `boneToWorld * poseToBone` — so in the rest pose it is
        // a bone's own transform times its own inverse, which is the IDENTITY for every bone. Its
        // translation is therefore (0, 0, 0) everywhere, and reading that as "the bones are
        // coincident" is what sent B222 down a blind alley for hours.
        //
        // The control that caught it was two `bip_hand` bones, which reported the same point. Two
        // hands are never in the same place, so the reader was wrong rather than the model strange.
        StudioSkeleton rest = StudioBones.RestPose(bones);

        for (int bone = 0; bone < bones.Count; bone++)
        {
            StudioBone each = bones[bone];

            // **`bip_` bones are the CONTROL and the first version had none.** Every weapon bone
            // printed a rest position of (0, 0, 0), including bones with different parents — which
            // is not something a real skeleton does, and is exactly what a misread matrix layout
            // looks like. A hand and a head are metres apart on any model, so if those also read
            // zero the reader is wrong rather than the model being strange.
            if (!each.Name.Contains("weapon", StringComparison.OrdinalIgnoreCase) &&
                !each.Name.Contains("bip_hand", StringComparison.OrdinalIgnoreCase) &&
                !each.Name.Contains("bip_head", StringComparison.OrdinalIgnoreCase) &&
                !each.Name.Contains("bip_pelvis", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadOnlySpan<float> matrix = rest.BoneToWorld[bone];

            TestContext.Out.WriteLine(
                $"  [{bone,2}] {each.Name,-24} parent {each.Parent,3} " +
                $"flags 0x{each.Flags:X5} proc {each.ProcedureType} " +
                $"rest ({matrix[3]:0.##}, {matrix[7]:0.##}, {matrix[11]:0.##})");
        }

        // And the separation that started this, stated as a number rather than left to the eye.
        List<int> weapons =
        [
            .. Enumerable.Range(0, bones.Count)
                .Where(bone => bones[bone].Name.StartsWith("vm_weapon_bone", StringComparison.OrdinalIgnoreCase))
        ];

        if (weapons.Count >= 2)
        {
            ReadOnlySpan<float> first = rest.BoneToWorld[weapons[0]];
            ReadOnlySpan<float> second = rest.BoneToWorld[weapons[1]];

            float dx = first[3] - second[3];
            float dy = first[7] - second[7];
            float dz = first[11] - second[11];

            TestContext.Out.WriteLine(
                $"  vm_weapon_bone separation in the REST pose: " +
                $"{MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)):0.##} units");
        }
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
