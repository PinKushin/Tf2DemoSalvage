using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Reading <c>mstudioquatinterpbone_t</c> and its triggers out of a model (B317).
/// </summary>
/// <remarks>
/// **Synthetic, so the test knows the right answer** (D38). The values below were put there by this
/// file, which a real `.mdl` cannot offer — reading Valve's own models proves only that two readings
/// of the same bytes agree. `bone-flags` on a corpus demo is what shows the reader works on shipped
/// files; this is what shows it reads the fields it means to.
///
/// <code>
/// struct mstudioquatinterpbone_t { int control; int numtriggers; int triggerindex; };
///
/// struct mstudioquatinterpinfo_t
/// {
///     float      inv_tolerance;  // 1 / radian angle of trigger influence
///     Quaternion trigger;        // angle to match
///     Vector     pos;            // new position
///     Quaternion quat;           // new angle
/// };
/// </code>
///
/// `studio.h:157-183`. Twelve bytes and forty-eight bytes.
/// </remarks>
public sealed class StudioQuatInterpTests
{
    /// <remarks>
    /// **Every field distinct, because a struct read with a wrong stride or field order returns
    /// plausible numbers rather than an error.** Values that repeated would let two swapped fields
    /// pass — the position and the rotation are both three-or-four floats in a row, and they are
    /// adjacent.
    /// </remarks>
    [Test]
    public void Read_ForABoneWithTheRule_ReturnsItsControlAndEveryTriggerField()
    {
        byte[] model = Model(
            Bone("bip_hand"),
            Bone("hlp_forearm", control: 0, triggers: 2));

        StudioQuatInterp rule = StudioQuatInterp.Read(model, 1).ShouldNotBeNull();

        rule.Control.ShouldBe(0);
        rule.Triggers.Count.ShouldBe(2);

        StudioQuatInterpTrigger first = rule.Triggers[0];

        first.InverseTolerance.ShouldBe(11f);
        first.TriggerX.ShouldBe(12f);
        first.TriggerY.ShouldBe(13f);
        first.TriggerZ.ShouldBe(14f);
        first.TriggerW.ShouldBe(15f);
        first.PositionX.ShouldBe(16f);
        first.PositionY.ShouldBe(17f);
        first.PositionZ.ShouldBe(18f);
        first.QuatX.ShouldBe(19f);
        first.QuatY.ShouldBe(20f);
        first.QuatZ.ShouldBe(21f);
        first.QuatW.ShouldBe(22f);

        // The SECOND trigger, which is what proves the 48-byte stride rather than the field order.
        rule.Triggers[1].InverseTolerance.ShouldBe(31f);
        rule.Triggers[1].QuatW.ShouldBe(42f);
    }

    /// <remarks>
    /// **A bone with no rule must answer null, not an empty rule.** `procindex == 0` is Valve's
    /// "none" and `pProcedure()` returns NULL for it — an empty-but-present rule would make every
    /// ordinary bone look procedural to the caller.
    /// </remarks>
    [Test]
    public void Read_ForAnOrdinaryBone_ReturnsNull()
    {
        byte[] model = Model(Bone("bip_hand"), Bone("hlp_forearm", control: 0, triggers: 2));

        StudioQuatInterp.Read(model, 0).ShouldBeNull();
    }

    /// <remarks>
    /// **A JIGGLE bone must not answer here.** `CalcProceduralBone` dispatches on
    /// `switch( proctype )` — an exact match — so a reader testing the bit pattern instead would
    /// hand a spring's parameters to the interpolation rule and read its stiffnesses as quaternions.
    /// `STUDIO_PROC_JIGGLE` is 5, whose low bits include 2 — which is exactly what makes a bitwise
    /// test wrong here.
    /// </remarks>
    [Test]
    public void Read_ForAJiggleBone_ReturnsNull()
    {
        byte[] model = Model(
            Bone("bip_hand"),
            Bone("jiggle_hat", control: 0, triggers: 2, procedureType: StudioProcedureType.Jiggle));

        StudioQuatInterp.Read(model, 1).ShouldBeNull();
    }

    /// <remarks>
    /// **A trigger count past the end of the file must refuse rather than read whatever follows.**
    /// A malformed or truncated model is the ordinary case for this project, not the exotic one.
    /// </remarks>
    [Test]
    public void Read_WithMoreTriggersThanTheFileHolds_ReturnsNull()
    {
        byte[] model = Model(Bone("bip_hand"), Bone("hlp_forearm", control: 0, triggers: 2));

        // Claim eight triggers where two were written.
        BinaryPrimitives.WriteInt32LittleEndian(
            model.AsSpan(RuleAt(model, bone: 1) + 4), 8);

        StudioQuatInterp.Read(model, 1).ShouldBeNull();
    }

    /// <remarks>
    /// **A control naming a bone that does not exist must refuse.** The caller indexes the bone
    /// array with it, so a bad value is an exception rather than a wrong picture.
    /// </remarks>
    [Test]
    public void Read_WithAControlOutsideTheBoneTable_ReturnsNull()
    {
        byte[] model = Model(Bone("bip_hand"), Bone("hlp_forearm", control: 0, triggers: 2));

        BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(RuleAt(model, bone: 1)), 99);

        StudioQuatInterp.Read(model, 1).ShouldBeNull();
    }

    /// <summary>Bytes of the fake header before the bone table.</summary>
    private const int HeaderSize = 256;

    /// <summary>Bytes of <c>mstudioquatinterpbone_t</c>.</summary>
    private const int RuleStride = 12;

    /// <summary>Bytes of <c>mstudioquatinterpinfo_t</c>.</summary>
    private const int TriggerStride = 48;

    /// <summary>One bone, and its rule when it has one.</summary>
    private sealed record BuiltBone(string Name, int Control, int Triggers, int ProcedureType);

    private static BuiltBone Bone(
        string name,
        int control = -1,
        int triggers = 0,
        int procedureType = StudioProcedureType.QuaternionInterpolate) =>
        new(name, control, triggers, procedureType);

    /// <summary>Where bone <paramref name="bone"/>'s rule structure begins in the file.</summary>
    private static int RuleAt(byte[] model, int bone)
    {
        int table = BinaryPrimitives.ReadInt32LittleEndian(
            model.AsSpan(StudioLayout.HeaderBoneIndexOffset));

        int start = table + (bone * StudioLayout.BoneStride);

        int index = BinaryPrimitives.ReadInt32LittleEndian(
            model.AsSpan(start + StudioLayout.BoneProcedureIndexOffset));

        return start + index;
    }

    /// <summary>
    /// A minimal <c>.mdl</c>: a header, a bone table, then each bone's rule and triggers.
    /// </summary>
    /// <remarks>
    /// **`procindex` and `triggerindex` are RELATIVE offsets**, the first from the bone's own
    /// address and the second from the rule's — <c>pProcedure()</c> is
    /// <c>((byte *)this) + procindex</c> and <c>pTrigger(i)</c> is
    /// <c>((byte *)this) + triggerindex</c>. Writing either as an absolute file position produces a
    /// reader that works only when the structure happens to sit at zero.
    /// </remarks>
    private static byte[] Model(params BuiltBone[] bones)
    {
        int table = HeaderSize;
        int after = table + (bones.Length * StudioLayout.BoneStride);

        int size = after;

        foreach (BuiltBone bone in bones)
        {
            if (bone.Triggers > 0)
            {
                size += RuleStride + (bone.Triggers * TriggerStride);
            }
        }

        byte[] file = new byte[size];

        Write(file, StudioLayout.HeaderBoneCountOffset, bones.Length);
        Write(file, StudioLayout.HeaderBoneIndexOffset, table);

        int at = after;

        for (int index = 0; index < bones.Length; index++)
        {
            int bone = table + (index * StudioLayout.BoneStride);

            Write(file, bone + StudioLayout.BoneParentOffset, index == 0 ? -1 : 0);

            if (bones[index].Triggers <= 0)
            {
                continue;
            }

            Write(file, bone + StudioLayout.BoneProcedureTypeOffset, bones[index].ProcedureType);
            Write(file, bone + StudioLayout.BoneProcedureIndexOffset, at - bone);

            Write(file, at, bones[index].Control);
            Write(file, at + 4, bones[index].Triggers);
            Write(file, at + 8, RuleStride);

            int trigger = at + RuleStride;

            for (int one = 0; one < bones[index].Triggers; one++)
            {
                // Every field distinct and ascending, so a swapped pair or a wrong stride shows up
                // as a specific wrong number rather than as something plausible.
                for (int field = 0; field < 12; field++)
                {
                    WriteFloat(
                        file,
                        trigger + (one * TriggerStride) + (field * 4),
                        11f + (one * 20f) + field);
                }
            }

            at += RuleStride + (bones[index].Triggers * TriggerStride);
        }

        return file;
    }

    private static void Write(byte[] into, int at, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(into.AsSpan(at), value);

    private static void WriteFloat(byte[] into, int at, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(into.AsSpan(at), value);
}
