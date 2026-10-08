using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A <c>.vvd</c>'s tangent array, read from hand-built files whose tangents the test put there (D38).
/// </summary>
/// <remarks>
/// <c>ModelTangentConformanceTests</c> quotes the layout: one <c>Vector4D</c> per vertex at
/// <c>tangentDataStart</c>, reordered by the same fixups as the vertices (<c>studio.h:3323-3329</c>).
/// </remarks>
public sealed class StudioVertexTangentTests
{
    private const int Header = 64;
    private const int VertexBytes = 48;
    private const int TangentBytes = 16;

    [Test]
    public void Read_AFileWithTangents_CarriesEachVertexsTangentAndSign()
    {
        byte[] file = Vvd(vertices: 2, tangents: true, fixups: []);

        IReadOnlyList<StudioVertex> read = StudioVertices.Read(file);

        read[0].Tangent.ShouldBe((1f, 0f, 0f, 1f));
        read[1].Tangent.ShouldBe((0f, 1f, 0f, -1f));
    }

    [Test]
    public void Read_AFileWithoutTangents_LeavesASignOfZero()
    {
        // tangentDataStart 0 is "no tangent data" (studio.h:1969), which the shader reads as "no frame".
        IReadOnlyList<StudioVertex> read = StudioVertices.Read(Vvd(vertices: 2, tangents: false, fixups: []));

        read[0].Tangent.ShouldBe((0f, 0f, 0f, 0f));
        read[1].Tangent.ShouldBe((0f, 0f, 0f, 0f));
    }

    [Test]
    public void Read_FixupsThatReverseTheVertices_ReverseTheTangentsWithThem()
    {
        // Two runs, vertex 1 then vertex 0: each vertex keeps its own tangent, so the X-tangent travels with the
        // vertex at x = 0 wherever the fixup puts it.
        byte[] file = Vvd(vertices: 2, tangents: true, fixups: [(0, 1, 1), (0, 0, 1)]);

        IReadOnlyList<StudioVertex> read = StudioVertices.Read(file);

        read[0].X.ShouldBe(1f);
        read[0].Tangent.ShouldBe((0f, 1f, 0f, -1f));
        read[1].X.ShouldBe(0f);
        read[1].Tangent.ShouldBe((1f, 0f, 0f, 1f));
    }

    [Test]
    public void Read_ATangentArrayShorterThanTheVertices_Fails()
    {
        byte[] file = Vvd(vertices: 2, tangents: true, fixups: []);
        Array.Resize(ref file, file.Length - TangentBytes);

        Should.Throw<System.IO.InvalidDataException>(() => StudioVertices.Read(file));
    }

    /// <summary>
    /// A version-4 VVD of <paramref name="vertices"/> vertices, vertex <c>i</c> at x = i with tangent X (+1) for
    /// even and Y (−1) for odd.
    /// </summary>
    private static byte[] Vvd(int vertices, bool tangents, (int Lod, int Source, int Count)[] fixups)
    {
        int fixupStart = Header;
        int vertexStart = fixupStart + (fixups.Length * 12);
        int tangentStart = vertexStart + (vertices * VertexBytes);
        byte[] file = new byte[tangentStart + (tangents ? vertices * TangentBytes : 0)];
        Span<byte> span = file;

        BinaryPrimitives.WriteInt32LittleEndian(span, 0x56534449);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], vertices);
        BinaryPrimitives.WriteInt32LittleEndian(span[48..], fixups.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[52..], fixupStart);
        BinaryPrimitives.WriteInt32LittleEndian(span[56..], vertexStart);
        BinaryPrimitives.WriteInt32LittleEndian(span[60..], tangents ? tangentStart : 0);

        for (int at = 0; at < fixups.Length; at++)
        {
            Span<byte> fixup = span[(fixupStart + (at * 12))..];
            BinaryPrimitives.WriteInt32LittleEndian(fixup, fixups[at].Lod);
            BinaryPrimitives.WriteInt32LittleEndian(fixup[4..], fixups[at].Source);
            BinaryPrimitives.WriteInt32LittleEndian(fixup[8..], fixups[at].Count);
        }

        for (int at = 0; at < vertices; at++)
        {
            Span<byte> vertex = span[(vertexStart + (at * VertexBytes))..];
            BinaryPrimitives.WriteSingleLittleEndian(vertex, 1f);
            BinaryPrimitives.WriteSingleLittleEndian(vertex[16..], at);
            BinaryPrimitives.WriteSingleLittleEndian(vertex[36..], 1f);

            if (tangents)
            {
                Span<byte> tangent = span[(tangentStart + (at * TangentBytes))..];
                bool even = at % 2 == 0;
                BinaryPrimitives.WriteSingleLittleEndian(tangent, even ? 1f : 0f);
                BinaryPrimitives.WriteSingleLittleEndian(tangent[4..], even ? 0f : 1f);
                BinaryPrimitives.WriteSingleLittleEndian(tangent[12..], even ? 1f : -1f);
            }
        }

        return file;
    }
}
