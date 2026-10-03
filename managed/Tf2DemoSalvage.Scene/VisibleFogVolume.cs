using System;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary><c>VisibleFogVolumeInfo_t</c> (<c>public/ivrenderview.h:102</c>), less the material, which is the caller's lookup.</summary>
/// <param name="Volume"><c>m_nVisibleFogVolume</c>, a water data ID, or −1.</param>
/// <param name="Leaf"><c>m_nVisibleFogVolumeLeaf</c>, or −1.</param>
/// <param name="EyeInFogVolume"><c>m_bEyeInFogVolume</c>.</param>
/// <param name="DistanceToWater"><c>m_flDistanceToWater</c>.</param>
/// <param name="WaterHeight"><c>m_flWaterHeight</c>.</param>
/// <param name="SurfaceTexinfo">The volume's surface texinfo, −1 for none. From inside the volume the engine draws with
/// that material's <c>$bottommaterial</c> instead (engine.dll <c>0x1800dffd0</c>).</param>
public readonly record struct FogVolumeInfo(
    int Volume, int Leaf, bool EyeInFogVolume, float DistanceToWater, float WaterHeight, int SurfaceTexinfo)
{
    /// <summary>No visible volume.</summary>
    /// <remarks>The height is the engine's fallback constant at <c>0x180367cc0</c>, read as 0 — <i>interpolated</i>:
    /// nothing reads the height without a volume.</remarks>
    public static FogVolumeInfo None(float distanceToWater) => new(-1, -1, false, distanceToWater, 0f, -1);
}

/// <summary><c>R_GetVisibleFogVolume</c>, engine.dll <c>0x1800e0080</c> — closed, read in disassembly.</summary>
/// <remarks>
/// **From the eye's leaf.** A water leaf's own volume, eye inside. Otherwise only a leaf flagged
/// <c>CONTENTS_TESTFOGVOLUME</c> (0x100, vbsp's mark for "water may be in view from here") looks further: a front-to-back
/// walk of the tree from the eye (<c>0x1800e0f70</c>) returning the first leaf that is visible this frame, inside the
/// frustum, has a water data ID and is not <c>CONTENTS_SLIME</c>. The <c>fast_fogvolume</c> shortcut (a map with exactly
/// one volume takes it unwalked) is off by default — its ConVar's default string is "0" (<c>0x18035de18</c>).
/// </remarks>
public static class VisibleFogVolume
{
    /// <summary><c>CONTENTS_TESTFOGVOLUME</c>, <c>public/bspflags.h</c>.</summary>
    public const int ContentsTestFogVolume = 0x100;

    /// <summary>Finds the fog volume the eye sees.</summary>
    /// <param name="tree">The map's tree.</param>
    /// <param name="water">The map's water.</param>
    /// <param name="eye">The eye.</param>
    /// <param name="leafVisible">Whether a leaf is in this frame's visible set.</param>
    /// <param name="boxInView">Whether a node or leaf box is inside the view frustum.</param>
    /// <returns>The info.</returns>
    public static FogVolumeInfo Find(
        BspLeafTree tree,
        BspWater water,
        (float X, float Y, float Z) eye,
        Func<int, bool> leafVisible,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> boxInView)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(leafVisible);
        ArgumentNullException.ThrowIfNull(boxInView);

        if (tree.IsEmpty)
        {
            // 0x1800e016a — no world: 1e6, the float at 0x1800e0179.
            return FogVolumeInfo.None(1e6f);
        }

        int eyeLeaf = tree.LeafAt(eye.X, eye.Y, eye.Z);
        float distance = water.DistanceToWater(eyeLeaf);

        int inside = tree.WaterDataId(eyeLeaf);

        if (inside != -1)
        {
            return Info(inside, eyeLeaf, true);
        }

        if ((tree.Contents(eyeLeaf) & ContentsTestFogVolume) == 0)
        {
            return FogVolumeInfo.None(distance);
        }

        (int volume, int leaf) found = (-1, -1);

        Walk(0);

        return found.volume < 0 ? FogVolumeInfo.None(distance) :Info(found.volume, found.leaf, false);

        FogVolumeInfo Info(int volume, int leaf, bool eyeIn) =>
            volume >= 0 && volume < water.Volumes.Count
                ? new(volume, leaf, eyeIn, distance, water.Volumes[volume].SurfaceZ, water.Volumes[volume].SurfaceTexinfo)
                : new(volume, leaf, eyeIn, distance, 0f, -1);

        // 0x1800e0f70: returns true to keep looking.
        bool Walk(int child)
        {
            if (child < 0)
            {
                int leaf = -1 - child;

                if (tree.Contents(leaf) == BspLeafTree.ContentsSolid || !leafVisible(leaf) ||
                    tree.Bounds(leaf) is not { } box || !boxInView(box.Min, box.Max))
                {
                    return true;
                }

                int id = tree.WaterDataId(leaf);

                if (id == -1 || (tree.Contents(leaf) & BspLeafTree.ContentsSlime) != 0)
                {
                    return true;
                }

                found = (id, leaf);

                return false;
            }

            // A node's visframe is set when any leaf beneath it is visible, so testing the leaves alone prunes less
            // and answers the same.
            if (tree.Node(child) is not { } node || !boxInView(node.Min, node.Max))
            {
                return true;
            }

            float side = (node.NormalX * eye.X) + (node.NormalY * eye.Y) + (node.NormalZ * eye.Z) - node.Distance;

            // Near side first: children[0] when the eye is on or in front of the plane (0x1800e1070).
            return side >= 0f
                ? Walk(node.Front) && Walk(node.Back)
                : Walk(node.Back) && Walk(node.Front);
        }
    }
}
