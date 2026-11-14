using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

using static Tf2DemoSalvage.Content.Assets.VertexFileLayout;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A model whose <c>.mdl</c> declares no meshes reads as no triangles, not as a corrupt file (B158).
/// </summary>
/// <remarks>
/// **The specimen is `models/props_ui/comp_stage_cam.mdl`**: one body part, one bone, three sequences and
/// zero meshes — an animated rig with nothing to draw. Built here by hand (D38), so the test knows the
/// answer: one body part, one model, one LOD, zero meshes.
/// </remarks>
public sealed class StudioTrianglesMeshlessTests
{
    private const int Checksum = 0x1234_5678;

    [Test]
    public void Read_AModelThatDeclaresNoMeshes_ReturnsNoMeshes()
    {
        StudioTriangles.Read(MeshlessIndexFile(Checksum), Model(meshes: [])).ShouldBeEmpty();
    }

    [Test]
    public void Read_AShadowLodOfAModelThatDeclaresNoMeshes_ReturnsNoMeshes()
    {
        StudioTriangles.Read(MeshlessIndexFile(Checksum), Model(meshes: []), lod: 1).ShouldBeEmpty();
    }

    [Test]
    public void Read_AMeshlessFileForAModelThatDeclaresAMesh_IsRejected()
    {
        // The control: the same bytes still fail against a .mdl that says there is something to draw, so the
        // acceptance test was relaxed only for the case the engine itself has nothing to walk.
        Should.Throw<InvalidDataException>(() =>
            StudioTriangles.Read(MeshlessIndexFile(Checksum), Model(meshes: [default])));
    }

    [Test]
    public void Read_AMeshlessFileWithTheWrongChecksum_IsRejected()
    {
        Should.Throw<InvalidDataException>(() =>
            StudioTriangles.Read(MeshlessIndexFile(Checksum + 1), Model(meshes: [])))
            .Message.ShouldContain("checksum");
    }

    [Test]
    public void Read_AMeshlessFileOfAnotherVersion_IsRejected()
    {
        byte[] file = MeshlessIndexFile(Checksum);
        BinaryPrimitives.WriteInt32LittleEndian(file, VtxVersion + 1);

        Should.Throw<InvalidDataException>(() => StudioTriangles.Read(file, Model(meshes: [])))
            .Message.ShouldContain("version");
    }

    private static StudioModelInfo Model(IReadOnlyList<StudioMesh> meshes) =>
        new("comp_stage_cam", Checksum, [], [], meshes, [(0, 1)], ["studio"], Flags: 0);

    /// <summary>A <c>FileHeader_t</c>, one <c>BodyPartHeader_t</c>, one <c>ModelHeader_t</c>, one <c>ModelLODHeader_t</c> with no meshes.</summary>
    private static byte[] MeshlessIndexFile(int checksum)
    {
        int partAt = VtxHeaderStride;
        int modelAt = partAt + VtxBodyPartStride;
        int lodAt = modelAt + VtxModelStride;
        byte[] file = new byte[lodAt + VtxLodStride];
        Span<byte> bytes = file;

        BinaryPrimitives.WriteInt32LittleEndian(bytes, VtxVersion);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], checksum);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[20..], 1);        // numLODs
        BinaryPrimitives.WriteInt32LittleEndian(bytes[28..], 1);        // numBodyParts
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], partAt);   // bodyPartOffset, from the header

        BinaryPrimitives.WriteInt32LittleEndian(bytes[partAt..], 1);                          // numModels
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(partAt + 4)..], modelAt - partAt);     // modelOffset

        BinaryPrimitives.WriteInt32LittleEndian(bytes[modelAt..], 1);                         // numLODs
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(modelAt + 4)..], lodAt - modelAt);     // lodOffset

        BinaryPrimitives.WriteInt32LittleEndian(bytes[lodAt..], 0);                           // numMeshes
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(lodAt + 4)..], VtxLodStride);          // meshOffset, past the end

        return file;
    }
}
