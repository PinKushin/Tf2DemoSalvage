using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What the bone fields actually carry on models TF2 ships.
/// </summary>
/// <remarks>
/// **Run before the content assertions were written, not after.** The reader now returns
/// <c>flags</c>, <c>proctype</c> and the controller slots, and the obvious next move is a test
/// asserting what a player model contains — but "obvious" there means guessed. Whether TF2 marks
/// its merge bones at all is a real question with a measurable answer, and Valve's own commented-out
/// warning in <c>bone_merge_cache.cpp</c> implies plenty of models do not.
///
/// <c>[Explicit]</c>, like every probe here, and it needs the game installed.
/// </remarks>
public sealed class BoneFlagContentProbe
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    [Explicit("Reports the bone flags real models carry; run it when the pipeline needs to know.")]
    public void Probe_TheModelsTf2Ships_ReportTheirBoneFlags()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return;
        }

        string[] models =
        [
            "models/player/scout.mdl",
            "models/player/heavy.mdl",
            "models/player/soldier.mdl",
            "models/weapons/c_models/c_rocketlauncher/c_rocketlauncher.mdl",
            "models/player/items/scout/scout_cap.mdl",
            "models/player/items/demo/demo_ttg_max_head.mdl",
        ];

        foreach (string path in models)
        {
            byte[]? file = GameArchives.Open(Game).Read(path);

            if (file is null)
            {
                TestContext.Out.WriteLine($"{path}: not in this install");
                continue;
            }

            IReadOnlyList<StudioBone> bones = StudioBones.Read(file);

            int merge = bones.Count(bone => bone.IsMergeTarget);
            int procedural = bones.Count(bone => bone.ProcedureType != 0);
            int driven = bones.Count(bone => bone.Controllers.ToArray().Any(slot => slot >= 0));

            // Every bit any bone sets, so an unknown one shows up rather than being masked away.
            int union = bones.Aggregate(0, (all, bone) => all | bone.Flags);

            TestContext.Out.WriteLine(
                $"=== {Path.GetFileName(path)}: {bones.Count} bones, " +
                $"{merge} merge targets, {procedural} procedural, {driven} controller-driven, " +
                $"flag union 0x{union:X}");

            foreach (StudioBone bone in bones.Where(bone => bone.ProcedureType != 0).Take(6))
            {
                TestContext.Out.WriteLine(
                    $"    proc {bone.Name}: type {bone.ProcedureType} at {bone.ProcedureIndex}, " +
                    $"flags 0x{bone.Flags:X}");
            }

            foreach (StudioBone bone in bones.Where(bone => bone.IsMergeTarget).Take(6))
            {
                TestContext.Out.WriteLine($"    merge {bone.Name}: flags 0x{bone.Flags:X}");
            }

            // Anything outside the families studio.h declares would mean the field is being read
            // from the wrong place — a plausible number rather than an error, which is this
            // project's standing failure mode.
            int known =
                StudioBoneFlags.UsedByAnything |
                StudioBoneFlags.AlwaysProcedural |
                StudioBoneFlags.PhysicallySimulated |
                StudioBoneFlags.PhysicsProcedural |
                0x00000008 | 0x00000010 |   // BONE_SCREEN_ALIGN_SPHERE, _CYLINDER
                0x00F00000 |                // BONE_TYPE_MASK
                0x00100000 |                // BONE_FIXED_ALIGNMENT
                0x00600000;                 // BONE_HAS_SAVEFRAME_POS, _ROT

            TestContext.Out.WriteLine($"    unknown bits 0x{union & ~known:X}");
        }
    }
}
