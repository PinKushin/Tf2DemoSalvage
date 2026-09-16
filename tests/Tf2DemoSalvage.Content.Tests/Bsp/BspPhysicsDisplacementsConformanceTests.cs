using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary><c>LUMP_PHYSDISP</c>, as <c>engine.dll</c>'s <c>FUN_18016f6d0</c> loads it (B369).</summary>
/// <remarks>
/// Read from the decompiled engine (`docs/findings/51`): a <c>u16</c> count, one <c>u16</c> size per displacement with <c>0xffff</c>
/// naming none, then the blobs back to back. Synthetic conformance (D38).
/// </remarks>
public sealed class BspPhysicsDisplacementsConformanceTests
{
    /// <remarks>**Each size is a running offset, and <c>0xffff</c> is no blob and takes no bytes.**</remarks>
    [Test]
    public void Read_ThreeDisplacementsWithTheMiddleOneBare_AnswersTheOuterTwoBlobs()
    {
        byte[] lump = [3, 0, 2, 0, 0xff, 0xff, 3, 0, 10, 11, 20, 21, 22];

        IReadOnlyList<byte[]?> blobs = BspPhysicsDisplacements.Read(lump, displacements: 3);

        blobs.Count.ShouldBe(3);
        blobs[0].ShouldBe([10, 11]);
        blobs[1].ShouldBeNull();
        blobs[2].ShouldBe([20, 21, 22]);
    }

    /// <remarks>**A count that is not the map's displacement count stops the load** — the engine's `Error`, "Bad map data".</remarks>
    [Test]
    public void Read_ACountThatDisagreesWithTheMap_IsRefused()
    {
        Should.Throw<System.IO.InvalidDataException>(() => BspPhysicsDisplacements.Read(new byte[] { 2, 0, 0, 0, 0, 0 }, displacements: 3));
    }

    /// <remarks>**An absent lump is no blobs at all**, where the engine builds each hull at runtime instead.</remarks>
    [Test]
    public void Read_AnEmptyLump_AnswersNothing()
    {
        BspPhysicsDisplacements.Read(ReadOnlyMemory<byte>.Empty, displacements: 3).ShouldBeEmpty();
    }
}
