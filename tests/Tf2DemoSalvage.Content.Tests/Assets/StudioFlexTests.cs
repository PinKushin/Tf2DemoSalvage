using System;
using System.Buffers.Binary;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A model's flex tables read from a hand-built <c>.mdl</c> whose values the test put there (D38, B513).
/// </summary>
/// <remarks>
/// <c>VertexFlexConformanceTests</c> quotes the layout; the struct sizes are <c>studio.h:919-1188</c>.
/// </remarks>
public sealed class StudioFlexTests
{
    [Test]
    public void Read_AFloat16File_DecodesTheDeltasAndMakesTheVertexAbsolute()
    {
        StudioFlexData flex = StudioFlex.Read(Mdl(fixedScale: null));

        flex.Descriptors.ShouldBe(["left", "right"]);
        flex.Controllers.Count.ShouldBe(1);
        flex.Controllers[0].ShouldBe(new StudioFlexController("eyes", "lid", -1f, 1f));
        flex.Rules.Count.ShouldBe(1);
        flex.Rules[0].Flex.ShouldBe(1);
        flex.Rules[0].Ops[0].ShouldBe(new StudioFlexOp(1, BitConverter.SingleToInt32Bits(0.5f), 0.5f));
        flex.Rules[0].Ops[1].Op.ShouldBe(2);
        flex.Rules[0].Ops[1].Index.ShouldBe(0);

        StudioMeshFlex mesh = flex.Flexes.ShouldHaveSingleItem();
        mesh.FlexDesc.ShouldBe(0);
        mesh.FlexPair.ShouldBe(1);
        (mesh.Target0, mesh.Target1, mesh.Target2, mesh.Target3).ShouldBe((0f, 1f, 10f, 11f));

        // The model's vertex start (10) plus the mesh's offset (5) plus the anim's own index (3).
        mesh.Vertices[0].Vertex.ShouldBe(18);
        mesh.Vertices[0].Speed.ShouldBe((byte)255);
        mesh.Vertices[0].Side.ShouldBe((byte)64);
        mesh.Vertices[0].Delta.ShouldBe((1f, -2f, 0.5f));
        mesh.Vertices[0].NormalDelta.ShouldBe((0.25f, 0f, -1f));
        mesh.Vertices[1].Vertex.ShouldBe(19);
    }

    [Test]
    public void Read_AFixedPointFile_ScalesTheShortsByTheHeadersScale()
    {
        StudioMeshFlex mesh = StudioFlex.Read(Mdl(fixedScale: 0.25f)).Flexes.ShouldHaveSingleItem();

        // The same six bytes per vector now read as shorts 4, -8, 2 and 1, 0, -4, times 0.25.
        mesh.Vertices[0].Delta.ShouldBe((1f, -2f, 0.5f));
        mesh.Vertices[0].NormalDelta.ShouldBe((0.25f, 0f, -1f));
    }

    [Test]
    public void Read_AModelWithNoDescriptors_HasNoFace()
    {
        byte[] file = Mdl(fixedScale: null);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(260), 0);

        StudioFlex.Read(file).ShouldBeSameAs(StudioFlexData.None);
    }

    [Test]
    public void Read_FlexesRunningPastTheFile_Fails()
    {
        byte[] file = Mdl(fixedScale: null);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(MeshAt + 16), 1_000);

        Should.Throw<System.IO.InvalidDataException>(() => StudioFlex.Read(file));
    }

    private const int PartAt = 400;
    private const int ModelAt = 500;
    private const int MeshAt = 700;
    private const int FlexAt = 900;
    private const int AnimAt = 1000;
    private const int DescAt = 1100;
    private const int ControllerAt = 1300;
    private const int RuleAt = 1400;
    private const int OpsAt = 1450;
    private const int StringsAt = 1600;

    /// <summary>A model with one body part, one model, one mesh carrying one stereo flex of two vertices.</summary>
    private static byte[] Mdl(float? fixedScale)
    {
        byte[] file = new byte[2048];
        Span<byte> s = file;

        BinaryPrimitives.WriteInt32LittleEndian(s[152..], fixedScale is null ? 0 : StudioFlex.FixedPointScaleFlag);
        BinaryPrimitives.WriteSingleLittleEndian(s[392..], fixedScale ?? 0f);

        Int(s, 232, 1);
        Int(s, 236, PartAt);
        Int(s, PartAt + 4, 1);
        Int(s, PartAt + 12, ModelAt - PartAt);
        Int(s, ModelAt + 84, 10 * 48);
        Int(s, ModelAt + 72, 1);
        Int(s, ModelAt + 76, MeshAt - ModelAt);
        Int(s, MeshAt + 12, 5);
        Int(s, MeshAt + 16, 1);
        Int(s, MeshAt + 20, FlexAt - MeshAt);

        Int(s, FlexAt, 0);
        BinaryPrimitives.WriteSingleLittleEndian(s[(FlexAt + 4)..], 0f);
        BinaryPrimitives.WriteSingleLittleEndian(s[(FlexAt + 8)..], 1f);
        BinaryPrimitives.WriteSingleLittleEndian(s[(FlexAt + 12)..], 10f);
        BinaryPrimitives.WriteSingleLittleEndian(s[(FlexAt + 16)..], 11f);
        Int(s, FlexAt + 20, 2);
        Int(s, FlexAt + 24, AnimAt - FlexAt);
        Int(s, FlexAt + 28, 1);

        for (int anim = 0; anim < 2; anim++)
        {
            Span<byte> at = s[(AnimAt + (anim * 16))..];
            BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)(3 + anim));
            at[2] = 255;
            at[3] = 64;
            Vector(at[4..], fixedScale, 1f, -2f, 0.5f);
            Vector(at[10..], fixedScale, 0.25f, 0f, -1f);
        }

        int strings = StringsAt;
        Int(s, 260, 2);
        Int(s, 264, DescAt);
        Int(s, DescAt, String(s, ref strings, "left") - DescAt);
        Int(s, DescAt + 4, String(s, ref strings, "right") - (DescAt + 4));

        Int(s, 268, 1);
        Int(s, 272, ControllerAt);
        Int(s, ControllerAt, String(s, ref strings, "eyes") - ControllerAt);
        Int(s, ControllerAt + 4, String(s, ref strings, "lid") - ControllerAt);
        BinaryPrimitives.WriteSingleLittleEndian(s[(ControllerAt + 12)..], -1f);
        BinaryPrimitives.WriteSingleLittleEndian(s[(ControllerAt + 16)..], 1f);

        Int(s, 276, 1);
        Int(s, 280, RuleAt);
        Int(s, RuleAt, 1);
        Int(s, RuleAt + 4, 2);
        Int(s, RuleAt + 8, OpsAt - RuleAt);
        Int(s, OpsAt, 1);
        BinaryPrimitives.WriteSingleLittleEndian(s[(OpsAt + 4)..], 0.5f);
        Int(s, OpsAt + 8, 2);
        Int(s, OpsAt + 12, 0);

        return file;
    }

    private static void Int(Span<byte> s, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(s[at..], value);

    private static void Vector(Span<byte> at, float? scale, float x, float y, float z)
    {
        float[] values = [x, y, z];

        for (int axis = 0; axis < 3; axis++)
        {
            if (scale is { } fixedScale)
            {
                BinaryPrimitives.WriteInt16LittleEndian(at[(axis * 2)..], (short)(values[axis] / fixedScale));
            }
            else
            {
                BinaryPrimitives.WriteHalfLittleEndian(at[(axis * 2)..], (Half)values[axis]);
            }
        }
    }

    private static int String(Span<byte> s, ref int at, string text)
    {
        int start = at;
        Encoding.UTF8.GetBytes(text).CopyTo(s[at..]);
        at += Encoding.UTF8.GetByteCount(text) + 1;
        return start;
    }
}
