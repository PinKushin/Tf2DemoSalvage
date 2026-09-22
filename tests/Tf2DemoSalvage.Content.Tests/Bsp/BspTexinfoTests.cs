using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary><see cref="BspMaterials.ReadTexinfo"/>: `texinfo_t.flags` at 64 and `texinfo_t.texdata` at 68, 72 bytes apart.</summary>
public sealed class BspTexinfoTests
{
    [Test]
    public void ReadTexinfo_TwoRecords_ReadsEachFlagsAndTexdata()
    {
        byte[] texinfo = new byte[72 * 2];

        BinaryPrimitives.WriteInt32LittleEndian(texinfo.AsSpan(64), 0x4);
        BinaryPrimitives.WriteInt32LittleEndian(texinfo.AsSpan(68), 7);
        BinaryPrimitives.WriteInt32LittleEndian(texinfo.AsSpan(72 + 64), 0x80);
        BinaryPrimitives.WriteInt32LittleEndian(texinfo.AsSpan(72 + 68), 11);

        IReadOnlyList<BspTexinfo> read = BspMaterials.ReadTexinfo(SyntheticBsp.Build(new Dictionary<int, byte[]> { [6] = texinfo }));

        read.ShouldBe([new BspTexinfo(SurfaceProperties.Sky, 7), new BspTexinfo(SurfaceProperties.NoDraw, 11)]);
    }
}
