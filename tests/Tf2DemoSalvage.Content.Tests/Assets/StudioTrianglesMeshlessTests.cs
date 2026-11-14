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
/// answer: one body part, one model, one or more LODs, and per LOD a number of meshes with no strip groups.
/// The shipped file is the same shape at one LOD: 72 bytes, the last eight its empty material replacement list.
/// </remarks>
public sealed class StudioTrianglesMeshlessTests
{
    private const int Checksum = 0x1234_5678;

    [Test]
    public void Read_AModelThatDeclaresNoMeshes_ReturnsNoMeshes()
    {
        StudioTriangles.Read(IndexFile(Checksum, lods: 1, meshes: 0), Model(meshes: [])).ShouldBeEmpty();
    }

    [Test]
    public void Read_TheSecondLodOfAModelThatDeclaresNoMeshes_ReturnsNoMeshes()
    {
        StudioTriangles.Read(IndexFile(Checksum, lods: 2, meshes: 0), Model(meshes: []), lod: 1).ShouldBeEmpty();
    }

    [Test]
    public void Read_AMeshlessFileForAModelThatDeclaresAMesh_IsRejected()
    {
        // Caught by the count: the .vtx walks no mesh where the .mdl declares one.
        Should.Throw<InvalidDataException>(() =>
            StudioTriangles.Read(IndexFile(Checksum, lods: 1, meshes: 0), Model(meshes: [default])));
    }

    [Test]
    public void Read_ADeclaredMeshWithNoStripGroups_IsRejected()
    {
        // Caught by the TRIANGLE requirement, not the count: one mesh each side, and it has nothing in it. Kept
        // because no shipped model needs it relaxed (B158: 0 of 19,810 LOD-0 meshes have no strip groups).
        Should.Throw<InvalidDataException>(() =>
            StudioTriangles.Read(IndexFile(Checksum, lods: 1, meshes: 1), Model(meshes: [default])));
    }

    [Test]
    public void Read_AMeshlessFileWithTheWrongChecksum_IsRejected()
    {
        Should.Throw<InvalidDataException>(() =>
            StudioTriangles.Read(IndexFile(Checksum + 1, lods: 1, meshes: 0), Model(meshes: [])))
            .Message.ShouldContain("checksum");
    }

    [Test]
    public void Read_AMeshlessFileOfAnotherVersion_IsRejected()
    {
        byte[] file = IndexFile(Checksum, lods: 1, meshes: 0);
        BinaryPrimitives.WriteInt32LittleEndian(file, VtxVersion + 1);

        Should.Throw<InvalidDataException>(() => StudioTriangles.Read(file, Model(meshes: [])))
            .Message.ShouldContain("version");
    }

    [Test]
    public void ShadowLod_AModelThatDeclaresNoMeshes_IsLevelZero()
    {
        // `$shadowlod` and two levels would put the caster at level 1; a rig has no caster at any level, so a
        // separate shadow level would pack an entry that casts nothing.
        StudioTriangles.ShadowLod(Model(meshes: [], StudioModelFlags.HasShadowLod), IndexFile(Checksum, lods: 2, meshes: 0))
            .ShouldBe(0);
    }

    [Test]
    public void ShadowLod_AModelThatDeclaresAMesh_IsItsLastLevel()
    {
        // The control: the same flags and levels with a mesh declared.
        StudioTriangles.ShadowLod(Model(meshes: [default], StudioModelFlags.HasShadowLod), IndexFile(Checksum, lods: 2, meshes: 0))
            .ShouldBe(1);
    }

    [Test]
    public void DeclaresNoMeshes_AModelWithAndWithoutMeshes_IsTheMeshCountBeingZero()
    {
        // The flag `ModelFrames.DeclaresNoMeshes` is copied from, at the narrowest seam a synthetic test reaches.
        Model(meshes: []).DeclaresNoMeshes.ShouldBeTrue();
        Model(meshes: [default]).DeclaresNoMeshes.ShouldBeFalse();
    }

    private static StudioModelInfo Model(IReadOnlyList<StudioMesh> meshes, int flags = 0) =>
        new("comp_stage_cam", Checksum, [], [], meshes, [(0, 1)], ["studio"], flags);

    /// <summary>
    /// A <c>FileHeader_t</c>, one <c>BodyPartHeader_t</c>, one <c>ModelHeader_t</c>, <paramref name="lods"/>
    /// <c>ModelLODHeader_t</c>s, and per LOD <paramref name="meshes"/> <c>MeshHeader_t</c>s with no strip groups.
    /// </summary>
    private static byte[] IndexFile(int checksum, int lods, int meshes)
    {
        int partAt = VtxHeaderStride;
        int modelAt = partAt + VtxBodyPartStride;
        int lodsAt = modelAt + VtxModelStride;
        int meshesAt = lodsAt + (lods * VtxLodStride);
        byte[] file = new byte[meshesAt + (lods * meshes * VtxMeshStride)];
        Span<byte> bytes = file;

        BinaryPrimitives.WriteInt32LittleEndian(bytes, VtxVersion);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], checksum);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[20..], lods);     // numLODs
        BinaryPrimitives.WriteInt32LittleEndian(bytes[28..], 1);        // numBodyParts
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], partAt);   // bodyPartOffset, from the header

        BinaryPrimitives.WriteInt32LittleEndian(bytes[partAt..], 1);                          // numModels
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(partAt + 4)..], modelAt - partAt);     // modelOffset

        BinaryPrimitives.WriteInt32LittleEndian(bytes[modelAt..], lods);                      // numLODs
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(modelAt + 4)..], lodsAt - modelAt);    // lodOffset

        for (int lod = 0; lod < lods; lod++)
        {
            int lodAt = lodsAt + (lod * VtxLodStride);
            int firstMesh = meshesAt + (lod * meshes * VtxMeshStride);

            BinaryPrimitives.WriteInt32LittleEndian(bytes[lodAt..], meshes);                  // numMeshes
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(lodAt + 4)..], firstMesh - lodAt); // meshOffset

            // Each MeshHeader_t stays zeroed: numStripGroups 0.
        }

        return file;
    }
}
