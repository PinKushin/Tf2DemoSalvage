using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The three bone fields the reader never read, against hand-built bytes.
/// </summary>
/// <remarks>
/// **Hand-built rather than measured off a real model, and that is the point.** A shipped
/// <c>.mdl</c> takes one path through this code: its bones all carry sensible flags, none of them
/// has a procedural rule at index zero, and every controller slot is either −1 or a small number.
/// The cases that decide whether the reader is right are the ones no real file contains — a
/// <c>procindex</c> of exactly zero, which means "no rule" and not "the start of the file", and a
/// flags word with a bit set that nothing else sets.
///
/// **The offsets these depend on are already checked** by <c>BonePipelineStructTests</c> against
/// <c>studio.h</c>, so a failure here is the reader rather than the layout. That separation is
/// deliberate: a single suite covering both would report a wrong constant and a wrong read
/// identically.
///
/// <c>BoneFlagContentTests</c> is the other half — the same fields read off models TF2 ships, which
/// is what catches a reader that is self-consistently wrong.
/// </remarks>
public sealed class BoneFlagReaderTests
{
    /// <summary>Bytes in <c>studiohdr_t</c>, so the bone table can start after it.</summary>
    private const int HeaderSize = 408;

    [Test]
    public void Read_ABoneCarryingUsedByBoneMerge_ReportsIt()
    {
        IReadOnlyList<StudioBone> bones = StudioBones.Read(
            Model(
                Bone("root", parent: -1, flags: StudioBoneFlags.UsedByVertexLod0),
                Bone("bip_head", parent: 0,
                    flags: StudioBoneFlags.UsedByVertexLod0 | StudioBoneFlags.UsedByBoneMerge)));

        bones[1].Flags.ShouldBe(
            StudioBoneFlags.UsedByVertexLod0 | StudioBoneFlags.UsedByBoneMerge);
        bones[1].IsMergeTarget.ShouldBeTrue();

        // The control. Without a bone that does NOT carry the bit, a reader returning
        // BONE_USED_BY_ANYTHING for everything satisfies the assertion above.
        bones[0].IsMergeTarget.ShouldBeFalse();
    }

    [Test]
    public void Read_AJiggleBone_ReportsItsRuleAndOffset()
    {
        IReadOnlyList<StudioBone> bones = StudioBones.Read(
            Model(
                Bone("root", parent: -1),
                Bone("jiggle", parent: 0,
                    flags: StudioBoneFlags.AlwaysProcedural,
                    procedureType: StudioProcedureType.Jiggle,
                    procedureIndex: 216)));

        bones[1].ProcedureType.ShouldBe(StudioProcedureType.Jiggle);
        bones[1].ProcedureIndex.ShouldBe(216);
        bones[1].IsProcedural.ShouldBeTrue();
    }

    [Test]
    public void Read_AProcedureIndexOfZero_IsNoRuleRatherThanAnOffset()
    {
        // **The case no shipped model contains, and the one that decides the reader.** `procindex`
        // is relative to the BONE and `pProcedure()` returns null for zero, so a reader that treats
        // it as an offset would decode the bone's own first bytes — its name index — as a
        // procedural rule. The flag is set here precisely so that only the index can distinguish
        // the two readings.
        IReadOnlyList<StudioBone> bones = StudioBones.Read(
            Model(
                Bone("armed", parent: -1,
                    flags: StudioBoneFlags.AlwaysProcedural,
                    procedureType: StudioProcedureType.Jiggle,
                    procedureIndex: 0)));

        bones[0].ProcedureIndex.ShouldBe(0);
        bones[0].IsProcedural.ShouldBeFalse();
    }

    [Test]
    public void Read_ABoneWithNoRule_ReportsNeitherTypeNorIndex()
    {
        IReadOnlyList<StudioBone> bones = StudioBones.Read(Model(Bone("plain", parent: -1)));

        bones[0].ProcedureType.ShouldBe(0);
        bones[0].ProcedureIndex.ShouldBe(0);
        bones[0].IsProcedural.ShouldBeFalse();
    }

    [Test]
    public void Read_TheSixControllerSlots_AreReportedInOrder()
    {
        // Distinct values per slot, because six copies of one number cannot show that the slots are
        // read in order — and the order IS the meaning: slot 3 is XR, not Z.
        IReadOnlyList<StudioBone> bones = StudioBones.Read(
            Model(Bone("driven", parent: -1, controllers: [-1, 2, -1, 0, 5, -1])));

        bones[0].Controllers.ToArray().ShouldBe([-1, 2, -1, 0, 5, -1]);
    }

    [Test]
    public void Read_ABoneTheReaderAlreadyHandled_IsUnchanged()
    {
        // **The control for the whole file.** Four fields were appended to a positional record, and
        // the failure that would cause is every earlier field shifting by one — which produces
        // plausible values, not an exception. This asserts the parts that were already right.
        IReadOnlyList<StudioBone> bones = StudioBones.Read(
            Model(Bone("root", parent: -1), Bone("child", parent: 0)));

        bones.Count.ShouldBe(2);
        bones[0].Name.ShouldBe("root");
        bones[1].Name.ShouldBe("child");
        bones[1].Parent.ShouldBe(0);
    }

    /// <summary>One built bone: its name, and the 216 bytes that are not the name.</summary>
    /// <remarks>
    /// **The name travels WITH the bytes, and the first version of this did not.** It collected
    /// names into a static list that <c>Model</c> read back, which is shared mutable state in an
    /// assembly that runs its fixtures in parallel — so a bone built by one test was named by
    /// another. The control test caught it by reporting a bone called <c>driven</c> in a model that
    /// only ever declared <c>root</c> and <c>child</c>, which is exactly the kind of cross-talk a
    /// fixture must not be capable of.
    /// </remarks>
    private sealed record BuiltBone(string Name, byte[] Bytes);

    /// <summary>One bone's 216 bytes, with everything not named left at zero.</summary>
    private static BuiltBone Bone(
        string name,
        int parent,
        int flags = 0,
        int procedureType = 0,
        int procedureIndex = 0,
        int[]? controllers = null)
    {
        byte[] bone = new byte[StudioLayout.BoneStride];

        // sznameindex is relative to the bone, so it cannot be written until Model knows where this
        // one lands. Everything else is here.
        Write(bone, StudioLayout.BoneParentOffset, parent);
        Write(bone, StudioLayout.BoneFlagsOffset, flags);
        Write(bone, StudioLayout.BoneProcedureTypeOffset, procedureType);
        Write(bone, StudioLayout.BoneProcedureIndexOffset, procedureIndex);

        int[] slots = controllers ?? [-1, -1, -1, -1, -1, -1];

        for (int slot = 0; slot < StudioLayout.BoneControllerSlots; slot++)
        {
            Write(bone, StudioLayout.BoneControllerListOffset + (slot * 4), slots[slot]);
        }

        return new BuiltBone(name, bone);
    }

    /// <summary>A minimal <c>.mdl</c>: a header, a bone table, and the names after it.</summary>
    private static byte[] Model(params BuiltBone[] bones)
    {
        int table = HeaderSize;
        int strings = table + (bones.Length * StudioLayout.BoneStride);

        List<byte> text = [];
        List<int> offsets = [];

        foreach (BuiltBone bone in bones)
        {
            offsets.Add(strings + text.Count);

            foreach (char letter in bone.Name)
            {
                text.Add((byte)letter);
            }

            text.Add(0);
        }

        byte[] file = new byte[strings + text.Count];

        Write(file, StudioLayout.HeaderBoneCountOffset, bones.Length);
        Write(file, StudioLayout.HeaderBoneIndexOffset, table);

        for (int index = 0; index < bones.Length; index++)
        {
            int at = table + (index * StudioLayout.BoneStride);

            bones[index].Bytes.CopyTo(file, at);

            Write(file, at + StudioLayout.BoneNameOffset, offsets[index] - at);
        }

        text.CopyTo(file, strings);

        return file;
    }

    private static void Write(byte[] into, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(into.AsSpan(at), value);
}
