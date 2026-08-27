using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Whether a player's animation stands the reference pose up.
/// </summary>
/// <remarks>
/// **A TF2 player's reference pose is authored lying on its back**, which is ordinary in Source
/// character pipelines and is measured: the vertices run 0..84.5 along Y, the bones agree, and the
/// rest skinning matrix is the identity so the model draws exactly as authored. Props are Z-tall in
/// their own vertices and so never showed this.
///
/// Therefore the standing orientation can only come from an animation, and the viewer's posed
/// players have a Z span of 23 where standing needs 83. This poses a named standing sequence
/// directly, with no renderer and no desktop, and asks how tall the result is.
///
/// **83 is standing and nothing else is.** A number near 23 means the animation this project
/// applies does not stand the model up; a number near 83 means it does and the fault is downstream
/// in the viewer.
/// </remarks>
public sealed class StandingPoseProbe
{
    [Test]
    public void StandingPose_ASequence_IsReported()
    {
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        if (archives.Read("models/player/scout.mdl") is not { } baseFile)
        {
            Assert.Ignore("scout.mdl not found in the install.");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(baseFile);

        // The base model plus every model it includes, which is where a player's animation lives:
        // scout.mdl declares 306 sequences and two one-frame local animations.
        List<byte[]> models = [baseFile];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, StudioSequences.Read(baseFile))];

        foreach (string include in StudioModelGroups.Read(baseFile))
        {
            if (archives.Read(include) is { } included)
            {
                groups.Add((models.Count, StudioSequences.Read(included)));
                models.Add(included);
            }
        }

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);

        TestContext.Out.WriteLine(
            $"STAND scout: {models.Count} models, {table.Count} merged sequences");

        // **Named rather than numbered.** A sequence index is a property of the merge, so asserting
        // on one would test the merge against itself; a label is what the model itself calls the
        // animation and is the same in every class.
        foreach (string label in new[] { "stand_PRIMARY", "Stand_MELEE", "run_PRIMARY" })
        {
            int sequence = -1;

            for (int index = 0; index < table.Count; index++)
            {
                if (table.At(index) is not { } at ||
                    at.Local >= groups[at.Group].Sequences.Count)
                {
                    continue;
                }

                if (string.Equals(
                    groups[at.Group].Sequences[at.Local].Label,
                    label,
                    StringComparison.OrdinalIgnoreCase))
                {
                    sequence = index;
                    break;
                }
            }

            if (sequence < 0)
            {
                TestContext.Out.WriteLine($"STAND {label}: not in the merged table");
                continue;
            }

            (int group, int local) = table.At(sequence)!.Value;
            int animation = groups[group].Sequences[local].Animation;

            IReadOnlyList<StudioBone> owner = StudioBones.Read(models[group]);

            IReadOnlyList<StudioBonePose> pose =
                StudioAnimation.Pose(models[group], owner, animation, 0);

            // The animation's bones are its own model's and must be renumbered onto the base
            // skeleton — Valve's masterBone, bone_setup.cpp:966. Skipping it moves the right
            // rotations to the wrong joints, which is scrambled rather than absent.
            if (group != 0)
            {
                int[] remap = StudioBones.Remap(owner, bones);
                List<StudioBonePose> renumbered = new(pose.Count);

                foreach (StudioBonePose moved in pose)
                {
                    int bone = moved.Bone >= 0 && moved.Bone < remap.Length ? remap[moved.Bone] : -1;

                    if (bone >= 0)
                    {
                        renumbered.Add(moved with { Bone = bone });
                    }
                }

                pose = renumbered;
            }

            StudioSkeleton posed = StudioBones.Posed(bones, pose);

            float minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (float[] bone in posed.BoneToWorld)
            {
                minY = MathF.Min(minY, bone[7]);
                maxY = MathF.Max(maxY, bone[7]);
                minZ = MathF.Min(minZ, bone[11]);
                maxZ = MathF.Max(maxZ, bone[11]);
            }

            int head = -1;

            for (int index = 0; index < bones.Count; index++)
            {
                if (string.Equals(bones[index].Name, "bip_head", StringComparison.OrdinalIgnoreCase))
                {
                    head = index;
                    break;
                }
            }

            TestContext.Out.WriteLine(
                $"STAND {label}: sequence {sequence} group {group} local {local} " +
                $"animation {animation}, {pose.Count} bones posed, " +
                $"bones span y {maxY - minY:0.#} z {maxZ - minZ:0.#}, " +
                $"head at ({posed.BoneToWorld[head][3]:0.#}," +
                $"{posed.BoneToWorld[head][7]:0.#},{posed.BoneToWorld[head][11]:0.#})");
        }

        // **What the viewer's own lookup returns, which is a SUBSTRING match on the first hit.**
        // PropModels.SkinnedModel.Find uses Contains rather than equality and takes the earliest
        // sequence in merged order, so any longer label containing the wanted one wins if it is
        // listed first — and the merged table lists the base model's sequences before the included
        // models that hold the real animations.
        foreach (string name in new[] { "Stand_PRIMARY", "run_PRIMARY" })
        {
            for (int sequence = 0; sequence < table.Count; sequence++)
            {
                if (table.At(sequence) is not { } at ||
                    at.Local >= groups[at.Group].Sequences.Count)
                {
                    continue;
                }

                string label = groups[at.Group].Sequences[at.Local].Label;

                if (!label.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int animation = groups[at.Group].Sequences[at.Local].Animation;

                TestContext.Out.WriteLine(
                    $"STAND Find(\"{name}\") -> sequence {sequence} label \"{label}\" " +
                    $"group {at.Group} local {at.Local} animation {animation}, " +
                    $"{StudioAnimation.Frames(models[at.Group], animation)} frames");

                break;
            }
        }

        // The rest pose for comparison, which is known to lie down.
        StudioSkeleton rest = StudioBones.RestPose(bones);

        float restMinY = float.MaxValue, restMaxY = float.MinValue;
        float restMinZ = float.MaxValue, restMaxZ = float.MinValue;

        foreach (float[] bone in rest.BoneToWorld)
        {
            restMinY = MathF.Min(restMinY, bone[7]);
            restMaxY = MathF.Max(restMaxY, bone[7]);
            restMinZ = MathF.Min(restMinZ, bone[11]);
            restMaxZ = MathF.Max(restMaxZ, bone[11]);
        }

        TestContext.Out.WriteLine(
            $"STAND rest: bones span y {restMaxY - restMinY:0.#} z {restMaxZ - restMinZ:0.#}");

        Assert.Pass();
    }

    [Test]
    public void StandingPose_ASequence_IsFoundByItsExactName()
    {
        // **The regression that matters, stated as a height.** A sequence is looked up by label,
        // and matching on a substring takes the first LONGER label embedding it —
        // "AttackStand_PRIMARY" beats "stand_PRIMARY" because it sorts earlier in the merged table.
        //
        // An attack sequence is an upper-body layer meant to be added to a base pose. Played alone
        // as an absolute pose it leaves the skeleton near its reference, which for a TF2 player is
        // lying on its back — so every player in the viewer lay down, every worn item sat at ankle
        // height because bip_head was down there with them, and the limbs splayed.
        //
        // Asserted on the posed height rather than on the sequence number, because the number is a
        // property of the merge and would test the merge against itself. A scout's head belongs
        // around 60 units up; the broken lookup put the whole skeleton inside a 23-unit band.
        if (Environment.GetEnvironmentVariable("TF2_FOLDER") is not { Length: > 0 } folder ||
            !Directory.Exists(folder))
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(folder);

        if (archives.Read("models/player/scout.mdl") is not { } baseFile)
        {
            Assert.Ignore("scout.mdl not found in the install.");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(baseFile);

        List<byte[]> models = [baseFile];
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, StudioSequences.Read(baseFile))];

        foreach (string include in StudioModelGroups.Read(baseFile))
        {
            if (archives.Read(include) is { } included)
            {
                groups.Add((models.Count, StudioSequences.Read(included)));
                models.Add(included);
            }
        }

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);

        // **Through the real lookup, not a copy of it.** The first version of this test did its own
        // exact-name search and then posed the result — so it measured the model files and passed
        // happily with the defect reinstated. A test of a lookup has to call the lookup.
        // Merged across the base model and its includes, exactly as PropModels does it — the base
        // model alone declares neither move_x nor move_y.
        (IReadOnlyList<StudioPoseParameter> sharedPose, IReadOnlyList<IReadOnlyList<int>> masterPose) =
            StudioPoseParameterMerge.Merge([.. models.Select(file => StudioSequences.PoseParameters(file))]);

        PropModels.SkinnedModel model = new(
            bones, models, table, groups, sharedPose, masterPose);

        int found = model.Find("stand_PRIMARY");

        found.ShouldBeGreaterThanOrEqualTo(0, "a scout declares stand_PRIMARY");

        // And posed through the real path too, so the whole chain from name to matrices runs.
        StudioSkeleton posed = model.Skeleton(found, 0);

        float lowest = float.MaxValue;
        float highest = float.MinValue;

        foreach (float[] bone in posed.BoneToWorld)
        {
            lowest = MathF.Min(lowest, bone[11]);
            highest = MathF.Max(highest, bone[11]);
        }

        // Standing. The reference pose spans 14 on Z and the attack layer 23; neither reaches this.
        (highest - lowest).ShouldBeGreaterThan(
            45f, "a standing scout's bones span about 59 units vertically");
    }
}
