using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>A two-leaf map for culling tests: leaf 1 in area 0 with face 0 (material 7), leaf 2 in area 1 with face 1
/// (material 9).</summary>
internal static class TwoFaceMap
{
    /// <summary>The map's culling.</summary>
    /// <param name="skyArea">The sky room's area, or -1 for none.</param>
    /// <param name="oneWayPvs">
    /// Leaf 1 (z &gt; 0) in cluster 0, which sees only itself; leaf 2 (z &lt; 0) in cluster 1, which sees both. Off, the map has
    /// no vis data and both leaves are cluster 0.
    /// </param>
    public static WorldCulling Culling(int skyArea = -1, bool oneWayPvs = false)
    {
        // Leaf 0 is the solid leaf every tree carries; the two real ones follow it.
        byte[] leaves = new byte[32 * 3];

        Leaf(leaves, leaf: 1, area: 0, firstFace: 0, faces: 1, cluster: 0);
        Leaf(leaves, leaf: 2, area: 1, firstFace: 1, faces: 1, cluster: oneWayPvs ? 1 : 0);

        // **A real node, because this fixture walks the TREE where the other leaf tests index it
        // directly.** An all-zero node lump gives node 0 two children both numbered 0 — itself —
        // and the descent never terminates. It does not fail, it HANGS, which reads as a slow
        // build rather than as a broken fixture: the first run of this sat for ten minutes with an
        // empty log while the build beside it succeeded in seconds.
        //
        // `plane` splits on z; a child written as `-leaf - 1` names a leaf rather than a node.
        byte[] plane = new byte[20];

        BitConverter.TryWriteBytes(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];

        BitConverter.TryWriteBytes(node.AsSpan(4), -2);
        BitConverter.TryWriteBytes(node.AsSpan(8), -3);

        BspLeafTree tree = BspLeafTree.FromLumps(node, plane, leaves);

        byte[] faceList = new byte[4];

        BitConverter.TryWriteBytes(faceList.AsSpan(0), (ushort)0);
        BitConverter.TryWriteBytes(faceList.AsSpan(2), (ushort)1);

        WorldFaceSpan[] spans =
        [
            new(0, FirstVertex: 0, VertexCount: 3, MaterialIndex: 7, Category: default),
            new(1, FirstVertex: 3, VertexCount: 3, MaterialIndex: 9, Category: default),
        ];

        return new WorldCulling(tree, oneWayPvs ? OneWayPvs() : BspVisibility.None, BspLeafFaces.FromLump(faceList), spans)
        {
            SkyArea = skyArea,
        };
    }

    /// <summary>Two clusters, one byte a row, uncompressed: cluster 0's row <c>01</c>, cluster 1's <c>03</c>.</summary>
    private static BspVisibility OneWayPvs()
    {
        // numclusters, then each cluster's PVS and PAS offsets, then the rows (`dvis_t`, `bspfile.h`).
        byte[] lump = new byte[4 + (2 * 8) + 2];

        BitConverter.TryWriteBytes(lump.AsSpan(0), 2);
        BitConverter.TryWriteBytes(lump.AsSpan(4), 20);
        BitConverter.TryWriteBytes(lump.AsSpan(8), 20);
        BitConverter.TryWriteBytes(lump.AsSpan(12), 21);
        BitConverter.TryWriteBytes(lump.AsSpan(16), 21);
        lump[20] = 0x01;
        lump[21] = 0x03;

        return BspVisibility.FromLump(lump);
    }

    /// <summary>A frustum that accepts everything, so the area is the only filter under test.</summary>
    public static ViewFrustum Everything() => default;

    /// <summary>Writes one <c>dleaf_t</c>: the cluster at 4, the packed area at 6, the face range at 20 and 22.</summary>
    private static void Leaf(byte[] lump, int leaf, int area, int firstFace, int faces, int cluster)
    {
        int at = leaf * 32;

        BitConverter.TryWriteBytes(lump.AsSpan(at + 4), (short)cluster);
        BitConverter.TryWriteBytes(lump.AsSpan(at + 6), (short)(area & 0x1FF));
        BitConverter.TryWriteBytes(lump.AsSpan(at + 20), (ushort)firstFace);
        BitConverter.TryWriteBytes(lump.AsSpan(at + 22), (ushort)faces);
    }
}
