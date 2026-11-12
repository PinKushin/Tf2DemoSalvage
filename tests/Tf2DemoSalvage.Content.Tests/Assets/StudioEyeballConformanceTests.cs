using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A model's eyeballs, mouths and eye deflection, which the eye and teeth shaders are set from (B725).</summary>
/// <remarks>
/// **Read** (published source): `mstudioeyeball_t` (`studio.h:1240-1273`, 172 bytes, studiorender's `imulq $0xac`),
/// `mstudiomodel_t.numeyeballs`/`eyeballindex` at 100/104 (`studio.h:1434`), `mstudiomesh_t.materialtype`/`materialparam`
/// at 24/28 (`studio.h:1381-1383`; `studiorender.dll` `0x180011bab` takes a mesh of `materialtype` 1 to its eyeball draw),
/// `studiohdr_t.nummouths`/`mouthindex` at 292/296 (`studio.h:2291`), `studiohdr2index` at 400 (`:2400`) and
/// `flMaxEyeDeflection` at 12 of it, 0.866 where zero (`:2108-2109`).
///
/// **Synthetic** (D38): one body part, one submodel with an eyeball and two meshes, one mouth, and a second header; the
/// values are the test's own.
/// </remarks>
public sealed class StudioEyeballConformanceTests
{
    [Test]
    public void Read_ASubmodelWithAnEyeballMesh_NamesTheEyeballAndReadsItsFields()
    {
        StudioModelInfo model = StudioModel.Read(Model(maxEyeDeflection: 0.5f));

        model.Eyeballs.Count.ShouldBe(1);
        StudioEyeball eye = model.Eyeballs[0];
        eye.Bone.ShouldBe(3);
        eye.Origin.ShouldBe((1f, 2f, 3f));
        eye.ZOffset.ShouldBe(0.25f);
        eye.Radius.ShouldBe(0.5f);
        eye.Up.ShouldBe((0f, 0f, 1f));
        eye.Forward.ShouldBe((1f, 0f, 0f));
        eye.IrisScale.ShouldBe(1.5f);

        model.Meshes.Count.ShouldBe(2);
        model.Meshes[0].Eyeball.ShouldBe(0, "materialtype 1, materialparam 0");
        model.Meshes[1].Eyeball.ShouldBe(-1, "materialtype 0");

        model.Mouths.Count.ShouldBe(1);
        model.Mouths[0].ShouldBe(new StudioMouth(2, (0f, 1f, 0f), 7));
        model.MaxEyeDeflection.ShouldBe(0.5f);
    }

    [Test]
    public void Read_ASecondHeaderStatingNoDeflection_IsCosThirty()
    {
        StudioModel.Read(Model(maxEyeDeflection: 0f)).MaxEyeDeflection.ShouldBe(0.866f);
    }

    private static byte[] Model(float maxEyeDeflection)
    {
        byte[] file = new byte[2048];
        Span<byte> s = file;
        "IDST"u8.CopyTo(s);
        Int(s, 4, 48);

        Int(s, StudioLayout.HeaderBodyPartCountOffset, 1);
        Int(s, StudioLayout.HeaderBodyPartIndexOffset, 600);

        // The body part at 600, its one submodel 100 bytes on.
        Int(s, 600 + StudioLayout.BodyPartModelCountOffset, 1);
        Int(s, 600 + StudioLayout.BodyPartBaseOffset, 1);
        Int(s, 600 + StudioLayout.BodyPartModelIndexOffset, 100);

        const int Submodel = 700;
        Int(s, Submodel + StudioLayout.ModelMeshCountOffset, 2);
        Int(s, Submodel + StudioLayout.ModelMeshIndexOffset, 300);
        Int(s, Submodel + StudioLayout.ModelVertexCountOffset, 10);
        Int(s, Submodel + StudioLayout.ModelEyeballCountOffset, 1);
        Int(s, Submodel + StudioLayout.ModelEyeballIndexOffset, 600);

        const int Meshes = 1000;
        Int(s, Meshes + StudioLayout.MeshVertexCountOffset, 4);
        Int(s, Meshes + StudioLayout.MeshMaterialTypeOffset, 1);
        Int(s, Meshes + StudioLayout.MeshMaterialParameterOffset, 0);
        Int(s, Meshes + StudioLayout.MeshStride + StudioLayout.MeshMaterialOffset, 1);
        Int(s, Meshes + StudioLayout.MeshStride + StudioLayout.MeshVertexCountOffset, 6);
        Int(s, Meshes + StudioLayout.MeshStride + StudioLayout.MeshVertexOffset, 4);

        const int Eyeball = 1300;
        Int(s, Eyeball + StudioLayout.EyeballBoneOffset, 3);
        Vec(s, Eyeball + StudioLayout.EyeballOriginOffset, 1f, 2f, 3f);
        Float(s, Eyeball + StudioLayout.EyeballZOffsetOffset, 0.25f);
        Float(s, Eyeball + StudioLayout.EyeballRadiusOffset, 0.5f);
        Vec(s, Eyeball + StudioLayout.EyeballUpOffset, 0f, 0f, 1f);
        Vec(s, Eyeball + StudioLayout.EyeballForwardOffset, 1f, 0f, 0f);
        Float(s, Eyeball + StudioLayout.EyeballIrisScaleOffset, 1.5f);

        Int(s, StudioLayout.HeaderMouthCountOffset, 1);
        Int(s, StudioLayout.HeaderMouthIndexOffset, 1500);
        Int(s, 1500, 2);
        Vec(s, 1500 + StudioLayout.MouthForwardOffset, 0f, 1f, 0f);
        Int(s, 1500 + StudioLayout.MouthFlexDescriptorOffset, 7);

        Int(s, StudioLayout.HeaderSecondHeaderIndexOffset, 1600);
        Float(s, 1600 + StudioLayout.SecondHeaderMaxEyeDeflectionOffset, maxEyeDeflection);

        return file;
    }

    private static void Int(Span<byte> s, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(s[at..], value);

    private static void Float(Span<byte> s, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(s[at..], value);

    private static void Vec(Span<byte> s, int at, float x, float y, float z)
    {
        Float(s, at, x);
        Float(s, at + 4, y);
        Float(s, at + 8, z);
    }
}
