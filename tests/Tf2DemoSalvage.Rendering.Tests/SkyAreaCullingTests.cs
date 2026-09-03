using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The 3D skybox room is drawn as its own pass, so the cull has to hand back two sets.
/// </summary>
/// <remarks>
/// **<c>CSkyboxView::DrawInternal</c> swaps the area bits before it draws**
/// (<c>viewrender.cpp:4877</c>):
///
/// <code>
///   tmpbits[m_pSky3dParams-&gt;area&gt;&gt;3] |= 1 &lt;&lt; (m_pSky3dParams-&gt;area&amp;7);
///   *areabits = tmpbits;
/// </code>
///
/// so the sky pass draws exactly one area and the main view draws the rest. **Both halves matter
/// and only one of them is obvious**: without the sky pass the miniature room is missing, and
/// without EXCLUDING it from the main pass it is still out there in the world at its literal size,
/// which is what this viewer does today (B152).
///
/// **Measured on the corpus maps before this was built**: `koth_harvest_final` puts its sky camera
/// in area 1, which holds 9 of 2074 leaves; `cp_fulgur` in area 16, holding 18 of 14264. So the
/// area names a small room, which is what makes it the right discriminator — an area holding most
/// of the map would mean filtering by it deletes the level.
/// </remarks>
public sealed class SkyAreaCullingTests
{
    [Test]
    public void Batches_WithNoSkyArea_DrawsEveryLeafInTheMainPass()
    {
        // **The control.** Every map without a sky_camera, and every map before this feature, must
        // behave exactly as it did — one pass holding everything.
        WorldCulling culling = Culled(skyArea: -1);

        culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull().Count.ShouldBe(2);
        culling.SkyBatches.ShouldNotBeNull().ShouldBeEmpty("there is no sky room to draw");
    }

    [Test]
    public void Batches_WithASkyArea_LeavesTheSkyRoomOutOfTheMainPass()
    {
        WorldCulling culling = Culled(skyArea: 1);

        culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull().Count.ShouldBe(
            1, "the sky room's leaf is drawn by the sky pass instead");
    }

    [Test]
    public void SkyBatches_WithASkyArea_HoldsTheSkyRoomAlone()
    {
        WorldCulling culling = Culled(skyArea: 1);

        _ = culling.Batches(0f, 0f, 0f, Everything());

        culling.SkyBatches.ShouldNotBeNull().Count.ShouldBe(
            1, "one run, for the one face in the sky area");
    }

    /// <remarks>
    /// **The two sets must not share storage, which a single reused buffer would make them do.**
    /// `VisibleWorld` clears and refills one list per call, so running the sky pass through the same
    /// instance would leave the main pass pointing at the sky's runs — a failure that shows as the
    /// world vanishing and the sky being drawn twice, and one that only appears once both passes
    /// exist.
    /// </remarks>
    [Test]
    public void Batches_AfterTheSkyPass_StillHoldsTheMainPassRuns()
    {
        WorldCulling culling = Culled(skyArea: 1);

        IReadOnlyList<WorldBatch> main = culling.Batches(0f, 0f, 0f, Everything()).ShouldNotBeNull();

        _ = culling.SkyBatches;

        main.Count.ShouldBe(1, "the main runs survive the sky pass being built");
        main[0].MaterialIndex.ShouldBe(7, "and they are the WORLD's material, not the sky's");
    }

    /// <summary>A two-leaf map: leaf 1 in area 0 with face 0, leaf 2 in area 1 with face 1.</summary>
    private static WorldCulling Culled(int skyArea)
    {
        // Leaf 0 is the solid leaf every tree carries; the two real ones follow it.
        byte[] leaves = new byte[32 * 3];

        Leaf(leaves, leaf: 1, area: 0, firstFace: 0, faces: 1);
        Leaf(leaves, leaf: 2, area: 1, firstFace: 1, faces: 1);

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

        return new WorldCulling(tree, BspVisibility.None, BspLeafFaces.FromLump(faceList), spans)
        {
            SkyArea = skyArea,
        };
    }

    /// <summary>Writes one <c>dleaf_t</c>: the packed area at 6, the face range at 20 and 22.</summary>
    private static void Leaf(byte[] lump, int leaf, int area, int firstFace, int faces)
    {
        int at = leaf * 32;

        BitConverter.TryWriteBytes(lump.AsSpan(at + 6), (short)(area & 0x1FF));
        BitConverter.TryWriteBytes(lump.AsSpan(at + 20), (ushort)firstFace);
        BitConverter.TryWriteBytes(lump.AsSpan(at + 22), (ushort)faces);
    }

    /// <summary>A frustum that accepts everything, so the area is the only filter under test.</summary>
    private static ViewFrustum Everything() => default;
}
