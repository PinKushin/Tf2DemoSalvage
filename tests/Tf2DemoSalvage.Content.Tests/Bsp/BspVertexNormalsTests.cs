using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Reading the per-vertex normals a map was compiled with.
/// </summary>
/// <remarks>
/// **Read but not yet drawn, on purpose (D93).** Nothing consumes these today — the world is lit by
/// its baked lightmaps, and Valve's bumped path takes its normal from the bump map against
/// `g_localBumpBasis` rather than from a vertex. They are read because decoding is total and
/// rendering is not: the file is understood now, so the day something needs a tangent basis is a
/// new consumer rather than an excavation.
///
/// **Why the plane normal is not a substitute**, which is the whole reason this exists: `vbsp`
/// writes `dplanes[f->planenum].normal` into the lump and comments *"this doesn't do an exhaustive
/// vertex normal match because the vrad does it"* (`src/utils/vbsp/normals.cpp:38`). vrad then
/// replaces them with true smoothed normals wherever a smoothing group applies, so the two agree on
/// flat unsmoothed brushwork and nowhere else.
///
/// Built from synthetic files: every property here — the stride, the index width, an absent lump —
/// is exactly expressible in a handful of hand-written bytes, and a real map cannot say which answer
/// is right without already trusting this code.
/// </remarks>
public sealed class BspVertexNormalsTests
{
    [Test]
    public void Read_AMapWithNormals_AnswersThemInOrder()
    {
        // Three normals whose components differ from each other on every axis, so a reader that
        // mixed up the stride or the component order produces a different answer rather than a
        // plausible one.
        byte[] map = BuildBsp(
            Normals((1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f)),
            Indices(0, 1, 2));

        VertexNormals read = BspVertexNormals.Read(map);

        read.Normals.Count.ShouldBe(3);
        read.Normals[1].Y.ShouldBe(1f);
        read.Normals[2].Z.ShouldBe(1f);
    }

    [Test]
    public void Read_TheIndices_AreSixteenBitAndIndexTheNormals()
    {
        // **The index width is the thing that fails silently.** `unsigned short` read as `int` gives
        // half as many indices, each a plausible-looking number — so the test uses a value above 255
        // that a byte-wide read could not produce and a wrong-width read would mangle.
        byte[] map = BuildBsp(
            Normals((0f, 0f, 1f)),
            Indices(0, 4096, 65535));

        VertexNormals read = BspVertexNormals.Read(map);

        read.Indices.ShouldBe([0, 4096, 65535]);
    }

    [Test]
    public void Read_AMapWithNoNormalLumps_IsEmptyRatherThanThrowing()
    {
        // A map compiled without running vrad has no smoothed normals to store, and every other
        // lump reader here treats an absent lump the same way.
        VertexNormals read = BspVertexNormals.Read(BuildBsp([], []));

        read.Normals.ShouldBeEmpty();
        read.Indices.ShouldBeEmpty();
    }

    [Test]
    public void Read_ALumpEndingMidStructure_StopsAtTheLastWholeOne()
    {
        // A truncated download, or a lump length that disagrees with its contents. Reading past the
        // end would answer with whatever followed in the file — which is the shape of every "the
        // numbers looked reasonable" bug this project has had.
        byte[] normals = Normals((1f, 0f, 0f), (0f, 1f, 0f));
        byte[] truncated = normals[..(normals.Length - 5)];

        VertexNormals read = BspVertexNormals.Read(BuildBsp(truncated, Indices(0)));

        read.Normals.Count.ShouldBe(1, "the second normal is incomplete and is not invented");
    }

    private static byte[] Normals(params (float X, float Y, float Z)[] normals)
    {
        byte[] data = new byte[normals.Length * 12];

        for (int i = 0; i < normals.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 12), normals[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 4), normals[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 8), normals[i].Z);
        }

        return data;
    }

    private static byte[] Indices(params int[] indices)
    {
        byte[] data = new byte[indices.Length * 2];

        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), (ushort)indices[i]);
        }

        return data;
    }

    /// <summary>Assembles a BSP carrying just the two lumps under test.</summary>
    private static byte[] BuildBsp(byte[] normals, byte[] indices)
    {
        Dictionary<int, byte[]> lumps = [];

        if (normals.Length > 0)
        {
            lumps[BspLumpIndex.VertNormals] = normals;
        }

        if (indices.Length > 0)
        {
            lumps[BspLumpIndex.VertNormalIndices] = indices;
        }

        int total = BspHeader.SizeBytes;

        foreach (byte[] payload in lumps.Values)
        {
            total += payload.Length;
        }

        byte[] file = new byte[total];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 21);

        int at = BspHeader.SizeBytes;

        foreach ((int index, byte[] payload) in lumps)
        {
            payload.CopyTo(file, at);
            int entry = 8 + (index * 16);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry), at);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry + 4), payload.Length);
            at += payload.Length;
        }

        return file;
    }
}
