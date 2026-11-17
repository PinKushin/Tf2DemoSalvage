using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One protobuf message as wire fields, in the order they were written: enough of `ParsePartialFromArray`
/// (tf_proto_script_obj_def.cpp:209) to walk the compiled proto definitions without their generated classes.
/// </summary>
/// <remarks>
/// **Wire order is the order the compiled file holds**, which the serializer writes in field-number order. A reader that
/// must walk fields in the `.proto`'s declaration order asks for them by number. A singular field written twice answers
/// with its last value, as protobuf does; a singular MESSAGE written twice would be merged by protobuf, which nothing in
/// these files does. *Interpolated:* not measured on the shipped file.
/// </remarks>
public sealed class ProtoMessage
{
    private const int WireVarint = 0;
    private const int WireFixed64 = 1;
    private const int WireLengthDelimited = 2;
    private const int WireFixed32 = 5;

    private ProtoMessage(List<ProtoField> fields) => Fields = fields;

    /// <summary>The fields in file order.</summary>
    public IReadOnlyList<ProtoField> Fields { get; }

    /// <summary>The message in <paramref name="bytes"/>, or null when it is cut short.</summary>
    /// <param name="bytes">The message's bytes, kept by reference.</param>
    /// <returns>The message, or null.</returns>
    public static ProtoMessage? Parse(ReadOnlyMemory<byte> bytes)
    {
        List<ProtoField> fields = [];
        ReadOnlySpan<byte> span = bytes.Span;
        int at = 0;

        while (at < span.Length)
        {
            if (Varint(span, ref at) is not { } tag)
            {
                return null;
            }

            int number = (int)(tag >> 3);
            int wire = (int)(tag & 7);

            switch (wire)
            {
                case WireVarint:
                    if (Varint(span, ref at) is not { } value)
                    {
                        return null;
                    }

                    fields.Add(new ProtoField(number, wire, value, ReadOnlyMemory<byte>.Empty));
                    break;

                case WireFixed64:
                case WireFixed32:
                    int width = wire == WireFixed64 ? 8 : 4;

                    if (span.Length - at < width)
                    {
                        return null;
                    }

                    ulong bits = width == 8
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span[at..])
                        : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[at..]);
                    fields.Add(new ProtoField(number, wire, bits, ReadOnlyMemory<byte>.Empty));
                    at += width;
                    break;

                case WireLengthDelimited:
                    if (Varint(span, ref at) is not { } length || length > (ulong)(span.Length - at))
                    {
                        return null;
                    }

                    fields.Add(new ProtoField(number, wire, length, bytes.Slice(at, (int)length)));
                    at += (int)length;
                    break;

                default:
                    // Groups (3, 4) and the reserved types do not appear in these messages.
                    return null;
            }
        }

        return new ProtoMessage(fields);
    }

    /// <summary>Whether field <paramref name="number"/> was written.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>Whether it is present.</returns>
    public bool Has(int number)
    {
        foreach (ProtoField field in Fields)
        {
            if (field.Number == number)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A varint field's last value, or null.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>The value, or null.</returns>
    public ulong? Varint(int number) => Last(number, WireVarint) is { } field ? field.Value : null;

    /// <summary>A string field's last value as UTF-8, or null.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>The string, or null.</returns>
    public string? Text(int number) => Last(number, WireLengthDelimited) is { } field ? Encoding.UTF8.GetString(field.Payload.Span) : null;

    /// <summary>A singular message field's last value, or null when absent or cut short.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>The message, or null.</returns>
    public ProtoMessage? Message(int number) => Last(number, WireLengthDelimited) is { } field ? Parse(field.Payload) : null;

    /// <summary>A repeated message field's values in file order; a cut-short one is skipped.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>The messages.</returns>
    public IReadOnlyList<ProtoMessage> Messages(int number)
    {
        List<ProtoMessage> messages = [];

        foreach (ProtoField field in Fields)
        {
            if (field.Number == number && field.Wire == WireLengthDelimited && Parse(field.Payload) is { } message)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>A repeated string field's values in file order.</summary>
    /// <param name="number">The field number.</param>
    /// <returns>The strings.</returns>
    public IReadOnlyList<string> Texts(int number)
    {
        List<string> strings = [];

        foreach (ProtoField field in Fields)
        {
            if (field.Number == number && field.Wire == WireLengthDelimited)
            {
                strings.Add(Encoding.UTF8.GetString(field.Payload.Span));
            }
        }

        return strings;
    }

    private ProtoField? Last(int number, int wire)
    {
        ProtoField? last = null;

        foreach (ProtoField field in Fields)
        {
            if (field.Number == number && field.Wire == wire)
            {
                last = field;
            }
        }

        return last;
    }

    /// <summary>A base-128 varint of at most ten bytes, or null when cut short.</summary>
    private static ulong? Varint(ReadOnlySpan<byte> bytes, ref int at)
    {
        ulong value = 0;

        for (int shift = 0; shift < 70 && at < bytes.Length; shift += 7)
        {
            byte next = bytes[at++];
            value |= (ulong)(next & 0x7F) << shift;

            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        return null;
    }
}
