using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The surface sort ID — material AND lightmap page — that the overlay queue groups by (B457).
/// </summary>
/// <remarks>
/// **Read in disassembly.** engine.dll <c>0x1800d2b10</c> (<c>BeginLightmapAllocation</c> +0x2c0 … End +0x2c8)
/// inserts every face into a tree ordered by <c>0x1800d2650</c> — lit before unlit, then material enumeration ID
/// (IMaterial vtable +0x70), then without light styles before with, then larger luxel area first, ties in face
/// order — and walks it, asking materialsystem.dll for each face's sort ID: <c>AllocateLightmap</c>
/// (<c>0x180023900</c>) with width <c>size[0] + 3</c> (×4 when the material needs bumped lightmaps) and height
/// <c>size[1] + 3</c>, or <c>AllocateWhiteLightmap</c> (<c>0x180023ef0</c>) for an unlit face.
///
/// <c>AllocateLightmap</c>: on a material change every page but the last is closed and, if a material was current,
/// the last page takes a new sort ID; the block goes in the first open page that takes it (<c>CImagePacker::AddBlock</c>,
/// <c>0x180047ea0</c>, a wavefront packer), else a new 1024×512 page (<c>0x1800480c0</c>) with a new sort ID.
/// <c>AllocateWhiteLightmap</c>: a new sort ID whenever its material changes. The pages start with one empty page,
/// sort ID 0 (<c>0x180023f90</c>).
/// </remarks>
public sealed class LightmapSortIdConformanceTests
{
    private static BspSurface Face(
        int index, int material, int luxelsWide, int luxelsHigh, SurfaceProperties flags = SurfaceProperties.None) =>
        new(index, [], material, default, (0f, 0f, 1f), flags, -1, luxelsWide, luxelsHigh);

    private static IReadOnlyList<int> Ids(params BspSurface[] faces)
    {
        IReadOnlyDictionary<int, int> ids = LightmapSortIds.Assign(faces);

        return [.. faces.Select(face => ids[face.FaceIndex])];
    }

    [Test]
    public void Assign_AMaterialSpillingOntoASecondPage_GetsTwoSortIds() =>
        // A and C (material 0) each take most of a page; B (material 1) follows C onto the last page, renamed.
        Ids(Face(0, 0, 998, 398), Face(1, 1, 8, 8), Face(2, 0, 998, 398)).ShouldBe([0, 2, 1]);

    [Test]
    public void Assign_ANewMaterial_ClosesEveryPageButTheLast() =>
        // B would fit beside A on page 0, but material 1 may only use the last page, under a new sort ID.
        Ids(Face(0, 0, 998, 398), Face(1, 0, 998, 398), Face(2, 1, 8, 8)).ShouldBe([0, 1, 2]);

    [Test]
    public void Assign_BumpedLightmaps_AreFourTimesAsWide() =>
        // 200 luxels bumped is 808 wide: two cannot sit side by side, and stacked they overflow 512.
        Ids(
            Face(0, 0, 200, 300, SurfaceProperties.BumpLight),
            Face(1, 0, 200, 300, SurfaceProperties.BumpLight)).ShouldBe([0, 1]);

    [Test]
    public void Assign_UnlitFaces_TakeAWhiteSortIdPerMaterialAfterTheLitOnes() =>
        Ids(
            Face(0, 0, 4, 4, SurfaceProperties.NoLight),
            Face(1, 0, 4, 4),
            Face(2, 1, 4, 4, SurfaceProperties.NoLight)).ShouldBe([1, 0, 2]);

    [Test]
    public void Assign_ALargerFaceOfTheSameMaterial_IsPackedFirst() =>
        // Face 1 is larger, so it takes page 0 and face 0 spills; in face order it would be the other way round.
        Ids(Face(0, 0, 598, 298), Face(1, 0, 998, 398)).ShouldBe([1, 0]);

    [Test]
    public void Order_AMaterialOnTwoPages_IsTwoSortListEntries()
    {
        // The A/B/C case: A and C share a material but not a page, so the engine queues A, B, C and draws them
        // C, B, A; under a material-only key it would queue A, C, B and draw B, C, A.
        WorldFaceSpan[] spans =
        [
            new(0, 0, 3, 10, SurfaceCategory.Brush, SortId: 0),
            new(1, 3, 3, 11, SurfaceCategory.Brush, SortId: 2),
            new(2, 6, 3, 10, SurfaceCategory.Brush, SortId: 1),
        ];

        OverlayRenderLists queue = new(
            [
                new OverlayFragment(0, 0, 20, 100, 6),
                new OverlayFragment(1, 0, 21, 106, 6),
                new OverlayFragment(2, 0, 22, 112, 6),
            ],
            spans);

        queue.Order([0, 1, 2], [], null, _ => false).Select(batch => batch.MaterialIndex).ShouldBe([22, 21, 20]);
    }
}
