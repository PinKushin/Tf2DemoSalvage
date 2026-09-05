using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Writes and reads a single entity property value as text.
/// </summary>
/// <remarks>
/// **Type-tagged, and the tag is for the reader rather than the parser.** Which of the six shapes
/// a value has is already known from the schema, so the parser could work without it — but a line
/// saying <c>v 1024 -512 96</c> tells somebody reading the file that those three numbers are one
/// position, and <c>i 100</c> that the health is an integer rather than a rounded float. The parser
/// checks the tag anyway, because a mismatch means the schema and the text disagree about what the
/// property is, and that is worth failing on rather than misreading.
///
/// **Floats use the round-trip format throughout.** A coordinate written to two decimal places
/// re-encodes to different bits, and the whole point of this text is that it does not.
/// </remarks>
public static class PropertyText
{
    /// <summary>Renders a value.</summary>
    /// <param name="flat">The property, which says how to read the value back.</param>
    /// <param name="value">The value.</param>
    /// <returns>The value as tokens, space-separated.</returns>
    /// <exception cref="InvalidDataException">The value's kind has no text form.</exception>
    public static string Write(FlatProperty flat, PropertyValue value) => value.Kind switch
    {
        PropertyValueKind.Int => string.Create(
            CultureInfo.InvariantCulture, $"i {value.AsInt}"),

        PropertyValueKind.Float => $"f {Round(value.AsFloat)}",

        PropertyValueKind.Vector => WriteVector(value),

        PropertyValueKind.VectorXY => string.Create(
            CultureInfo.InvariantCulture,
            $"v2 {Round(value.AsVectorXY.X)} {Round(value.AsVectorXY.Y)}"),

        PropertyValueKind.String => $"s {Quote(value.AsString)}",

        PropertyValueKind.Array => WriteArray(flat, value),

        _ => throw new InvalidDataException($"Property value kind {value.Kind} has no text form."),
    };

    /// <summary>Reads a value back, given the property that describes it.</summary>
    /// <param name="flat">The property.</param>
    /// <param name="tokens">The line's tokens.</param>
    /// <param name="index">Where the value starts.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The tokens do not describe a value.</exception>
    public static PropertyValue Read(FlatProperty flat, IReadOnlyList<string> tokens, int index)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return Read(flat, tokens, ref index);
    }

    private static PropertyValue Read(
        FlatProperty flat, IReadOnlyList<string> tokens, ref int index)
    {
        string tag = Take(tokens, ref index, "a value tag");

        switch (tag)
        {
            case "i":
                return PropertyValue.FromInt(AssemblyText.Wide(
                    Take(tokens, ref index, "an integer"), "integer", Subject));

            case "f":
                return PropertyValue.FromFloat(Real(tokens, ref index));

            case "v":
            {
                float x = Real(tokens, ref index);
                float y = Real(tokens, ref index);
                return PropertyValue.FromVector(x, y, Real(tokens, ref index));
            }

            case "v2":
            {
                float x = Real(tokens, ref index);
                return PropertyValue.FromVectorXY(x, Real(tokens, ref index));
            }

            case "s":
                return PropertyValue.FromString(Take(tokens, ref index, "a string"));

            case "a":
            {
                // **The declared length is not trusted.** It was passed straight to the list's
                // capacity, so `a 2000000000` raised OutOfMemoryException before a single element
                // was read (B345). An array cannot have more elements than the line has tokens
                // left, which is an exact ceiling rather than a tuned one.
                int count = AssemblyText.Count(
                    Take(tokens, ref index, "an element count"),
                    tokens.Count - index,
                    "elements",
                    Subject);

                List<PropertyValue> values = new(count);

                // The element template rather than the array itself, matching how the decoder
                // reads them: an array's elements have their own encoding, and using the array's
                // would be a different width for every one of them.
                FlatProperty element = new(
                    flat.ArrayElement ?? throw new InvalidDataException(
                        $"Array property '{flat.Property.Name}' has no element template."),
                    flat.OwnerTable,
                    null);

                for (int i = 0; i < count; i++)
                {
                    values.Add(Read(element, tokens, ref index));
                }

                return PropertyValue.FromArray(values);
            }

            default:
                throw new InvalidDataException($"'{tag}' is not a property value tag.");
        }
    }

    private static string WriteVector(PropertyValue value)
    {
        (float x, float y, float z) = value.AsVector;
        return string.Create(
            CultureInfo.InvariantCulture, $"v {Round(x)} {Round(y)} {Round(z)}");
    }

    private static string WriteArray(FlatProperty flat, PropertyValue value)
    {
        IReadOnlyList<PropertyValue> values = value.AsArray;
        StringBuilder text = new("a ");
        text.Append(values.Count.ToString(CultureInfo.InvariantCulture));

        FlatProperty element = new(
            flat.ArrayElement ?? throw new InvalidDataException(
                $"Array property '{flat.Property.Name}' has no element template."),
            flat.OwnerTable,
            null);

        foreach (PropertyValue item in values)
        {
            text.Append(' ').Append(Write(element, item));
        }

        return text.ToString();
    }

    /// <summary>What a refusal from this file calls the thing it was reading.</summary>
    private const string Subject = "A property value";

    private static float Real(IReadOnlyList<string> tokens, ref int index) =>
        AssemblyText.Real(Take(tokens, ref index, "a number"), "number", Subject);

    /// <summary>The next token, or a refusal saying what the line stopped short of.</summary>
    private static string Take(IReadOnlyList<string> tokens, ref int index, string what) =>
        AssemblyText.Token(tokens, index++, what, Subject);

    private static string Round(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
