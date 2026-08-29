using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Whether a leaf can reach the displacements standing in it, through <c>LUMP_LEAFFACES</c>.
/// </summary>
/// <remarks>
/// **This is the question the narrowing step depends on, and it is not answerable from the SDK.**
/// The engine attaches displacements to leaves in <c>cmodel_disp.cpp</c>, which is engine-side and
/// not published — `vrad` builds its own list a third way. So the route from a leaf to the terrain
/// standing in it had to be measured on a real map rather than read.
///
/// **The route under test:** leaf → <c>LUMP_LEAFFACES</c> (16) → face index → <c>dface_t.dispinfo</c>
/// at offset 12, which is −1 on a flat face and a displacement index otherwise.
///
/// **What a failure here would mean is a design change, not a bug.** If displacement base faces are
/// NOT reachable from leaves, the sweep has to find nearby terrain some other way — by testing each
/// displacement's bounds against the leaf, which is what `vrad` does and what the engine is believed
/// to do. Measuring first is the point: building the narrowing on an assumption and discovering it
/// on a map is the expensive order.
/// </remarks>
public sealed class LeafDisplacementReachTests
{
    /// <summary>A map with a lot of terrain, which is what makes it the right subject.</summary>
    private static string? MapFile => GameInstall.Find("maps/cp_badlands.bsp");

    [Test]
    public void LeafFaces_OnAMapWithTerrain_ReachTheDisplacementFaces()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> faces = BspLumpData.Read(file, header.Lump(BspLumpIndex.Faces)).Span;
        ReadOnlySpan<byte> leafFaces = BspLumpData.Read(file, header.Lump(BspLumpIndex.LeafFaces)).Span;
        ReadOnlySpan<byte> leaves = BspLumpData.Read(file, header.Lump(BspLumpIndex.Leafs)).Span;

        int faceCount = faces.Length / BspStructLayout.FaceStride;
        int leafStride = header.Lump(BspLumpIndex.Leafs).Version >= 1 ? 32 : 56;
        int leafCount = leaves.Length / leafStride;

        // Every face that IS a displacement, by index.
        HashSet<int> displacementFaces = [];

        for (int face = 0; face < faceCount; face++)
        {
            short displacement = BinaryPrimitives.ReadInt16LittleEndian(
                faces[((face * BspStructLayout.FaceStride) + BspStructLayout.FaceDisplacementOffset)..]);

            if (displacement >= 0)
            {
                displacementFaces.Add(face);
            }
        }

        // Every face any leaf names, and how many leaves name a displacement.
        HashSet<int> reached = [];
        HashSet<int> reachedFlat = [];
        int leavesWithTerrain = 0;

        for (int leaf = 0; leaf < leafCount; leaf++)
        {
            ReadOnlySpan<byte> entry = leaves.Slice(leaf * leafStride, leafStride);

            int first = BinaryPrimitives.ReadUInt16LittleEndian(entry[FirstLeafFaceOffset..]);
            int count = BinaryPrimitives.ReadUInt16LittleEndian(entry[LeafFaceCountOffset..]);

            bool any = false;

            for (int index = first; index < first + count && (index * 2) + 2 <= leafFaces.Length; index++)
            {
                int face = BinaryPrimitives.ReadUInt16LittleEndian(leafFaces[(index * 2)..]);

                if (displacementFaces.Contains(face))
                {
                    reached.Add(face);
                    any = true;
                }
                else
                {
                    reachedFlat.Add(face);
                }
            }

            if (any)
            {
                leavesWithTerrain++;
            }
        }

        TestContext.Out.WriteLine(
            $"cp_badlands: {faceCount} faces, {displacementFaces.Count} of them displacements; "
            + $"{leafCount} leaves, {leavesWithTerrain} naming terrain; "
            + $"{reached.Count} displacement faces and {reachedFlat.Count} flat faces reachable");

        displacementFaces.Count.ShouldBeGreaterThan(
            0, "cp_badlands is built on terrain; a map with none cannot answer this");

        // **The control, and without it this test proves nothing.** An empty result is usually a
        // fact about the reader rather than about the data
        // (`docs/memory/an-empty-search-needs-a-control.md`) — a wrong `dleaf_t` offset or stride
        // would give zero for every face, displacement or not. Flat faces ARE reached in quantity,
        // so the walk works and the absence below is real.
        reachedFlat.Count.ShouldBeGreaterThan(
            1000, "leaffaces must reach ordinary faces, or the leaf walk itself is wrong");

        // **MEASURED 2026-08-29, and it is the opposite of what was planned.** The previous
        // handoff's step 2 was "leaf → LUMP_LEAFFACES → faces → dispinfo". On cp_badlands that
        // reaches **none** of the 1191 displacement faces: a displacement's base face is not in any
        // leaf's face list at all.
        //
        // It makes sense in hindsight. The base quad is not the terrain — the real surface is a
        // heightfield that bulges out of it — so the compiler has no single leaf to put the face in,
        // and `vrad` builds its own displacement list rather than using leaves for the same reason.
        // The engine's `cmodel_disp.cpp`, which would say so outright, is not published.
        //
        // So the narrowing is by BOUNDS, not by leaf: each displacement carries a bounding box and
        // the sweep tests the ones its own swept box could touch. Asserted as an equality rather
        // than deleted, because the day a map does put them in leaves this should say so.
        reached.Count.ShouldBe(
            0,
            "a displacement's base face is not in any leaf's face list; the narrowing is by bounds");
    }

    /// <summary>Byte offset of <c>firstleafface</c> inside a <c>dleaf_t</c>.</summary>
    /// <remarks>
    /// Sits immediately before <c>firstleafbrush</c> at 24, which `BspLeafTree` already reads — the
    /// pair mins[3]/maxs[3] of shorts occupy 8 through 19.
    /// </remarks>
    private const int FirstLeafFaceOffset = 20;

    /// <summary>Byte offset of <c>numleaffaces</c> inside a <c>dleaf_t</c>.</summary>
    private const int LeafFaceCountOffset = 22;
}
