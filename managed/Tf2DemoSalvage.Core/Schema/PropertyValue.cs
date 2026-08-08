using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>Which of a <see cref="PropertyValue"/>'s payloads carries the value.</summary>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32",
    Justification = "byte is enough for six cases and keeps PropertyValue compact - one is " +
                    "allocated per changed property, of which a demo has tens of millions.")]
[SuppressMessage("Design", "CA1008:Enums should have zero value",
    Justification = "Int is the natural default and is already 0; a None member would be a " +
                    "state no decoded value can be in.")]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "These mirror SendPropType, which uses Valve's own names for the wire " +
                    "types. Renaming only here would break the correspondence.")]
public enum PropertyValueKind : byte
{
    /// <summary>A signed or unsigned integer.</summary>
    Int,

    /// <summary>A float, however it was encoded on the wire.</summary>
    Float,

    /// <summary>Three floats.</summary>
    Vector,

    /// <summary>Two floats.</summary>
    VectorXY,

    /// <summary>A string.</summary>
    String,

    /// <summary>A repeated value.</summary>
    Array,
}

/// <summary>
/// One decoded property value, tagged with which kind it holds.
/// </summary>
/// <remarks>
/// A tagged union rather than <c>object</c>, so reading a value as the wrong type is a
/// throw at the point of the mistake rather than a cast failure somewhere downstream. The
/// distinction matters more here than usual: nearly every value in this format is a number,
/// and confusing two numeric kinds produces a plausible reading rather than an error.
/// </remarks>
public readonly record struct PropertyValue
{
    private readonly int _int;
    private readonly float _x;
    private readonly float _y;
    private readonly float _z;
    private readonly string? _string;
    private readonly IReadOnlyList<PropertyValue>? _array;

    private PropertyValue(
        PropertyValueKind kind,
        int intValue = 0,
        float x = 0f,
        float y = 0f,
        float z = 0f,
        string? stringValue = null,
        IReadOnlyList<PropertyValue>? array = null)
    {
        Kind = kind;
        _int = intValue;
        _x = x;
        _y = y;
        _z = z;
        _string = stringValue;
        _array = array;
    }

    /// <summary>Which payload this value carries.</summary>
    public PropertyValueKind Kind { get; }

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromInt(int value) =>
        new(PropertyValueKind.Int, intValue: value);

    /// <summary>Creates a float value.</summary>
    /// <param name="value">The float.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromFloat(float value) =>
        new(PropertyValueKind.Float, x: value);

    /// <summary>Creates a three-component vector.</summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <param name="z">Third component.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromVector(float x, float y, float z) =>
        new(PropertyValueKind.Vector, x: x, y: y, z: z);

    /// <summary>Creates a two-component vector.</summary>
    /// <param name="x">First component.</param>
    /// <param name="y">Second component.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromVectorXY(float x, float y) =>
        new(PropertyValueKind.VectorXY, x: x, y: y);

    /// <summary>Creates a string value.</summary>
    /// <param name="value">The string.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromString(string value) =>
        new(PropertyValueKind.String, stringValue: value);

    /// <summary>Creates an array value.</summary>
    /// <param name="values">The elements.</param>
    /// <returns>The wrapped value.</returns>
    public static PropertyValue FromArray(IReadOnlyList<PropertyValue> values) =>
        new(PropertyValueKind.Array, array: values);

    /// <summary>The integer payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not an integer.</exception>
    public int AsInt => Kind == PropertyValueKind.Int
        ? _int
        : throw Mismatch(PropertyValueKind.Int);

    /// <summary>The float payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not a float.</exception>
    public float AsFloat => Kind == PropertyValueKind.Float
        ? _x
        : throw Mismatch(PropertyValueKind.Float);

    /// <summary>The three-component vector payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not a vector.</exception>
    public (float X, float Y, float Z) AsVector => Kind == PropertyValueKind.Vector
        ? (_x, _y, _z)
        : throw Mismatch(PropertyValueKind.Vector);

    /// <summary>The two-component vector payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not a two-component vector.</exception>
    public (float X, float Y) AsVectorXY => Kind == PropertyValueKind.VectorXY
        ? (_x, _y)
        : throw Mismatch(PropertyValueKind.VectorXY);

    /// <summary>The string payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not a string.</exception>
    public string AsString => Kind == PropertyValueKind.String && _string is not null
        ? _string
        : throw Mismatch(PropertyValueKind.String);

    /// <summary>The array payload.</summary>
    /// <exception cref="InvalidOperationException">This value is not an array.</exception>
    public IReadOnlyList<PropertyValue> AsArray => Kind == PropertyValueKind.Array && _array is not null
        ? _array
        : throw Mismatch(PropertyValueKind.Array);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        PropertyValueKind.Int => _int.ToString(CultureInfo.InvariantCulture),
        PropertyValueKind.Float => _x.ToString("0.###", CultureInfo.InvariantCulture),
        PropertyValueKind.Vector => string.Create(
            CultureInfo.InvariantCulture, $"({_x:0.###}, {_y:0.###}, {_z:0.###})"),
        PropertyValueKind.VectorXY => string.Create(
            CultureInfo.InvariantCulture, $"({_x:0.###}, {_y:0.###})"),
        PropertyValueKind.String => _string ?? string.Empty,
        _ => $"[{_array?.Count ?? 0}]",
    };

    private InvalidOperationException Mismatch(PropertyValueKind wanted) =>
        new($"Property value is {Kind}, not {wanted}.");
}
