using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// How one animation group's bones land on the skeleton being posed: Valve's <c>virtualgroup_t</c> pair of maps, and the
/// bones a sampled pose is seeded from (B621).
/// </summary>
/// <remarks>
/// **One object, so the three cannot disagree.** <c>CalcVirtualAnimation</c> walks the BASE model's bones, seeds base
/// bone <c>i</c> from the group's bone <c>boneMap[i]</c> (<c>bone_setup.cpp:927-947</c>), and writes each track at
/// <c>masterBone[panim-&gt;bone]</c> (<c>:966</c>). Before this, the track map travelled alone and the pose was sized
/// and seeded from the group's own bones index for index, which is right only while the group has the base's order and
/// count. Twelve of 94 shipped include pairs do not (<c>animation-remap</c>, 2026-11-15).
/// </remarks>
public sealed class StudioRemap
{
    private StudioRemap(
        IReadOnlyList<StudioBone> bones, int[]? tracks, int[]? boneMap, IReadOnlyList<StudioBone> seeds)
    {
        Bones = bones;
        Tracks = tracks;
        BoneMap = boneMap;
        Seeds = seeds;
    }

    /// <summary>The bones the group's animations are numbered against: each track's rest values and scales.</summary>
    public IReadOnlyList<StudioBone> Bones { get; }

    /// <summary>For each group bone, its index in the posed skeleton or −1: <c>masterBone</c>. Null for a model's own.</summary>
    internal int[]? Tracks { get; }

    /// <summary>For each posed bone, its index in the group or −1: <c>boneMap</c>. Null for a model's own.</summary>
    internal int[]? BoneMap { get; }

    /// <summary>
    /// One bone per POSED bone: the group's bone through <see cref="BoneMap"/>, the skeleton's own where the group has none.
    /// A sampled pose is sized and seeded from these, and a frame blend reads each bone's fixed-alignment flag from them.
    /// </summary>
    public IReadOnlyList<StudioBone> Seeds { get; }

    /// <summary>A model's own animations: no remap, seeded from its own bones.</summary>
    /// <param name="skeleton">The model's bones.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skeleton"/> is null.</exception>
    public static StudioRemap Own(IReadOnlyList<StudioBone> skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);

        return new StudioRemap(skeleton, null, null, skeleton);
    }

    /// <summary>An included model's animations onto the skeleton that includes it, matched by bone name.</summary>
    /// <param name="skeleton">The posed (base) model's bones.</param>
    /// <param name="group">The included model's own bones.</param>
    /// <returns>Both maps and the seeds.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A base bone the group lacks is never written by <c>CalcVirtualAnimation</c>: a main sequence leaves it at
    /// <c>InitPose</c>'s bind and a layer weighs it zero (<c>SlerpBones</c>, <c>:1417-1431</c>). Its seed is the base's
    /// own bone, and <see cref="SeedInto"/> keeps it at that bind even for a delta.
    /// </remarks>
    public static StudioRemap Onto(IReadOnlyList<StudioBone> skeleton, IReadOnlyList<StudioBone> group)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(group);

        int[] boneMap = StudioBones.Remap(skeleton, group);
        StudioBone[] seeds = new StudioBone[skeleton.Count];

        for (int bone = 0; bone < seeds.Length; bone++)
        {
            seeds[bone] = boneMap[bone] >= 0 ? group[boneMap[bone]] : skeleton[bone];
        }

        return new StudioRemap(group, StudioBones.Remap(group, skeleton), boneMap, seeds);
    }

    /// <summary>A track map given outright, seeded from the group's own bones: the shape the frame-pair reference pins.</summary>
    /// <param name="bones">The group's bones, which are also the seeds.</param>
    /// <param name="tracks">Each group bone's posed index, or −1.</param>
    /// <returns>The descriptor.</returns>
    /// <remarks>Tests only: production builds every remap by name through <see cref="Onto"/>.</remarks>
    internal static StudioRemap Explicit(IReadOnlyList<StudioBone> bones, int[] tracks) =>
        new(bones, tracks, null, bones);

    /// <summary>What a bone a pose does not name holds before it is overlaid: <c>CalcVirtualAnimation</c>'s seed.</summary>
    /// <param name="additive">Whether the animation is <c>STUDIO_DELTA</c>.</param>
    /// <param name="full">Receives one entry per seed.</param>
    /// <remarks>
    /// A mapped bone is seeded identity for a delta and the group's bind otherwise (<c>bone_setup.cpp:933-947</c>). An
    /// unmapped one is never written there, so it holds the base's bind whatever the animation is.
    /// </remarks>
    internal void SeedInto(bool additive, Span<StudioBonePose> full)
    {
        StudioPoseBlend.SeedInto(Seeds, additive, full);

        if (!additive || BoneMap is not { } map)
        {
            return;
        }

        for (int bone = 0; bone < full.Length && bone < map.Length; bone++)
        {
            if (map[bone] < 0)
            {
                StudioBone rest = Seeds[bone];

                full[bone] = new StudioBonePose(bone, rest.Position, rest.Rotation);
            }
        }
    }

    /// <summary>Expands a pose over <see cref="SeedInto"/>'s seed into <paramref name="into"/>.</summary>
    /// <param name="additive">Whether the animation is <c>STUDIO_DELTA</c>.</param>
    /// <param name="pose">The pose, sparse, in posed numbering.</param>
    /// <param name="into">Receives one entry per seed.</param>
    /// <returns><paramref name="into"/>.</returns>
    internal List<StudioBonePose> ExpandInto(bool additive, IReadOnlyList<StudioBonePose> pose, List<StudioBonePose> into)
    {
        CollectionsMarshal.SetCount(into, Seeds.Count);

        Span<StudioBonePose> full = CollectionsMarshal.AsSpan(into);

        SeedInto(additive, full);
        StudioPoseBlend.Overlay(pose, full);

        return into;
    }
}
