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

        // The rest pose walks the parent chain, so these are model-space positions rather than
        // each bone's offset from its parent — which is what the viewer's numbers are too.
        StudioSkeleton rest = StudioBones.RestPose(bones);

        for (int bone = 0; bone < bones.Count; bone++)
        {
            StudioBone each = bones[bone];

            // Only the weapon-related bones; a 65-bone arms model would otherwise bury them.
            if (!each.Name.Contains("weapon", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadOnlySpan<float> matrix = rest.Matrices[bone];

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
            ReadOnlySpan<float> first = rest.Matrices[weapons[0]];
            ReadOnlySpan<float> second = rest.Matrices[weapons[1]];

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
