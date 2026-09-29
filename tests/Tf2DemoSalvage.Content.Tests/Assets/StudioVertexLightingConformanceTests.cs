using System;
using System.Buffers.Binary;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What `engine.dll` accepts of a `.vhv` header, on hand-built files (B436).
/// </summary>
/// <remarks>
/// `0x1800f4760` (static prop lighting load) reads the 40-byte header and sets the prop's static-lighting bit only when
/// <c>version == 2</c>, <c>checksum == studiohdr->checksum</c> and <c>vertexSize == 4</c>; otherwise it sets the "bad"
/// bit, and `FUN_1800f36e0` bakes the prop on the CPU as though it had no file.
/// </remarks>
public sealed class StudioVertexLightingConformanceTests
{
    private const int Checksum = 0x1234;

    [Test]
    public void Read_AFourByteVertexAtTheModelsChecksum_IsAccepted() =>
        StudioVertexLighting.Read(Vhv(Checksum, 4), Checksum).Count.ShouldBe(1);

    [Test]
    public void Read_AnEightByteVertex_IsRefusedAs0x1800f4760Does() =>
        Should.Throw<InvalidDataException>(() => StudioVertexLighting.Read(Vhv(Checksum, 8), Checksum));

    [Test]
    public void Read_AnotherModelsChecksum_IsRefusedAs0x1800f4760Does() =>
        Should.Throw<InvalidDataException>(() => StudioVertexLighting.Read(Vhv(Checksum + 1, 4), Checksum));

    /// <summary>One LOD-0 mesh of one vertex.</summary>
    private static byte[] Vhv(int checksum, int vertexSize)
    {
        byte[] file = new byte[40 + 28 + vertexSize];
        Span<byte> bytes = file;
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], checksum);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], vertexSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[20..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[44..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[48..], 68);
        return file;
    }
}
