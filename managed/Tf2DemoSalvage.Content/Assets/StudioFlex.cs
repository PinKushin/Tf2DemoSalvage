using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One flex controller — an input slider — as <c>mstudioflexcontroller_t</c> stores it.</summary>
/// <param name="Type">Its group, <c>pszType</c> — <c>eyes</c>, <c>phoneme</c>, <c>default</c>.</param>
/// <param name="Name">Its name, which is how scenes and the engine's global list address it.</param>
/// <param name="Min">The bottom of its range.</param>
/// <param name="Max">The top of its range.</param>
public readonly record struct StudioFlexController(string Type, string Name, float Min, float Max);

/// <summary>One step of a flex rule, <c>mstudioflexop_t</c> (<c>studio.h:1170</c>).</summary>
/// <param name="Op">A <c>STUDIO_*</c> opcode (<c>studio.h:3035-3055</c>).</param>
/// <param name="Index">The union read as an int — a controller, a descriptor, or a count.</param>
/// <param name="Value">The same four bytes read as a float, which only <c>STUDIO_CONST</c> uses.</param>
public readonly record struct StudioFlexOp(int Op, int Index, float Value);

/// <summary>One flex rule: a stack program whose result becomes one descriptor's weight.</summary>
/// <param name="Flex">The descriptor the result lands in.</param>
/// <param name="Ops">The program.</param>
public sealed record StudioFlexRule(int Flex, IReadOnlyList<StudioFlexOp> Ops);

/// <summary>One vertex's share of a flex, <c>mstudiovertanim_t</c> (<c>studio.h:1008</c>), decoded.</summary>
/// <param name="Vertex">The vertex, ABSOLUTE in the model's <c>.vvd</c> (the mesh's start added).</param>
/// <param name="Speed">0-255: how much of the current weight rather than the delayed one it takes.</param>
/// <param name="Side">0-255: how much of the partner (right) descriptor rather than its own it takes.</param>
/// <param name="Delta">The position delta at full weight.</param>
/// <param name="NormalDelta">The normal delta at full weight.</param>
public readonly record struct StudioVertAnim(
    int Vertex,
    byte Speed,
    byte Side,
    (float X, float Y, float Z) Delta,
    (float X, float Y, float Z) NormalDelta);

/// <summary>One mesh's vertex animation for one descriptor, <c>mstudioflex_t</c> (<c>studio.h:1144</c>).</summary>
/// <param name="FlexDesc">The descriptor whose weight drives it.</param>
/// <param name="FlexPair">The partner descriptor for a stereo flex, or zero for none.</param>
/// <param name="Target0">Below this the flex is off.</param>
/// <param name="Target1">From here to <paramref name="Target2"/> it is fully on.</param>
/// <param name="Target2">See <paramref name="Target1"/>.</param>
/// <param name="Target3">Above this it is off again.</param>
/// <param name="Vertices">The vertices it moves.</param>
public sealed record StudioMeshFlex(
    int FlexDesc,
    int FlexPair,
    float Target0,
    float Target1,
    float Target2,
    float Target3,
    IReadOnlyList<StudioVertAnim> Vertices);

/// <summary>Everything a model says about its face: descriptors, controllers, rules, and the deltas.</summary>
/// <param name="Descriptors">FACS names, one per descriptor — the rules' outputs.</param>
/// <param name="Controllers">The input sliders.</param>
/// <param name="Rules">The programs from controllers to descriptors, in file order.</param>
/// <param name="Flexes">Every mesh's flexes, flattened; a vertex index is already absolute.</param>
public sealed record StudioFlexData(
    IReadOnlyList<string> Descriptors,
    IReadOnlyList<StudioFlexController> Controllers,
    IReadOnlyList<StudioFlexRule> Rules,
    IReadOnlyList<StudioMeshFlex> Flexes)
{
    /// <summary>A model with no face.</summary>
    public static StudioFlexData None { get; } = new([], [], [], []);

    /// <summary>Whether there is anything to move.</summary>
    public bool HasVertexAnimation => Flexes.Count > 0 && Descriptors.Count > 0;

    /// <summary>A controller's index by name, or -1 — <c>C_BaseFlex::FindFlexController</c>.</summary>
    /// <param name="name">The name, compared without case as the engine's global list does.</param>
    /// <returns>The local index.</returns>
    public int FindController(string name)
    {
        for (int index = 0; index < Controllers.Count; index++)
        {
            if (string.Equals(Controllers[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>
/// Reads a model's flex tables (vertex flex): <c>numflexdesc</c>, <c>numflexcontrollers</c>,
/// <c>numflexrules</c> from the header and <c>numflexes</c> from every mesh.
/// </summary>
/// <remarks>
/// **The deltas are float16 on disk unless the header says fixed point.** <c>mstudiovertanim_t</c> stores
/// a union of <c>short delta[3]</c> and <c>float16 flDelta[3]</c> (<c>studio.h:1017-1027</c>), and the flag
/// <c>STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE</c> is "flagged on load" (<c>studio.h:2091</c>): the
/// engine converts the file's float16 to fixed with <c>ConvertToFixed</c> and sets it. So a file that
/// does not carry the flag holds float16, and one that does holds shorts scaled by
/// <c>VertAnimFixedPointScale()</c> (<c>studio.h:2395</c>). Reading float16 directly skips the engine's
/// requantisation, whose step is below a hundredth of a unit — interpolated, not measured.
/// </remarks>
public static class StudioFlex
{
    /// <summary><c>STUDIOHDR_FLAGS_VERT_ANIM_FIXED_POINT_SCALE</c>, <c>studio.h:2092</c>.</summary>
    public const int FixedPointScaleFlag = 0x00200000;

    /// <summary><c>STUDIO_VERT_ANIM_WRINKLE</c>, <c>studio.h:1141</c>.</summary>
    private const byte WrinkleType = 1;

    /// <summary>The most of any one table this reader builds from an untrusted file (D32).</summary>
    private const int MaximumCount = 1 << 20;

    /// <summary>Reads the flex tables.</summary>
    /// <param name="file">The <c>.mdl</c>'s bytes.</param>
    /// <returns>The tables, or <see cref="StudioFlexData.None"/> for a model without them.</returns>
    /// <exception cref="InvalidDataException">A table runs past the file.</exception>
    public static StudioFlexData Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderVertAnimScaleOffset + 4)
        {
            return StudioFlexData.None;
        }

        int flags = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderFlagsOffset..]);
        float? scale = (flags & FixedPointScaleFlag) != 0
            ? BinaryPrimitives.ReadSingleLittleEndian(bytes[HeaderVertAnimScaleOffset..])
            : null;

        int descCount = Table(bytes, HeaderFlexDescCountOffset, FlexDescStride, out int descAt);
        int controllerCount = Table(bytes, HeaderFlexControllerCountOffset, FlexControllerStride, out int controllerAt);
        int ruleCount = Table(bytes, HeaderFlexRuleCountOffset, FlexRuleStride, out int ruleAt);

        if (descCount == 0)
        {
            return StudioFlexData.None;
        }

        List<string> descs = new(descCount);

        for (int index = 0; index < descCount; index++)
        {
            int at = descAt + (index * FlexDescStride);
            descs.Add(StudioStrings.At(bytes, at + BinaryPrimitives.ReadInt32LittleEndian(bytes[at..])));
        }

        List<StudioFlexController> controllers = new(controllerCount);

        for (int index = 0; index < controllerCount; index++)
        {
            int at = controllerAt + (index * FlexControllerStride);
            controllers.Add(new StudioFlexController(
                StudioStrings.At(bytes, at + BinaryPrimitives.ReadInt32LittleEndian(bytes[at..])),
                StudioStrings.At(bytes, at + BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..])),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 12)..]),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 16)..])));
        }

        List<StudioFlexRule> rules = new(ruleCount);

        for (int index = 0; index < ruleCount; index++)
        {
            int at = ruleAt + (index * FlexRuleStride);
            int ops = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
            int opsAt = at + BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 8)..]);
            Fits(bytes, opsAt, ops, FlexOpStride, "flex ops");

            List<StudioFlexOp> program = new(ops);

            for (int op = 0; op < ops; op++)
            {
                ReadOnlySpan<byte> entry = bytes.Slice(opsAt + (op * FlexOpStride), FlexOpStride);
                program.Add(new StudioFlexOp(
                    BinaryPrimitives.ReadInt32LittleEndian(entry),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(entry[4..])));
            }

            rules.Add(new StudioFlexRule(BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]), program));
        }

        return new StudioFlexData(descs, controllers, rules, ReadMeshFlexes(bytes, scale));
    }

    private static List<StudioMeshFlex> ReadMeshFlexes(ReadOnlySpan<byte> bytes, float? scale)
    {
        List<StudioMeshFlex> flexes = [];

        int parts = Table(bytes, HeaderBodyPartCountOffset, BodyPartStride, out int partsAt);

        for (int part = 0; part < parts; part++)
        {
            int partAt = partsAt + (part * BodyPartStride);
            int models = BinaryPrimitives.ReadInt32LittleEndian(bytes[(partAt + BodyPartModelCountOffset)..]);
            int modelsAt = partAt + BinaryPrimitives.ReadInt32LittleEndian(bytes[(partAt + BodyPartModelIndexOffset)..]);
            Fits(bytes, modelsAt, models, ModelStride, "models");

            for (int model = 0; model < models; model++)
            {
                int modelAt = modelsAt + (model * ModelStride);

                // The same byte-offset-to-vertex conversion StudioModel.ReadModelMeshes makes.
                int firstVertex =
                    BinaryPrimitives.ReadInt32LittleEndian(bytes[(modelAt + ModelVertexIndexOffset)..]) / VertexStride;
                int meshes = BinaryPrimitives.ReadInt32LittleEndian(bytes[(modelAt + ModelMeshCountOffset)..]);
                int meshesAt = modelAt + BinaryPrimitives.ReadInt32LittleEndian(bytes[(modelAt + ModelMeshIndexOffset)..]);
                Fits(bytes, meshesAt, meshes, MeshStride, "meshes");

                for (int mesh = 0; mesh < meshes; mesh++)
                {
                    int meshAt = meshesAt + (mesh * MeshStride);
                    int meshFirst = firstVertex +
                        BinaryPrimitives.ReadInt32LittleEndian(bytes[(meshAt + MeshVertexOffset)..]);
                    int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[(meshAt + MeshFlexCountOffset)..]);
                    int at = meshAt + BinaryPrimitives.ReadInt32LittleEndian(bytes[(meshAt + MeshFlexIndexOffset)..]);

                    if (count == 0)
                    {
                        continue;
                    }

                    Fits(bytes, at, count, FlexStride, "flexes");

                    for (int flex = 0; flex < count; flex++)
                    {
                        flexes.Add(ReadFlex(bytes, at + (flex * FlexStride), meshFirst, scale));
                    }
                }
            }
        }

        return flexes;
    }

    private static StudioMeshFlex ReadFlex(ReadOnlySpan<byte> bytes, int at, int meshFirst, float? scale)
    {
        ReadOnlySpan<byte> flex = bytes.Slice(at, FlexStride);
        int vertices = BinaryPrimitives.ReadInt32LittleEndian(flex[20..]);
        int verticesAt = at + BinaryPrimitives.ReadInt32LittleEndian(flex[24..]);

        // **A wrinkle vertanim is two bytes longer** (`VertAnimSizeBytes`, studio.h:1161), so its type
        // decides the stride even though the wrinkle itself is not read here.
        int stride = flex[32] == WrinkleType ? VertAnimWrinkleStride : VertAnimStride;
        Fits(bytes, verticesAt, vertices, stride, "vertex animations");

        StudioVertAnim[] anims = new StudioVertAnim[vertices];

        for (int index = 0; index < vertices; index++)
        {
            ReadOnlySpan<byte> anim = bytes.Slice(verticesAt + (index * stride), VertAnimStride);
            anims[index] = new StudioVertAnim(
                meshFirst + BinaryPrimitives.ReadUInt16LittleEndian(anim),
                anim[2],
                anim[3],
                Three(anim[4..], scale),
                Three(anim[10..], scale));
        }

        return new StudioMeshFlex(
            BinaryPrimitives.ReadInt32LittleEndian(flex),
            BinaryPrimitives.ReadInt32LittleEndian(flex[28..]),
            BinaryPrimitives.ReadSingleLittleEndian(flex[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(flex[8..]),
            BinaryPrimitives.ReadSingleLittleEndian(flex[12..]),
            BinaryPrimitives.ReadSingleLittleEndian(flex[16..]),
            anims);
    }

    /// <summary>Three float16s, or three shorts times the fixed-point scale (<c>GetDeltaFixed</c>).</summary>
    private static (float X, float Y, float Z) Three(ReadOnlySpan<byte> at, float? scale) =>
        scale is { } fixedScale
            ? (BinaryPrimitives.ReadInt16LittleEndian(at) * fixedScale,
               BinaryPrimitives.ReadInt16LittleEndian(at[2..]) * fixedScale,
               BinaryPrimitives.ReadInt16LittleEndian(at[4..]) * fixedScale)
            : ((float)BinaryPrimitives.ReadHalfLittleEndian(at),
               (float)BinaryPrimitives.ReadHalfLittleEndian(at[2..]),
               (float)BinaryPrimitives.ReadHalfLittleEndian(at[4..]));

    /// <summary>A header count and its file-relative index, checked to fit.</summary>
    private static int Table(ReadOnlySpan<byte> bytes, int countAt, int stride, out int at)
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[countAt..]);
        at = BinaryPrimitives.ReadInt32LittleEndian(bytes[(countAt + 4)..]);

        if (count <= 0)
        {
            return 0;
        }

        Fits(bytes, at, count, stride, "flex tables");
        return count;
    }

    private static void Fits(ReadOnlySpan<byte> bytes, int at, int count, int stride, string what)
    {
        if (count < 0 || count > MaximumCount || at < 0 || (long)at + ((long)count * stride) > bytes.Length)
        {
            throw new InvalidDataException($"A model puts {count} {what} at {at} of {bytes.Length} bytes.");
        }
    }
}
