using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One bone's rest position in a model's skeleton.</summary>
/// <param name="Name">Its name, which is how one model's bones are matched to another's.</param>
/// <param name="Parent">The bone this hangs off, or −1 for the root.</param>
/// <param name="Position">Where it sits relative to its parent.</param>
/// <param name="Rotation">How it is turned relative to its parent, as a quaternion.</param>
/// <param name="PoseToBone">Model space into this bone's space, as a 3×4 row-major matrix.</param>
/// <param name="Euler">The same rest rotation as radians, which animation adds its angles to.</param>
/// <param name="PositionScale">What an animation's compressed position values are multiplied by.</param>
/// <param name="RotationScale">What an animation's compressed rotation values are multiplied by.</param>
/// <param name="Flags">The <c>BONE_USED_BY_*</c> mask; see <see cref="StudioBoneFlags"/>.</param>
/// <param name="ProcedureType">Which rule computes this bone, or 0 for none.</param>
/// <param name="ProcedureIndex">Where that rule's data sits, RELATIVE TO THE BONE, or 0 for none.</param>
/// <param name="Controllers">
/// Six slots, one per degree of freedom, each a bone controller index or −1.
/// </param>
/// <param name="Alignment">
/// <c>qAlignment</c>: the orientation an animated rotation is aligned to when this bone carries
/// <see cref="StudioBoneFlags.FixedAlignment"/>. Meaningless, and left at zero, for every other
/// bone — the engine reads it only under that flag.
/// </param>
/// <remarks>
/// **The rotation is stored twice, and both are needed.** <c>quat</c> is the rest pose the renderer
/// uses directly; <c>rot</c> is the same rotation as Euler radians, and an animation's compressed
/// channels are added to THAT before being turned back into a quaternion
/// (<c>bone_setup.cpp:417</c>). Adding them to the quaternion instead is meaningless.
///
/// **The last four were added on 2026-08-24 and appended rather than inserted** (D88, B182). The
/// record is positional, so putting a parameter in the middle silently re-maps every call site that
/// passes arguments by position — the compiler only objects when the types happen to differ. Append
/// is the one safe edit.
/// </remarks>
public readonly record struct StudioBone(
    string Name,
    int Parent,
    (float X, float Y, float Z) Position,
    (float X, float Y, float Z, float W) Rotation,
    ReadOnlyMemory<float> PoseToBone,
    (float X, float Y, float Z) Euler = default,
    (float X, float Y, float Z) PositionScale = default,
    (float X, float Y, float Z) RotationScale = default,
    int Flags = 0,
    int ProcedureType = 0,
    int ProcedureIndex = 0,
    ReadOnlyMemory<int> Controllers = default,
    (float X, float Y, float Z, float W) Alignment = default)
{
    /// <summary>Whether this bone is one the engine computes with a rule rather than an animation.</summary>
    /// <remarks>
    /// **The PAIR, not either half.** <c>BuildTransformations</c> tests
    /// <c>(hdr-&gt;boneFlags( i ) &amp; BONE_ALWAYS_PROCEDURAL) &amp;&amp; (pBone-&gt;proctype &amp;
    /// STUDIO_PROC_JIGGLE)</c> (<c>c_baseanimating.cpp:1545</c>), so a bone carrying one without the
    /// other is not what it looks like.
    /// </remarks>
    public bool IsProcedural =>
        (Flags & StudioBoneFlags.AlwaysProcedural) != 0 && ProcedureIndex != 0;

    /// <summary>Whether anything worn may bone-merge onto this bone without widening the mask.</summary>
    /// <remarks>
    /// An unmarked bone does not break a merge — <c>CBoneMergeCache::UpdateCache</c>
    /// (<c>bone_merge_cache.cpp:95</c>) widens the wearer's setup mask to
    /// <c>BONE_USED_BY_ANYTHING</c> instead, so the wearer builds its whole skeleton for every item
    /// worn on it. It is a cost, not a failure, which is why it is worth being able to ask.
    /// </remarks>
    public bool IsMergeTarget => (Flags & StudioBoneFlags.UsedByBoneMerge) != 0;
}

/// <summary>A model's skeleton, resolved to the matrices that move its vertices.</summary>
/// <remarks>
/// **A type rather than an array of arrays**, so the matrices and the multiply that uses them stay
/// together. A caller holding raw matrices has to know the row-major layout to use them, and that
/// knowledge belongs next to the code that built them.
/// </remarks>
public sealed class StudioSkeleton
{
    private readonly float[][] _skinning;
    private readonly float[][] _boneToWorld;

    internal StudioSkeleton(float[][] skinning)
        : this(skinning, skinning)
    {
    }

    internal StudioSkeleton(float[][] skinning, float[][] boneToWorld)
    {
        _skinning = skinning;
        _boneToWorld = boneToWorld;
    }

    /// <summary>Where each bone itself is, before the bind pose is undone.</summary>
    /// <remarks>
    /// **Bone merging needs this and skinning matrices cannot supply it.** A skinning matrix is
    /// <c>boneToWorld * poseToBone</c> — the bind pose is already folded in, and it is the WEARER's
    /// bind pose. Copying one into a worn item's slot is right only where the two models were bound
    /// identically, which is why a fully-matched hat looks fine and a partly-matched one tears: an
    /// unmatched bone has to be built from its parent's position, and a skinning matrix does not
    /// say where its bone is.
    /// </remarks>
    public IReadOnlyList<float[]> BoneToWorld => _boneToWorld;

    /// <summary>How many bones the model has.</summary>
    public int Count => _skinning.Length;

    /// <summary>The matrices themselves, for a renderer that skins on the GPU.</summary>
    /// <remarks>
    /// **Exposed because the transform can happen in two places.** <see cref="Skin"/> applies them
    /// here, which is what a model with its frames baked wants — the work happens once at load. A
    /// model too large to bake is skinned per draw instead, and then the matrices themselves are
    /// what the shader needs, as constants.
    ///
    /// Row-major three-by-four, twelve floats each, which is the studio format's own layout and
    /// the one the shader reads.
    /// </remarks>
    public IReadOnlyList<float[]> Matrices => _skinning;

    /// <summary>Whether the model has a skeleton at all.</summary>
    /// <remarks>
    /// A model compiled with <c>$staticprop</c> still declares one bone, so this being false is
    /// rare; what distinguishes a static prop is that its vertices name no bone weights.
    /// </remarks>
    public bool IsEmpty => _skinning.Length == 0;

    /// <summary>Moves one vertex by its bones, as the renderer would.</summary>
    /// <param name="bones">Which bones move this vertex.</param>
    /// <param name="weights">How much each moves it.</param>
    /// <param name="x">Model-space position.</param>
    /// <param name="y">Model-space position.</param>
    /// <param name="z">Model-space position.</param>
    /// <returns>The vertex in the model's rest pose.</returns>
    /// <remarks>
    /// **A vertex naming no bone is left where it is**, which is the right answer for a model
    /// compiled with <c>$staticprop</c>: its transform is already baked in, and moving it by a
    /// skeleton it does not have would be inventing a pose.
    ///
    /// Weights summing to nothing are treated the same way rather than divided by, since a single
    /// vertex sent to infinity stretches a triangle across the whole map.
    /// </remarks>
    public (float X, float Y, float Z) Skin(
        (byte First, byte Second, byte Third) bones,
        (float First, float Second, float Third) weights,
        float x,
        float y,
        float z)
    {
        if (_skinning.Length == 0)
        {
            return (x, y, z);
        }

        Span<byte> indices = [bones.First, bones.Second, bones.Third];
        Span<float> amounts = [weights.First, weights.Second, weights.Third];

        float total = amounts[0] + amounts[1] + amounts[2];

        if (total <= 0f)
        {
            return (x, y, z);
        }

        float outX = 0f, outY = 0f, outZ = 0f;

        for (int slot = 0; slot < 3; slot++)
        {
            if (amounts[slot] <= 0f || indices[slot] >= _skinning.Length)
            {
                continue;
            }

            float[] matrix = _skinning[indices[slot]];
            float share = amounts[slot] / total;

            outX += share * ((matrix[0] * x) + (matrix[1] * y) + (matrix[2] * z) + matrix[3]);
            outY += share * ((matrix[4] * x) + (matrix[5] * y) + (matrix[6] * z) + matrix[7]);
            outZ += share * ((matrix[8] * x) + (matrix[9] * y) + (matrix[10] * z) + matrix[11]);
        }

        return (outX, outY, outZ);
    }
}

/// <summary>
/// A model's skeleton, and the matrices that put its vertices where they belong.
/// </summary>
/// <remarks>
/// **Why this exists: without it an animated model draws lying on its side.** A model compiled
/// with <c>$staticprop</c> has its bone transform baked into the vertices, so drawing them raw is
/// correct — which is every prop in a map, and is why this went unnoticed for as long as props
/// were the only models. An animated model does not, and its vertices sit in whatever pose the
/// artist built the skeleton around.
///
/// Measured on cp_process_f12, model-space extents before any skinning:
///
/// <code>
///   resupply_locker.mdl      x 63.8   y 35.4    z 113.2    upright, $staticprop
///   cappoint_hologram.mdl    x 39.2   y 64.9    z 171.5    upright, $staticprop
///   player/soldier.mdl       x 47.9   y 84.5    z 24.8     on its side
///   player/scout.mdl         x 30.8   y 81.7    z 22.2     on its side
/// </code>
///
/// A TF2 player is about 83 units tall, and there is the 83 — on Y. The props are the control:
/// same reader, same code path, standing up correctly.
///
/// **The transform is Valve's, from <c>bone_setup.cpp</c>:** a vertex is moved by
/// <c>BoneToWorld × poseToBone</c> per bone, weighted. <c>poseToBone</c> takes model space into a
/// bone's own space (it is inverted at <c>bone_setup.cpp:1775</c> to go the other way), and
/// <c>BoneToWorld</c> is built by walking the hierarchy from each bone's rest
/// <c>pos</c>/<c>quat</c>. In the rest pose the two are inverses for a model whose vertices were
/// stored in that same pose, and the product is the identity — which is exactly why static props
/// look right without this and cost nothing when it is applied.
///
/// This is the REST pose only. Animation replaces <c>BoneToWorld</c> with a posed one; the
/// <c>poseToBone</c> half never changes.
/// </remarks>
public static class StudioBones
{
    /// <summary>Most bones a model may declare, as a guard against a malformed header.</summary>
    private const int MaximumBones = StudioReaderLimits.Bones;

    /// <summary>Reads a model's skeleton.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The bones in file order, so a parent index addresses this list directly.</returns>
    /// <exception cref="InvalidDataException">The header names more bones than it holds.</exception>
    public static IReadOnlyList<StudioBone> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderBoneIndexOffset + 4)
        {
            return [];
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneCountOffset..]);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderBoneIndexOffset..]);

        if (count <= 0)
        {
            return [];
        }

        if (count > MaximumBones)
        {
            throw new InvalidDataException($"A model declares {count} bones.");
        }

        if (at < 0 || (long)at + ((long)count * BoneStride) > bytes.Length)
        {
            throw new InvalidDataException(
                $"A model's {count} bones at {at} run past its own length of {bytes.Length}.");
        }

        List<StudioBone> bones = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> bone = bytes.Slice(at + (index * BoneStride), BoneStride);

            float[] poseToBone = new float[12];

            for (int cell = 0; cell < 12; cell++)
            {
                poseToBone[cell] =
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePoseToBoneOffset + (cell * 4))..]);
            }

            // Six signed slots, one per degree of freedom, each a controller index or −1. Read as a
            // run because the ORDER is the meaning: slot 3 is XR, not Z.
            int[] controllers = new int[BoneControllerSlots];

            for (int slot = 0; slot < BoneControllerSlots; slot++)
            {
                controllers[slot] = BinaryPrimitives.ReadInt32LittleEndian(
                    bone[(BoneControllerListOffset + (slot * 4))..]);
            }

            bones.Add(new StudioBone(
                StudioStrings.At(
                    bytes,
                    at + (index * BoneStride) +
                        BinaryPrimitives.ReadInt32LittleEndian(bone[BoneNameOffset..])),
                BinaryPrimitives.ReadInt32LittleEndian(bone[BoneParentOffset..]),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[BonePositionOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePositionOffset +4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BonePositionOffset +8)..])),
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[BoneRotationOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +8)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneRotationOffset +12)..])),
                poseToBone,
                Vector(bone, BoneEulerOffset),
                Vector(bone, BonePositionScaleOffset),
                Vector(bone, BoneRotationScaleOffset),

                // **The mask the engine gates its whole bone pipeline on**, unread here until
                // 2026-08-24 (B182). BuildTransformations skips a bone outright when it does not
                // intersect the caller's boneMask (c_baseanimating.cpp:1516).
                BinaryPrimitives.ReadInt32LittleEndian(bone[BoneFlagsOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(bone[BoneProcedureTypeOffset..]),

                // Relative to the BONE, and zero means "no rule" rather than "the start of the
                // file" — pProcedure() returns null for zero. Kept as the raw value so the
                // distinction survives to whoever resolves it.
                BinaryPrimitives.ReadInt32LittleEndian(bone[BoneProcedureIndexOffset..]),
                controllers,

                // **`qAlignment`, in the gap between `poseToBone` and `flags`** (B308). Read
                // unconditionally because the flag that decides whether it MEANS anything lives
                // beside it; the decode consults both.
                (
                    BinaryPrimitives.ReadSingleLittleEndian(bone[BoneAlignmentOffset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneAlignmentOffset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneAlignmentOffset + 8)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(bone[(BoneAlignmentOffset + 12)..]))));
        }

        return bones;
    }

    /// <summary>The matrices for a skeleton with an animation applied.</summary>
    /// <param name="bones">The skeleton, as <see cref="Read"/> returned it.</param>
    /// <param name="pose">Bone poses from an animation frame; bones it omits keep their rest value.</param>
    /// <returns>One matrix per bone.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **This is the half that changes.** <c>poseToBone</c> is fixed by the model; what an
    /// animation replaces is the other factor, the bone's transform relative to its parent. So
    /// posing is the rest computation with different local transforms, not a different
    /// computation.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    /// <remarks>
    /// **<c>BoneToWorld × poseToBone</c>, which is Valve's own composition.** Each bone's world
    /// transform is its rest <c>pos</c>/<c>quat</c> concatenated onto its parent's, walking down
    /// from the root; <c>poseToBone</c> then brings a model-space vertex into that bone's space
    /// first.
    ///
    /// **Parents always precede children in the file**, which is what makes a single forward pass
    /// correct rather than needing a recursive walk. A model that violated it would produce a
    /// child built on an unfinished parent, so the parent index is bounds-checked against the
    /// bones already computed rather than against the whole list — a malformed skeleton then draws
    /// its own bone unmoved instead of reading a matrix that is still zeroes.
    /// </remarks>
    public static StudioSkeleton Posed(
        IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(pose);

        if (pose.Count == 0)
        {
            return RestPose(bones);
        }

        // **A bone the animation does not mention keeps its rest value**, which is most of the
        // skeleton for most animations: an animation that only turns an elbow names one bone.
        StudioBone[] posed = [.. bones];

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < posed.Length)
            {
                posed[moved.Bone] = posed[moved.Bone] with
                {
                    Position = moved.Position,
                    Rotation = moved.Rotation,
                };
            }
        }

        return RestPose(posed);
    }

    /// <summary>The matrix that moves each bone's vertices into the model's rest pose.</summary>
    /// <param name="bones">The skeleton, as <see cref="Read"/> returned it.</param>
    /// <returns>One matrix per bone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bones"/> is null.</exception>
    public static StudioSkeleton RestPose(IReadOnlyList<StudioBone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);

        float[][] boneToWorld = new float[bones.Count][];
        float[][] skinning = new float[bones.Count][];

        for (int index = 0; index < bones.Count; index++)
        {
            StudioBone bone = bones[index];

            float[] local = FromQuaternion(bone.Rotation, bone.Position);

            boneToWorld[index] = bone.Parent >= 0 && bone.Parent < index
                ? Concatenate(boneToWorld[bone.Parent], local)
                : local;

            skinning[index] = Concatenate(boneToWorld[index], bone.PoseToBone.Span);
        }

        return new StudioSkeleton(skinning, boneToWorld);
    }

    /// <summary>Poses one model's bones from another's, the way a bone merge does.</summary>
    /// <param name="bones">The worn model's skeleton, which decides the numbering of the result.</param>
    /// <param name="wearer">Where the wearer's bones are, as <see cref="StudioSkeleton.BoneToWorld"/>.</param>
    /// <param name="map">
    /// For each of <paramref name="bones"/>, the wearer bone it matches, or −1. <see cref="Remap"/>
    /// produces it.
    /// </param>
    /// <returns>Skinning matrices in the worn model's bone order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Valve copies only the bones that match, and the rest are NOT left alone.** <c>
    /// CBoneMergeCache::MergeMatchingBones</c> runs after the worn model has done its own full
    /// <c>SetupBones</c>, so an unmatched bone already holds a position built by walking the worn
    /// model's own hierarchy — from its parent, which may itself have been merged. Copying the
    /// matches and leaving everything else at its rest position in the model's OWN space is a
    /// different thing entirely, and it tears the model apart: measured on a scout, a
    /// <c>ghostly_gibus</c> matched 1 bone of 8 and the other seven stayed at the model origin
    /// while the matched one sat at head height, so the triangles between them stretched from the
    /// player's head to their feet as a large flat sheet.
    ///
    /// So the chain is walked here, in bone-to-world space, and only then folded with the WORN
    /// model's own <c>poseToBone</c> — its bind pose, not the wearer's.
    ///
    /// **Bones are in hierarchy order and a parent's index is below its child's.** The studio
    /// format guarantees it, and <see cref="RestPose"/> already relies on the same thing, so one
    /// pass suffices.
    /// </remarks>
    public static IReadOnlyList<float[]> MergeOnto(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<float[]> wearer,
        IReadOnlyList<int> map)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(wearer);
        ArgumentNullException.ThrowIfNull(map);

        float[][] boneToWorld = new float[bones.Count][];
        float[][] skinning = new float[bones.Count][];

        for (int index = 0; index < bones.Count; index++)
        {
            StudioBone bone = bones[index];
            int matched = index < map.Count ? map[index] : -1;

            if (matched >= 0 && matched < wearer.Count)
            {
                boneToWorld[index] = wearer[matched];
            }
            else
            {
                float[] local = FromQuaternion(bone.Rotation, bone.Position);

                boneToWorld[index] = bone.Parent >= 0 && bone.Parent < index
                    ? Concatenate(boneToWorld[bone.Parent], local)
                    : local;
            }

            skinning[index] = Concatenate(boneToWorld[index], bone.PoseToBone.Span);
        }

        return skinning;
    }

    /// <summary>Maps one model's bone numbering onto another's, by name.</summary>
    /// <param name="from">The bones an animation's indices refer to.</param>
    /// <param name="to">The bones a pose is to be applied to.</param>
    /// <returns>An index into <paramref name="to"/> for each bone in <paramref name="from"/>, or −1.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **This is Valve's <c>masterBone</c>**, which <c>studio.h</c> describes as mapping a local
    /// bone to a global one and which <c>bone_setup.cpp:966</c> applies to every animation it
    /// reads: <c>int j = pAnimGroup-&gt;masterBone[panim-&gt;bone];</c>.
    ///
    /// **Without it a pose is scrambled rather than absent, which is worse.** An animation model
    /// numbers its own bones, and applying those numbers to the base model's skeleton moves the
    /// wrong joints by the right amounts — measured on a soldier as extents of 56 by 66 by 65,
    /// roughly cubical, where a standing player is about 25 by 48 by 83. It looked like a model
    /// sitting up rather than one lying down, which is exactly what a partly-correct skeleton is.
    ///
    /// Matched by name because that is what makes an animation model shareable in the first place.
    /// </remarks>
    public static int[] Remap(IReadOnlyList<StudioBone> from, IReadOnlyList<StudioBone> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < to.Count; index++)
        {
            byName.TryAdd(to[index].Name, index);
        }

        int[] remap = new int[from.Count];

        for (int index = 0; index < from.Count; index++)
        {
            remap[index] = byName.TryGetValue(from[index].Name, out int found) ? found : -1;
        }

        return remap;
    }

    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> bone, int at) =>
        (
            BinaryPrimitives.ReadSingleLittleEndian(bone[at..]),
            BinaryPrimitives.ReadSingleLittleEndian(bone[(at + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bone[(at + 8)..]));

    /// <summary>A quaternion and a position as a 3×4 row-major matrix.</summary>
    /// <remarks>
    /// Valve's <c>QuaternionMatrix</c>, which is the standard expansion. Written out rather than
    /// called through a vector library so the row-major layout is visible beside the multiply in
    /// <see cref="StudioSkeleton.Skin"/> — a transposed rotation is the classic silent failure here, and it looks
    /// like a model turned inside out rather than like an error.
    /// </remarks>
    /// <param name="rotation">The bone's rotation.</param>
    /// <param name="position">Where it sits relative to its parent.</param>
    /// <returns>Twelve floats, row-major 3×4.</returns>
    /// <remarks>
    /// **Public since 2026-08-24 because the bone pipeline needs the same convention** (D88). Two
    /// copies of this expansion in one solution is the drift this project has been bitten by
    /// before, and a transposed rotation is the classic silent failure: it looks like a model
    /// turned inside out rather than like an error.
    /// </remarks>
    public static float[] FromQuaternion(
        (float X, float Y, float Z, float W) rotation, (float X, float Y, float Z) position)
    {
        float[] matrix = new float[12];

        FromQuaternion(rotation, position, matrix);

        return matrix;
    }
    /// <summary>A quaternion and a position as a 3×4, written into an existing array.</summary>
    /// <param name="rotation">The bone's rotation.</param>
    /// <param name="position">Where it sits relative to its parent.</param>
    /// <param name="matrix">Where the twelve floats go.</param>
    /// <remarks>
    /// **The allocation-free form, for the per-frame path.** The bone pipeline calls this once per
    /// bone per entity per frame — a player is around eighty bones and a match has two dozen
    /// players — so returning a fresh array each time is megabytes a second through the collector.
    /// Measured 2026-08-25: 34 gen0 collections a second with the allocating form.
    /// </remarks>
    public static void FromQuaternion(
        (float X, float Y, float Z, float W) rotation,
        (float X, float Y, float Z) position,
        Span<float> matrix)
    {
        float x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;

        matrix[0] = 1f - (2f * ((y * y) + (z * z)));
        matrix[1] = 2f * ((x * y) - (z * w));
        matrix[2] = 2f * ((x * z) + (y * w));
        matrix[3] = position.X;

        matrix[4] = 2f * ((x * y) + (z * w));
        matrix[5] = 1f - (2f * ((x * x) + (z * z)));
        matrix[6] = 2f * ((y * z) - (x * w));
        matrix[7] = position.Y;

        matrix[8] = 2f * ((x * z) - (y * w));
        matrix[9] = 2f * ((y * z) + (x * w));
        matrix[10] = 1f - (2f * ((x * x) + (y * y)));
        matrix[11] = position.Z;
    }

    /// <summary>Spherically interpolates one rotation toward another, aligning them first.</summary>
    /// <param name="from">The rotation at <paramref name="fraction"/> zero.</param>
    /// <param name="to">The rotation at <paramref name="fraction"/> one.</param>
    /// <param name="fraction">How far to travel.</param>
    /// <returns>The blended rotation.</returns>
    /// <remarks>
    /// **<c>QuaternionSlerp</c>, <c>mathlib_base.cpp:1605</c>**, which is two steps:
    ///
    /// <code>
    ///   QuaternionAlign( p, q, q2 );          // negate q if it points the long way round
    ///   QuaternionSlerpNoAlign( p, q2, t, qt );
    /// </code>
    ///
    /// **The alignment is not optional and is the trap.** A quaternion and its negation are the same
    /// rotation, so an unaligned blend can travel 300 degrees the wrong way to reach a pose 60
    /// degrees away — a limb that swings through the body instead of to its target.
    /// <c>System.Numerics.Quaternion.Slerp</c> does the same negation on a negative dot product,
    /// which is why this delegates rather than reimplementing the trigonometry.
    ///
    /// **Not <c>QuaternionSlerpNoAlign</c>**, which the engine reaches only for a bone flagged
    /// <c>BONE_FIXED_ALIGNMENT</c> (<c>bone_setup.cpp:1492</c>) — see
    /// <see cref="SlerpNoAlign"/>, and <see cref="StudioBoneFlags.FixedAlignment"/> for why an
    /// animator would ask for it.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Slerp(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction)
    {
        Quaternion blended = Quaternion.Slerp(
            new Quaternion(from.X, from.Y, from.Z, from.W),
            new Quaternion(to.X, to.Y, to.Z, to.W),
            fraction);

        return (blended.X, blended.Y, blended.Z, blended.W);
    }

    /// <summary>Spherically interpolates without aligning first — <c>QuaternionSlerpNoAlign</c>.</summary>
    /// <param name="from">The rotation at <paramref name="fraction"/> zero.</param>
    /// <param name="to">The rotation at <paramref name="fraction"/> one.</param>
    /// <param name="fraction">How far to travel.</param>
    /// <returns>The blended rotation.</returns>
    /// <remarks>
    /// **<c>mathlib_base.cpp:1617</c>**, reached for a bone carrying
    /// <see cref="StudioBoneFlags.FixedAlignment"/>:
    ///
    /// <code>
    ///   cosom = p[0]*q[0] + p[1]*q[1] + p[2]*q[2] + p[3]*q[3];
    ///   if ((1.0f + cosom) &gt; 0.000001f) {
    ///       if ((1.0f - cosom) &gt; 0.000001f) {
    ///           omega = acos( cosom ); sinom = sin( omega );
    ///           sclp = sin( (1.0f - t)*omega) / sinom; sclq = sin( t*omega ) / sinom;
    ///       } else { sclp = 1.0f - t; sclq = t; }
    ///       for (i = 0; i &lt; 4; i++) qt[i] = sclp * p[i] + sclq * q[i];
    ///   } else {
    ///       qt[0] = -q[1]; qt[1] = q[0]; qt[2] = -q[3]; qt[3] = q[2];
    ///       sclp = sin( (1.0f - t) * (0.5f * M_PI) ); sclq = sin( t * (0.5f * M_PI) );
    ///       for (i = 0; i &lt; 3; i++) qt[i] = sclp * p[i] + sclq * qt[i];
    ///   }
    /// </code>
    ///
    /// **Written out rather than delegated, which <see cref="Slerp"/> can do and this cannot.**
    /// `System.Numerics.Quaternion.Slerp` negates the target on a negative dot product — that IS the
    /// alignment — so there is no way to ask it not to. The whole point here is the blend without
    /// that step.
    ///
    /// **The antipodal arm is not a rounding guard, it is a different rotation.** When the two
    /// quaternions are opposite there is no shorter arc to take, so Valve builds a perpendicular
    /// from the target's own components and sweeps a quarter turn through it. Note its loop runs to
    /// THREE, leaving <c>qt[3]</c> as the perpendicular's own <c>q[2]</c> — reproduced, because a
    /// fourth iteration is the obvious tidy-up and would change the result.
    /// </remarks>
    public static (float X, float Y, float Z, float W) SlerpNoAlign(
        (float X, float Y, float Z, float W) from,
        (float X, float Y, float Z, float W) to,
        float fraction)
    {
        const float Epsilon = 0.000001f;

        float cosine = (from.X * to.X) + (from.Y * to.Y) + (from.Z * to.Z) + (from.W * to.W);

        if (1f + cosine > Epsilon)
        {
            float fromShare;
            float toShare;

            if (1f - cosine > Epsilon)
            {
                float omega = MathF.Acos(cosine);
                float sine = MathF.Sin(omega);

                fromShare = MathF.Sin((1f - fraction) * omega) / sine;
                toShare = MathF.Sin(fraction * omega) / sine;
            }
            else
            {
                fromShare = 1f - fraction;
                toShare = fraction;
            }

            return (
                (fromShare * from.X) + (toShare * to.X),
                (fromShare * from.Y) + (toShare * to.Y),
                (fromShare * from.Z) + (toShare * to.Z),
                (fromShare * from.W) + (toShare * to.W));
        }

        // The antipodal case: a perpendicular built from the target, swept a quarter turn.
        (float X, float Y, float Z, float W) perpendicular = (-to.Y, to.X, -to.W, to.Z);

        float quarter = 0.5f * MathF.PI;
        float fromArc = MathF.Sin((1f - fraction) * quarter);
        float toArc = MathF.Sin(fraction * quarter);

        // Valve's loop runs to three, so W keeps the perpendicular's value untouched.
        return (
            (fromArc * from.X) + (toArc * perpendicular.X),
            (fromArc * from.Y) + (toArc * perpendicular.Y),
            (fromArc * from.Z) + (toArc * perpendicular.Z),
            perpendicular.W);
    }

    /// <summary>Scales a rotation toward identity — <c>QuaternionScale</c>.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <param name="scale">How much of it to keep.</param>
    /// <returns>The scaled rotation.</returns>
    /// <remarks>
    /// **<c>mathlib_base.cpp:1757</c>**, and it is NOT a component multiply. The angle is scaled,
    /// not the vector:
    ///
    /// <code>
    ///   float sinom = sqrt( DotProduct( &amp;p.x, &amp;p.x ) );
    ///   sinom = min( sinom, 1.f );
    ///   float sinsom = sin( asin( sinom ) * t );
    ///   t = sinsom / (sinom + FLT_EPSILON);
    ///   VectorScale( &amp;p.x, t, &amp;q.x );
    ///   r = 1.0f - sinsom * sinsom;
    ///   if (r &lt; 0.0f) r = 0.0f;
    ///   r = sqrt( r );
    ///   if (p.w &lt; 0) q.w = -r; else q.w = r;
    /// </code>
    ///
    /// The sign of <c>w</c> is carried across deliberately — Valve's own comment says *"keep sign
    /// of rotation"* — because a quaternion and its negation are the same rotation and dropping the
    /// sign here would send a scaled delta the long way round.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Scale(
        (float X, float Y, float Z, float W) rotation, float scale)
    {
        float sine = MathF.Sqrt(
            (rotation.X * rotation.X) + (rotation.Y * rotation.Y) + (rotation.Z * rotation.Z));

        sine = MathF.Min(sine, 1f);

        float scaled = MathF.Sin(MathF.Asin(sine) * scale);
        float share = scaled / (sine + float.Epsilon);

        float remainder = 1f - (scaled * scaled);

        if (remainder < 0f)
        {
            remainder = 0f;
        }

        remainder = MathF.Sqrt(remainder);

        return (
            rotation.X * share,
            rotation.Y * share,
            rotation.Z * share,
            rotation.W < 0f ? -remainder : remainder);
    }

    /// <summary>One rotation applied after another — <c>QuaternionMult</c>.</summary>
    /// <param name="first">The rotation applied second, Valve's <c>p</c>.</param>
    /// <param name="second">The rotation applied first, Valve's <c>q</c>.</param>
    /// <returns>The product.</returns>
    /// <remarks>
    /// **<c>mathlib_base.cpp:1837</c>**, and it aligns before multiplying:
    /// <c>QuaternionAlign( p, q, q2 )</c> negates the second when the two point opposite ways, for
    /// the same reason <see cref="Slerp"/> does. <c>System.Numerics</c> multiplies without
    /// aligning, so this cannot delegate.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Multiply(
        (float X, float Y, float Z, float W) first,
        (float X, float Y, float Z, float W) second)
    {
        (float X, float Y, float Z, float W) aligned = Align(first, second);

        return (
            (first.X * aligned.W) + (first.Y * aligned.Z) -
                (first.Z * aligned.Y) + (first.W * aligned.X),
            (-first.X * aligned.Z) + (first.Y * aligned.W) +
                (first.Z * aligned.X) + (first.W * aligned.Y),
            (first.X * aligned.Y) - (first.Y * aligned.X) +
                (first.Z * aligned.W) + (first.W * aligned.Z),
            (-first.X * aligned.X) - (first.Y * aligned.Y) -
                (first.Z * aligned.Z) + (first.W * aligned.W));
    }

    /// <summary>Negates a rotation when it points the long way round — <c>QuaternionAlign</c>.</summary>
    /// <param name="to">The rotation to align against.</param>
    /// <param name="rotation">The rotation to align.</param>
    /// <returns>The aligned rotation.</returns>
    /// <remarks>
    /// **<c>mathlib_base.cpp:1509</c>**, compared as sums of squares rather than by a dot product,
    /// which Valve marks as a possible simplification. Kept in that form because the two agree in sign
    /// and the comparison is the documented one.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Align(
        (float X, float Y, float Z, float W) to,
        (float X, float Y, float Z, float W) rotation)
    {
        float apart =
            ((to.X - rotation.X) * (to.X - rotation.X)) +
            ((to.Y - rotation.Y) * (to.Y - rotation.Y)) +
            ((to.Z - rotation.Z) * (to.Z - rotation.Z)) +
            ((to.W - rotation.W) * (to.W - rotation.W));

        float together =
            ((to.X + rotation.X) * (to.X + rotation.X)) +
            ((to.Y + rotation.Y) * (to.Y + rotation.Y)) +
            ((to.Z + rotation.Z) * (to.Z + rotation.Z)) +
            ((to.W + rotation.W) * (to.W + rotation.W));

        return apart > together
            ? (-rotation.X, -rotation.Y, -rotation.Z, -rotation.W)
            : rotation;
    }

    /// <summary>Adds a scaled delta BEFORE a rotation — <c>QuaternionSM</c>.</summary>
    /// <param name="scale">How much of the delta to apply.</param>
    /// <param name="delta">The additive rotation.</param>
    /// <param name="onto">What it is added to.</param>
    /// <returns>The result, normalised.</returns>
    /// <remarks>
    /// **<c>bone_setup.cpp:1165</c>**: <c>QuaternionScale( p, s, p1 ); QuaternionMult( p1, q, q1 );
    /// QuaternionNormalize( q1 );</c> — the default composition for a
    /// <c>STUDIO_DELTA</c> layer, used whenever <c>STUDIO_POST</c> is absent.
    /// </remarks>
    public static (float X, float Y, float Z, float W) ScaleBefore(
        float scale,
        (float X, float Y, float Z, float W) delta,
        (float X, float Y, float Z, float W) onto) =>
        NormalizeRotation(Multiply(Scale(delta, scale), onto));

    /// <summary>Adds a scaled delta AFTER a rotation — <c>QuaternionMA</c>.</summary>
    /// <param name="onto">What the delta is added to.</param>
    /// <param name="scale">How much of the delta to apply.</param>
    /// <param name="delta">The additive rotation.</param>
    /// <returns>The result, normalised.</returns>
    /// <remarks>
    /// **<c>bone_setup.cpp:1192</c>**, Valve's own comment: <c>qt = p * ( s * q )</c>. Reached only
    /// for a delta layer whose sequence also carries <c>STUDIO_POST</c>.
    /// </remarks>
    public static (float X, float Y, float Z, float W) ScaleAfter(
        (float X, float Y, float Z, float W) onto,
        float scale,
        (float X, float Y, float Z, float W) delta) =>
        NormalizeRotation(Multiply(onto, Scale(delta, scale)));

    /// <summary>A rotation scaled to unit length, or identity when it has none.</summary>
    /// <param name="rotation">The rotation.</param>
    /// <returns>The normalised rotation.</returns>
    private static (float X, float Y, float Z, float W) NormalizeRotation(
        (float X, float Y, float Z, float W) rotation)
    {
        float length = MathF.Sqrt(
            (rotation.X * rotation.X) + (rotation.Y * rotation.Y) +
            (rotation.Z * rotation.Z) + (rotation.W * rotation.W));

        return length > 0f
            ? (rotation.X / length, rotation.Y / length, rotation.Z / length, rotation.W / length)
            : (0f, 0f, 0f, 1f);
    }


    /// <summary>One 3×4 transform applied after another.</summary>
    /// <param name="first">The outer transform, applied second.</param>
    /// <param name="second">The inner transform, applied first.</param>
    /// <returns>Twelve floats, row-major 3×4.</returns>
    public static float[] Concatenate(ReadOnlySpan<float> first, ReadOnlySpan<float> second)
    {
        float[] result = new float[12];

        Concatenate(first, second, result);

        return result;
    }

    /// <summary>One 3×4 transform applied after another, written into an existing array.</summary>
    /// <param name="first">The outer transform, applied second.</param>
    /// <param name="second">The inner transform, applied first.</param>
    /// <param name="result">Where the twelve floats go.</param>
    /// <remarks>
    /// **The allocation-free form, for the per-frame path.** The bone pipeline runs this once per
    /// bone per entity per frame — a player is around eighty bones and a match has two dozen
    /// players — so returning a fresh array each time puts kilobytes a frame through the collector
    /// for no reason. D87's argument in miniature: a frame has a deadline.
    ///
    /// **<paramref name="result"/> may not alias either input.** Each output cell is written before
    /// the later cells of the same row are read, so writing into <paramref name="first"/> would
    /// feed partly-updated values back into the multiply. Callers pass a distinct destination; the
    /// bone accessor's arrays are per bone, so they never alias their own parent.
    /// </remarks>
    public static void Concatenate(
        ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> result)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                result[(row * 4) + column] =
                    (first[(row * 4) + 0] * second[column]) +
                    (first[(row * 4) + 1] * second[4 + column]) +
                    (first[(row * 4) + 2] * second[8 + column]);
            }

            result[(row * 4) + 3] =
                (first[(row * 4) + 0] * second[3]) +
                (first[(row * 4) + 1] * second[7]) +
                (first[(row * 4) + 2] * second[11]) +
                first[(row * 4) + 3];
        }
    }

    /// <summary>The inverse of a bone matrix — <c>MatrixInvert</c>.</summary>
    /// <param name="matrix">The matrix, row-major 3x4.</param>
    /// <param name="result">Where the inverse goes; may not alias the input.</param>
    /// <exception cref="ArgumentException">Either span is not twelve floats.</exception>
    /// <remarks>
    /// **<c>mathlib_base.cpp:380</c>**, and it inverts by TRANSPOSING, which is only the inverse
    /// for a rotation:
    ///
    /// <code>
    ///   out[0][0] = in[0][0]; out[0][1] = in[1][0]; out[0][2] = in[2][0];  // transpose
    ///   ...
    ///   tmp[0] = in[0][3]; tmp[1] = in[1][3]; tmp[2] = in[2][3];
    ///   out[0][3] = -DotProduct( tmp, out[0] );                            // and re-space the position
    /// </code>
    ///
    /// **A SCALED matrix inverts wrongly through this**, and that is Valve's behaviour rather than
    /// an oversight to fix: a transpose undoes a rotation and multiplies a scale instead of
    /// dividing it. Bone matrices are rotations plus translations, and the one place a scale enters
    /// is the model scale, which is applied outside the bone hierarchy.
    ///
    /// **The position is not simply negated.** It has to be expressed in the inverted frame, which
    /// is the three dot products — negating it in place would put every child bone somewhere
    /// plausible and wrong.
    /// </remarks>
    public static void Invert(ReadOnlySpan<float> matrix, Span<float> result)
    {
        if (matrix.Length != 12 || result.Length != 12)
        {
            throw new ArgumentException("A bone matrix is a matrix3x4_t of twelve floats.");
        }

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                result[(row * 4) + column] = matrix[(column * 4) + row];
            }
        }

        float x = matrix[3];
        float y = matrix[7];
        float z = matrix[11];

        for (int row = 0; row < 3; row++)
        {
            result[(row * 4) + 3] = -(
                (x * result[(row * 4) + 0]) +
                (y * result[(row * 4) + 1]) +
                (z * result[(row * 4) + 2]));
        }
    }

    /// <summary>A bone matrix's rotation, as a quaternion — <c>MatrixAngles</c>.</summary>
    /// <param name="matrix">The matrix, row-major 3x4.</param>
    /// <returns>Its rotation, normalised.</returns>
    /// <exception cref="ArgumentException"><paramref name="matrix"/> is not twelve floats.</exception>
    /// <remarks>
    /// **<c>mathlib_base.cpp:150</c>**, and the four branches are not interchangeable. The first
    /// uses the trace, which is numerically best when the rotation is small; the other three pick
    /// whichever diagonal element is largest, because the trace form divides by something near zero
    /// for a rotation near half a turn. Choosing one branch for everything is the plausible
    /// simplification and it loses precision exactly where a limb is bent furthest.
    ///
    /// **Valve normalises at the end rather than scaling each branch**, so the four expressions
    /// need only be proportional to the answer — which is why they look unrelated to each other.
    /// </remarks>
    public static (float X, float Y, float Z, float W) ToQuaternion(ReadOnlySpan<float> matrix)
    {
        if (matrix.Length != 12)
        {
            throw new ArgumentException("A bone matrix is a matrix3x4_t of twelve floats.");
        }

        float m00 = matrix[0];
        float m11 = matrix[5];
        float m22 = matrix[10];

        float trace = m00 + m11 + m22 + 1f;

        float x;
        float y;
        float z;
        float w;

        if (trace > 1f + float.Epsilon)
        {
            x = matrix[9] - matrix[6];
            y = matrix[2] - matrix[8];
            z = matrix[4] - matrix[1];
            w = trace;
        }
        else if (m00 > m11 && m00 > m22)
        {
            x = 1f + m00 - m11 - m22;
            y = matrix[4] + matrix[1];
            z = matrix[2] + matrix[8];
            w = matrix[9] - matrix[6];
        }
        else if (m11 > m22)
        {
            x = matrix[1] + matrix[4];
            y = 1f + m11 - m00 - m22;
            z = matrix[9] + matrix[6];
            w = matrix[2] - matrix[8];
        }
        else
        {
            x = matrix[2] + matrix[8];
            y = matrix[9] + matrix[6];
            z = 1f + m22 - m00 - m11;
            w = matrix[4] - matrix[1];
        }

        float length = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        return length > 0f
            ? (x / length, y / length, z / length, w / length)
            : (0f, 0f, 0f, 1f);
    }

}
