using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The vertex layout writer, into a caller's memory (finding 78).</summary>
/// <remarks>
/// **Measured with `alloc-trace`:** 161 MB of large <c>float[]</c> per 20 s of f12 playback came from
/// <c>UploadShotDecals</c> packing every frame's decal vertices into a new array before copying it into the mapped
/// buffer. Writing straight into the mapped memory needs no array. So the writer must lay out the same floats and
/// allocate nothing.
/// </remarks>
public sealed class VertexPackingTests
{
    [Test]
    public void PackInto_TwoDistinctVertices_WritesWhatPackReturnsAndAllocatesNothing()
    {
        List<WorldVertex> vertices =
        [
            new(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f),
            new(-1f, -2f, -3f, -4f, -5f, -6f, -7f, -8f, -9f, -10f, -11f),
        ];
        float[] expected = WorldRenderer.Pack(vertices);
        float[] into = new float[expected.Length];

        WorldRenderer.PackInto(vertices, into);
        long before = GC.GetAllocatedBytesForCurrentThread();
        WorldRenderer.PackInto(vertices, into);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        into.ShouldBe(expected);
        allocated.ShouldBe(0);
    }
}
