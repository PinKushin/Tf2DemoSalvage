using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Valve's attribute types, as <c>dmattributetypes.h</c> numbers them.</summary>
/// <remarks>
/// **Transcribed from <c>src/public/datamodel/dmattributetypes.h:66</c>**, which is the only part of
/// the DMX story the SDK ships: the headers are public and `src/dmxloader/*.cpp` is not. So the
/// TYPES are read from source and the LAYOUT is read from the files (B373).
///
/// **The array types are the value types plus a fixed offset**, because Valve declares
/// <c>AT_ELEMENT_ARRAY = AT_FIRST_ARRAY_TYPE</c> immediately after <c>AT_VMATRIX</c> — so an array
/// type is its element type + 14, and a reader that hardcodes twenty-nine cases instead is one
/// renumbering away from silently misreading every file.
/// </remarks>
public enum DmxAttributeType
{
    /// <summary><c>AT_UNKNOWN</c>.</summary>
    Unknown = 0,

    /// <summary><c>AT_ELEMENT</c> — an index into the file's own element list.</summary>
    Element = 1,

    /// <summary><c>AT_INT</c>. Named <c>Whole</c> because CA1720 refuses <c>Integer</c>.</summary>
    Whole = 2,

    /// <summary><c>AT_FLOAT</c>. Named <c>Real</c> because CA1720 refuses <c>Float</c>.</summary>
    Real = 3,

    /// <summary><c>AT_BOOL</c>.</summary>
    Boolean = 4,

    /// <summary><c>AT_STRING</c>. Named <c>Text</c> because CA1720 refuses <c>String</c>.</summary>
    Text = 5,

    /// <summary><c>AT_VOID</c> — a length-prefixed blob.</summary>
    Void = 6,

    /// <summary><c>AT_OBJECTID</c> — sixteen bytes.</summary>
    ObjectId = 7,

    /// <summary><c>AT_COLOR</c> — four bytes, RGBA.</summary>
    Colour = 8,

    /// <summary><c>AT_VECTOR2</c>.</summary>
    Vector2 = 9,

    /// <summary><c>AT_VECTOR3</c>.</summary>
    Vector3 = 10,

    /// <summary><c>AT_VECTOR4</c>.</summary>
    Vector4 = 11,

    /// <summary><c>AT_QANGLE</c>.</summary>
    Angle = 12,

    /// <summary><c>AT_QUATERNION</c>.</summary>
    Quaternion = 13,

    /// <summary><c>AT_VMATRIX</c> — sixteen floats.</summary>
    Matrix = 14,

    /// <summary><c>AT_FIRST_ARRAY_TYPE</c>, and <c>AT_ELEMENT_ARRAY</c> with it.</summary>
    FirstArray = 15,
}

/// <summary>One attribute's value, whatever kind it is.</summary>
/// <param name="Type">Which kind, as the file declared it.</param>
/// <param name="Number">The value for a numeric or boolean kind.</param>
/// <param name="Text">The value for a string.</param>
/// <param name="Vector">The value for a vector, colour, angle or quaternion kind.</param>
/// <param name="Elements">The referenced element indices, for an element or element array.</param>
/// <param name="Numbers">The values of a numeric array.</param>
/// <remarks>
/// **One record rather than a class hierarchy**, because a particle definition is read by asking
/// for a named attribute and taking the kind it turns out to be. A hierarchy would put a cast at
/// every call site to answer the same question.
/// </remarks>
public readonly record struct DmxValue(
    DmxAttributeType Type,
    double Number = 0d,
    string? Text = null,
    Vector4 Vector = default,
    IReadOnlyList<int>? Elements = null,
    IReadOnlyList<double>? Numbers = null);

/// <summary>One element: a typed, named bag of attributes.</summary>
/// <param name="Type">Its type name, such as <c>DmeParticleSystemDefinition</c>.</param>
/// <param name="Name">Its own name, such as <c>rockettrail</c>.</param>
/// <param name="Attributes">Its attributes, by name.</param>
public sealed record DmxElement(
    string Type,
    string Name,
    IReadOnlyDictionary<string, DmxValue> Attributes);

/// <summary>
/// A binary DMX file — the container TF2's particle definitions ship in (B373).
/// </summary>
/// <remarks>
/// **The header is plain text and says exactly what to expect**, which is why this refuses rather
/// than guesses when it says something else:
///
/// <code>
/// &lt;!-- dmx encoding binary 2 format pcf 1 --&gt;
/// </code>
///
/// **The layout is read from the FILES, because the SDK ships dmxloader's headers and not its
/// implementation.** Measured on `particles/rockettrail.pcf`, 118,126 bytes:
///
/// <code>
/// 0      the header line, null-terminated                    (ends at 45)
/// 45     uint16 string count                                 (183)
/// 47     that many null-terminated strings                   (ends at 3554)
/// 3554   int32 element count                                 (755)
/// 3558   per element: uint16 type index, inline name, 16-byte id
/// then   per element, in the same order: int32 attribute count,
///        then each attribute as uint16 name index, byte type, value
/// </code>
///
/// **An element NAME is inline and an element TYPE is an index**, which is the asymmetry a reader
/// gets wrong first. It is visible in the bytes: element 0 is type index 0 (`DmeElement`) followed
/// by the literal `untitled`, and element 1 is type index 4 (`DmeParticleSystemDefinition`)
/// followed by the literal `rockettrail_`.
///
/// **A stranger's file (D32)**, so every read is bounds-checked and a structure that walks outside
/// the blob yields what was read so far rather than throwing on arithmetic.
/// </remarks>
public static class DmxFile
{
    /// <summary>The encodings this reads. Anything else is refused rather than guessed at.</summary>
    private const string SupportedHeader = "binary 2";

    /// <summary>Reads a binary DMX file's elements.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>Its elements, in file order, or an empty list when it is not one this reads.</returns>
    /// <remarks>
    /// **Order is preserved because an element reference IS an index into it.** Sorting or
    /// de-duplicating this list would silently repoint every reference in the file.
    /// </remarks>
    public static IReadOnlyList<DmxElement> Read(ReadOnlySpan<byte> file)
    {
        int start = file.IndexOf((byte)0);

        if (start < 0)
        {
            return [];
        }

        string header = Encoding.ASCII.GetString(file[..start]);

        if (!header.Contains(SupportedHeader, StringComparison.Ordinal))
        {
            return [];
        }

        int at = start + 1;

        if (at + 2 > file.Length)
        {
            return [];
        }

        // The string table, which every name and type indexes.
        int strings = BitConverter.ToUInt16(file[at..]);
        at += 2;

        List<string> table = new(strings);

        for (int index = 0; index < strings; index++)
        {
            if (ReadString(file, ref at) is not { } text)
            {
                return [];
            }

            table.Add(text);
        }

        if (at + 4 > file.Length)
        {
            return [];
        }

        int count = BitConverter.ToInt32(file[at..]);
        at += 4;

        if (count < 0)
        {
            return [];
        }

        // **Two passes, because the file is written that way**: every element's identity first, then
        // every element's attributes in the same order. An attribute can reference an element that
        // appears later, so the identities have to exist before any of them are filled in.
        List<(string Type, string Name)> identities = new(count);

        for (int index = 0; index < count; index++)
        {
            if (at + 2 > file.Length)
            {
                return [];
            }

            int type = BitConverter.ToUInt16(file[at..]);
            at += 2;

            if (ReadString(file, ref at) is not { } name)
            {
                return [];
            }

            // The sixteen-byte id, which nothing here needs: an element is referenced by INDEX
            // within a file, and this reader does not join files together.
            at += 16;

            identities.Add((type < table.Count ? table[type] : string.Empty, name));
        }

        List<DmxElement> elements = new(count);

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, DmxValue> attributes = new(StringComparer.Ordinal);

            if (at + 4 > file.Length)
            {
                break;
            }

            int attributeCount = BitConverter.ToInt32(file[at..]);
            at += 4;

            for (int attribute = 0; attribute < attributeCount; attribute++)
            {
                if (at + 3 > file.Length)
                {
                    break;
                }

                int named = BitConverter.ToUInt16(file[at..]);
                at += 2;

                DmxAttributeType type = (DmxAttributeType)file[at];
                at += 1;

                if (ReadValue(file, ref at, type) is not { } value)
                {
                    break;
                }

                if (named < table.Count)
                {
                    attributes[table[named]] = value;
                }
            }

            elements.Add(new DmxElement(
                identities[index].Type, identities[index].Name, attributes));
        }

        return elements;
    }

    /// <summary>Reads one null-terminated string, advancing past it.</summary>
    private static string? ReadString(ReadOnlySpan<byte> file, ref int at)
    {
        if (at >= file.Length)
        {
            return null;
        }

        int end = file[at..].IndexOf((byte)0);

        if (end < 0)
        {
            return null;
        }

        string text = Encoding.UTF8.GetString(file.Slice(at, end));
        at += end + 1;

        return text;
    }

    /// <summary>Reads one attribute value of a declared type, advancing past it.</summary>
    /// <remarks>
    /// **An ARRAY is its element type plus fourteen**, per <c>AT_FIRST_ARRAY_TYPE</c>, so the array
    /// cases are derived rather than enumerated — see <see cref="DmxAttributeType"/>.
    /// </remarks>
    private static DmxValue? ReadValue(ReadOnlySpan<byte> file, ref int at, DmxAttributeType type)
    {
        if (type >= DmxAttributeType.FirstArray)
        {
            DmxAttributeType inner = type - (int)DmxAttributeType.FirstArray + 1;

            if (at + 4 > file.Length)
            {
                return null;
            }

            int count = BitConverter.ToInt32(file[at..]);
            at += 4;

            if (count < 0)
            {
                return null;
            }

            List<int> references = [];
            List<double> numbers = [];

            for (int index = 0; index < count; index++)
            {
                if (ReadValue(file, ref at, inner) is not { } one)
                {
                    return null;
                }

                if (inner == DmxAttributeType.Element)
                {
                    references.Add((int)one.Number);
                }
                else
                {
                    numbers.Add(one.Number);
                }
            }

            return new DmxValue(type, Elements: references, Numbers: numbers);
        }

        switch (type)
        {
            case DmxAttributeType.Element:
            case DmxAttributeType.Whole:
                return Fixed(file, ref at, 4, span =>
                    new DmxValue(type, BitConverter.ToInt32(span)));

            case DmxAttributeType.Real:
                return Fixed(file, ref at, 4, span =>
                    new DmxValue(type, BitConverter.ToSingle(span)));

            case DmxAttributeType.Boolean:
                return Fixed(file, ref at, 1, span => new DmxValue(type, span[0]));

            case DmxAttributeType.Text:
                return ReadString(file, ref at) is { } text
                    ? new DmxValue(type, Text: text)
                    : null;

            case DmxAttributeType.Void:
                {
                    if (at + 4 > file.Length)
                    {
                        return null;
                    }

                    int length = BitConverter.ToInt32(file[at..]);
                    at += 4;

                    if (length < 0 || at + length > file.Length)
                    {
                        return null;
                    }

                    at += length;
                    return new DmxValue(type, length);
                }

            case DmxAttributeType.ObjectId:
                return Fixed(file, ref at, 16, _ => new DmxValue(type));

            case DmxAttributeType.Colour:
                return Fixed(file, ref at, 4, span => new DmxValue(
                    type, Vector: new Vector4(span[0], span[1], span[2], span[3])));

            case DmxAttributeType.Vector2:
                return Fixed(file, ref at, 8, span => new DmxValue(type, Vector: new Vector4(
                    BitConverter.ToSingle(span), BitConverter.ToSingle(span[4..]), 0f, 0f)));

            case DmxAttributeType.Vector3:
            case DmxAttributeType.Angle:
                return Fixed(file, ref at, 12, span => new DmxValue(type, Vector: new Vector4(
                    BitConverter.ToSingle(span),
                    BitConverter.ToSingle(span[4..]),
                    BitConverter.ToSingle(span[8..]),
                    0f)));

            case DmxAttributeType.Vector4:
            case DmxAttributeType.Quaternion:
                return Fixed(file, ref at, 16, span => new DmxValue(type, Vector: new Vector4(
                    BitConverter.ToSingle(span),
                    BitConverter.ToSingle(span[4..]),
                    BitConverter.ToSingle(span[8..]),
                    BitConverter.ToSingle(span[12..]))));

            case DmxAttributeType.Matrix:
                return Fixed(file, ref at, 64, _ => new DmxValue(type));

            default:
                // **An unknown type cannot be skipped, because its WIDTH is unknown.** Stopping is
                // the only honest answer: guessing a size would resynchronise onto rubbish and
                // report it as data.
                return null;
        }
    }

    /// <summary>Reads a fixed-width value, advancing past it, or null when it would overrun.</summary>
    private static DmxValue? Fixed(
        ReadOnlySpan<byte> file, ref int at, int width, ReadValueOf read)
    {
        if (at + width > file.Length)
        {
            return null;
        }

        DmxValue value = read(file.Slice(at, width));
        at += width;

        return value;
    }

    /// <summary>Turns a fixed-width span into a value.</summary>
    private delegate DmxValue ReadValueOf(ReadOnlySpan<byte> span);

    /// <summary>What a file's header says it is, for a report or a refusal.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The header line, or an empty string when there is none.</returns>
    public static string HeaderOf(ReadOnlySpan<byte> file)
    {
        int end = file.IndexOf((byte)0);

        return end < 0
            ? string.Empty
            : Encoding.ASCII.GetString(file[..end]);
    }

    /// <summary>A one-line census of a file, for a probe.</summary>
    /// <param name="elements">What <see cref="Read"/> returned.</param>
    /// <returns>How many elements, and how many of each type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="elements"/> is null.</exception>
    public static string Census(IReadOnlyList<DmxElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Dictionary<string, int> byType = new(StringComparer.Ordinal);

        foreach (DmxElement element in elements)
        {
            byType[element.Type] = byType.TryGetValue(element.Type, out int already)
                ? already + 1
                : 1;
        }

        StringBuilder report = new();

        report.Append(CultureInfo.InvariantCulture, $"{elements.Count} elements");

        foreach ((string type, int count) in byType)
        {
            report.Append(CultureInfo.InvariantCulture, $", {count} {type}");
        }

        return report.ToString();
    }
}
